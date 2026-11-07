using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
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
    private static string FolderFile => Path.Combine(DesktopStorage.SaveDirectory, "pictures-folder.txt");

    public static bool ImportFolder(object manager, string folder) => Import(manager, folder);

    public static void ChooseFolder(object manager, object mapper)
    {
        if (refreshingMapper || pending is not null ||
            !OperatingSystem.IsWindows() || !Environment.UserInteractive) return;
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
            try { completion.SetResult(BrowseFolder(owner)); }
            catch (Exception error) { completion.SetException(error); }
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
                Flags = 0x0001 | 0x0010 | 0x0040
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
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHGetPathFromIDListW(IntPtr item, IntPtr path);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(IntPtr owner, string text, string title, uint flags);

    private sealed record PendingPicker(object Manager, object Mapper, Task<string?> Result);
}
