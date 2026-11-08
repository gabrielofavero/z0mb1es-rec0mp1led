using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Port.Hooks;

/// <summary>Imports desktop image thumbnails into the original creator source list.</summary>
public static class DesktopPictures
{
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int ThumbnailSize = 256;
    private const int MaximumPictures = 256;
    private static readonly HashSet<Texture2D> OwnedTextures = [];
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg" };
    private static PendingPicker? pending;
    private static bool refreshingMapper;
    private static readonly BrowseCallback BringPickerForward = OnBrowseEvent;
    private static string FolderFile => Path.Combine(DesktopStorage.SaveDirectory, "pictures-folder.txt");
    private const string ChooseFolderLabel = "CHOOSE PICTURE FOLDER";

    public static bool ImportFolder(object manager, string folder) => Import(manager, folder);

    public static void LoadRememberedFolder(object manager)
    {
        if (!File.Exists(FolderFile) ||
            (int)manager.GetType().GetMethod("GetSrcImageCount", InstanceFields)!.Invoke(manager, null)! > 0)
            return;
        try
        {
            if (Import(manager, File.ReadAllText(FolderFile).Trim()))
                DesktopStorage.ReapplyCustom(manager);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Could not reload picture folder: {error.Message}");
        }
    }

    public static void OfferFolderButton(object mapper)
    {
        var type = mapper.GetType();
        var fields = type.BaseType!;
        var itemsField = fields.GetField("item", InstanceFields)!;
        var items = (Array?)itemsField.GetValue(mapper);
        var itemType = type.Assembly.GetType("ZombiesWP7.menu.MenuItem", true)!;
        var constructor = itemType.GetConstructor([typeof(string), typeof(Vector2), typeof(float)])!;
        var gameType = type.Assembly.GetType("ZombiesWP7.Game1", true)!;
        var menuScale = (float)gameType.GetField("MENUSCALE", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(null)!;
        var center = DesktopDisplay.SceneSize.X / (2f * menuScale);
        var hasPictures = (bool)type.GetField("havePics", InstanceFields)!.GetValue(mapper)!;
        var button = constructor.Invoke([ChooseFolderLabel, new Vector2(hasPictures ? center + 95f : center, hasPictures ? 280f : 245f), 0.55f]);
        var next = Array.CreateInstance(itemType, (items?.Length ?? 0) + 1);
        if (items is not null) Array.Copy(items, next, items.Length);
        next.SetValue(button, next.Length - 1);
        itemsField.SetValue(mapper, next);
        if (!hasPictures)
            fields.GetField("infos", InstanceFields)!.SetValue(mapper,
                new[] { new StringBuilder(), new StringBuilder(),
                        new StringBuilder("Choose a folder with PNG or JPEG pictures."),
                        new StringBuilder("You can add custom monster sprites from them.") });
    }

    public static bool HandleMapperClick(object manager, object mapper, string label)
    {
        if (label != ChooseFolderLabel) return false;
        ChooseFolder(manager, mapper);
        return true;
    }

    public static void ChooseFolder(object manager, object mapper)
    {
        if (refreshingMapper || pending is not null || !OperatingSystem.IsWindows()) return;
        Trace.TraceInformation("Opening picture folder picker for Mega Game Creator 2000.");
        pending = new PendingPicker(manager, mapper, StartBrowse(DesktopDisplay.GameWindowHandle));
    }

    public static void Update()
    {
        var picker = pending;
        if (picker is null || !picker.Result.IsCompleted) return;
        pending = null;
        try
        {
            var folder = picker.Result.GetAwaiter().GetResult();
            if (folder is null)
            {
                Trace.TraceInformation("Picture folder picker was canceled.");
                if (File.Exists(FolderFile) && Import(picker.Manager, File.ReadAllText(FolderFile).Trim()))
                    DesktopStorage.ReapplyCustom(picker.Manager);
            }
            else if (!Import(picker.Manager, folder))
            {
                MessageBoxW(DesktopDisplay.GameWindowHandle,
                    "This folder has no readable PNG or JPEG pictures. Choose a folder containing images.",
                    "Mega Game Creator 2000", 0x00000040);
            }
            else
            {
                DesktopStorage.ReapplyCustom(picker.Manager);
                try
                {
                    Directory.CreateDirectory(DesktopStorage.SaveDirectory);
                    File.WriteAllText(FolderFile, folder);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    Trace.TraceWarning($"Could not remember picture folder: {error.Message}");
                }
            }
        }
        catch (Exception error)
        {
            Trace.TraceWarning($"Picture folder selection failed: {error}");
            MessageBoxW(DesktopDisplay.GameWindowHandle,
                "The selected picture folder could not be loaded. Check the folder and try again.",
                "Mega Game Creator 2000", 0x00000040);
        }
        finally
        {
            refreshingMapper = true;
            try { picker.Mapper.GetType().GetMethod("Init", InstanceFields)!.Invoke(picker.Mapper, null); }
            finally { refreshingMapper = false; }
        }
    }

    private static bool Import(object manager, string folder)
    {
        if (!Directory.Exists(folder)) return false;
        var managerType = manager.GetType();
        var sourcesField = managerType.GetField("srcImages", InstanceFields)!;
        var device = (GraphicsDevice)managerType.GetField("gd", InstanceFields)!.GetValue(manager)!;
        var sources = (IList)Activator.CreateInstance(sourcesField.FieldType)!;
        var sourceType = sourcesField.FieldType.GenericTypeArguments[0];
        var constructor = sourceType.GetConstructor([typeof(string), typeof(Texture2D)])!;
        var imageIndex = sourceType.GetField("imgIdx", InstanceFields)!;
        var sourceId = sourceType.GetField("ID", InstanceFields)!;
        var sourceTexture = sourceType.GetField("texture", InstanceFields)!;
        var loaded = new List<Texture2D>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", new EnumerationOptions
                     { RecurseSubdirectories = true, IgnoreInaccessible = true })
                     .Where(file => Extensions.Contains(Path.GetExtension(file)))
                     .OrderBy(file => file, StringComparer.OrdinalIgnoreCase))
            {
                if (sources.Count == MaximumPictures) break;
                try
                {
                    if (new FileInfo(file).Length > 32 * 1024 * 1024) continue;
                    using var stream = File.OpenRead(file);
                    var thumbnail = Texture2D.FromStream(device, stream,
                        ThumbnailSize, ThumbnailSize, zoom: false);
                    loaded.Add(thumbnail);
                    var id = "file:" + Convert.ToHexString(SHA256.HashData(
                        Encoding.UTF8.GetBytes(Path.GetFullPath(file).ToUpperInvariant()))).ToLowerInvariant();
                    sources.Add(constructor.Invoke([id, thumbnail]));
                }
                catch (Exception error)
                {
                    Trace.TraceWarning($"Skipping picture {file}: {error.Message}");
                }
            }
            if (sources.Count == 0) return false;

            var previous = (IList?)sourcesField.GetValue(manager);
            sourcesField.SetValue(manager, sources);
            var custom = (Array)managerType.GetField("customImg", InstanceFields)!.GetValue(manager)!;
            foreach (var image in custom)
            {
                var type = image!.GetType();
                if (!(bool)type.GetField("enabled", InstanceFields)!.GetValue(image)!) continue;
                var id = (string?)type.GetField("ID", InstanceFields)!.GetValue(image);
                var match = -1;
                for (var i = 0; i < sources.Count; i++)
                    if (string.Equals(id, (string?)sourceId.GetValue(sources[i]), StringComparison.Ordinal))
                    { match = i; break; }
                if (match < 0)
                {
                    type.GetField("enabled", InstanceFields)!.SetValue(image, false);
                    continue;
                }
                var slot = Array.IndexOf(custom, image);
                imageIndex.SetValue(sources[match], slot);
                type.GetField("srcIdx", InstanceFields)!.SetValue(image, match);
                type.GetMethod("Process", InstanceFields)!.Invoke(image, [sources[match], device]);
            }
            managerType.GetMethod("SetGameCustomMap", InstanceFields)!.Invoke(manager, null);
            if (previous is not null)
                foreach (var old in previous)
                    if (sourceTexture.GetValue(old) is Texture2D texture && OwnedTextures.Remove(texture))
                        texture.Dispose();
            foreach (var texture in loaded) OwnedTextures.Add(texture);
            Trace.TraceInformation($"Imported {sources.Count} creator pictures from {folder}.");
            return true;
        }
        finally
        {
            if (sources.Count == 0)
                foreach (var texture in loaded) texture.Dispose();
        }
    }

    [SupportedOSPlatform("windows")]
    private static Task<string?> StartBrowse(IntPtr owner)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var initialized = false;
            try
            {
                Marshal.ThrowExceptionForHR(CoInitializeEx(IntPtr.Zero, 0x2));
                initialized = true;
                completion.SetResult(BrowseFolder(owner));
            }
            catch (Exception error) { completion.SetException(error); }
            finally { if (initialized) CoUninitialize(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    [SupportedOSPlatform("windows")]
    private static string? BrowseFolder(IntPtr owner)
    {
        var display = Marshal.AllocHGlobal(520);
        var path = Marshal.AllocHGlobal(65536);
        try
        {
            var info = new BrowseInfo
            {
                Owner = owner, DisplayName = display,
                Title = "Choose a picture folder for Mega Game Creator 2000",
                Flags = 0x0001 | 0x0010 | 0x0040,
                Callback = Marshal.GetFunctionPointerForDelegate(BringPickerForward)
            };
            var item = SHBrowseForFolderW(ref info);
            if (item == IntPtr.Zero) return null;
            try { return SHGetPathFromIDListW(item, path) ? Marshal.PtrToStringUni(path) : null; }
            finally { Marshal.FreeCoTaskMem(item); }
        }
        finally
        {
            Marshal.FreeHGlobal(display);
            Marshal.FreeHGlobal(path);
        }
    }

    private static int OnBrowseEvent(IntPtr window, uint message, IntPtr parameter, IntPtr data)
    {
        if (message == 1) // BFFM_INITIALIZED
        {
            SetWindowPos(window, new IntPtr(-1), 0, 0, 0, 0, 0x0043);
            SetForegroundWindow(window);
        }
        return 0;
    }

    private delegate int BrowseCallback(IntPtr window, uint message, IntPtr parameter, IntPtr data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BrowseInfo
    {
        public IntPtr Owner;
        public IntPtr Root;
        public IntPtr DisplayName;
        [MarshalAs(UnmanagedType.LPWStr)] public string Title;
        public uint Flags;
        public IntPtr Callback;
        public IntPtr Parameter;
        public int Image;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr SHBrowseForFolderW(ref BrowseInfo info);
    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoInitializeEx(IntPtr reserved, uint coInit);
    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoUninitialize();
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHGetPathFromIDListW(IntPtr item, IntPtr path);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(IntPtr owner, string text, string title, uint flags);
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    private sealed record PendingPicker(object Manager, object Mapper, Task<string?> Result);
}
