using System.Reflection;
using Android.App;
using Android.Content.PM;
using Android.Content;
using System.Collections.Concurrent;
using Port.IosGame;
using Android.OS;
using Android.Views;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input.Touch;
using Org.Libsdl.App;
using Port.Bridge;
using Port.Hooks;
using Port.Prepare;
using SDL = SDL3.SDL;

namespace Port.AndroidGame;

[Activity(Label = "Z0MB1ES", MainLauncher = true, Exported = true,
    LaunchMode = LaunchMode.SingleTask, ScreenOrientation = ScreenOrientation.SensorLandscape,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden |
        ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.UiMode)]
public sealed class MainActivity : SDLActivity
{
    private static readonly SDL.SDL_main_func Entry = NativeMain;
    private static MainActivity? current;
    private readonly ConcurrentQueue<Action> pending = new();
    private FolderSetup? setup;
    private FolderInstallerGame? installer;
    private string? pickerSlot;
    private readonly CancellationTokenSource lifetime = new();
    private const int PackageRequest = 42;
    private string dataRoot = "";
    private AndroidControllerInput? controllers;
    private static readonly object logLock = new();

    protected override string[] GetLibraries() => ["SDL3"];

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        current = this;
        dataRoot = FilesDir!.AbsolutePath;
        base.OnCreate(savedInstanceState);
        controllers = new AndroidControllerInput((Android.Hardware.Input.InputManager)GetSystemService(InputService)!);
        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
        Log("launch variant=device-import framework=FNA.NET renderer=OpenGL arm64");
    }

    protected override void Main()
    {
        SDL.SDL_SetHint("FNA_GRAPHICS_ENABLE_HIGHDPI", "1");
        SDL.SDL_SetHint(SDL.SDL_HINT_MOUSE_TOUCH_EVENTS, "0");
        SDL.SDL_SetHint(SDL.SDL_HINT_TOUCH_MOUSE_EVENTS, "0");
        SDL.SDL_SetHint("SDL_ACCELEROMETER_AS_JOYSTICK", "0");
        SDL.SDL_SetHint("FNA3D_FORCE_DRIVER", "OpenGL");
        SDL.SDL_SetHint("FNA3D_OPENGL_FORCE_ES3", "1");
        SDL.SDL_RunApp(0, IntPtr.Zero, Entry, IntPtr.Zero);
    }

    public override bool DispatchKeyEvent(KeyEvent? e) =>
        e is not null && controllers?.Process(e) == true || base.DispatchKeyEvent(e);

    public override bool DispatchGenericMotionEvent(MotionEvent? e) =>
        e is not null && controllers?.Process(e) == true || base.DispatchGenericMotionEvent(e);

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        if (!hasFocus) AndroidGamePad.Input.Reset();
        base.OnWindowFocusChanged(hasFocus);
    }

    protected override void OnPause()
    {
        AndroidGamePad.Input.Reset();
        base.OnPause();
    }

    protected override void OnDestroy()
    {
        controllers?.Stop();
        lifetime.Cancel();
        setup?.Cancel();
        base.OnDestroy();
    }

    private static int NativeMain(int argc, IntPtr argv)
    {
        try { current!.RunPlayer(); return 0; }
        catch (Exception error)
        {
            Log("game-failure=" + error);
            current?.RunOnUiThread(() => new AlertDialog.Builder(current)!
                .SetTitle("Game could not start")!.SetMessage(error.ToString())!
                .SetPositiveButton("Close", (_, _) => current.Finish())!.Show());
            return 1;
        }
    }

    private void RunPlayer()
    {
        var picks = Path.Combine(dataRoot, "package-picks");
        if (Directory.Exists(picks)) Directory.Delete(picks, true);
        var assetRoot = Path.Combine(dataRoot, "Assets");
        if (Directory.Exists(assetRoot))
            foreach (var path in Directory.GetDirectories(assetRoot))
            {
                var name = Path.GetFileName(path);
                if ((name.StartsWith(".stage-", StringComparison.Ordinal) || name.StartsWith(".work-", StringComparison.Ordinal)) &&
                    Guid.TryParseExact(name[(name.IndexOf('-') + 1)..], "N", out _)) Directory.Delete(path, true);
            }
        var store = ImportAssets.Store(Assets!, dataRoot);
        var active = store.TryOpen();
        var flag = Path.Combine(dataRoot, "open-importer");
        if (active is null || !FirstLaunchWarning.IsAcknowledged(Path.Combine(dataRoot, "Saves")) || File.Exists(flag))
            active = RunSetup(store, active, true);
        if (active is null) return;
        if (RunOriginal(active)) RunSetup(store, store.TryOpen(), false);
    }

    private ActiveAssetSet? RunSetup(PersistentAssetStore store, ActiveAssetSet? active, bool allowLaunch)
    {
        using var state = new FolderSetup(Path.Combine(dataRoot, "package-picks"), Path.Combine(dataRoot, "Saves"),
            store, active, allowLaunch, Log, () => File.Delete(Path.Combine(dataRoot, "open-importer")), usesPicker: true);
        using var screen = new FolderInstallerGame(state, true, _ => Vector4.Zero, Log);
        setup = state;
        installer = screen;
        screen.PickFile = PickPackage;
        screen.Components.Add(new PendingActions(screen, pending));
        try { screen.Run(); return state.LaunchRequested ? state.Active : null; }
        finally { installer = null; setup = null; }
    }

    private void PickPackage(string slot)
    {
        if (installer is null || installer.PickerOpen) return;
        installer.PickerOpen = true;
        pickerSlot = slot;
        Log("package-picker=opening slot=" + slot);
        RunOnUiThread(() =>
        {
            try
            {
                var intent = new Intent(Intent.ActionOpenDocument);
                intent.AddCategory(Intent.CategoryOpenable);
                intent.SetType("*/*");
                intent.AddFlags(ActivityFlags.GrantReadUriPermission);
                StartActivityForResult(intent, PackageRequest);
            }
            catch (Exception error)
            {
                Log("package-picker-failure=" + error);
                pending.Enqueue(() => { if (installer is not null) installer.PickerOpen = false; setup?.SetStatus("Could not open the file picker."); });
            }
        });
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != PackageRequest) return;
        var slot = pickerSlot;
        pickerSlot = null;
        var uri = data?.Data;
        Log("package-picker=result slot=" + slot + " accepted=" + (resultCode == Result.Ok && uri is not null));
        if (resultCode != Result.Ok || uri is null || slot is null)
        {
            pending.Enqueue(() => { if (installer is not null) installer.PickerOpen = false; });
            return;
        }
        pending.Enqueue(() => setup?.SetStatus("Copying selected file..."));
        // Providers may be remote: copy off the UI/game threads. Never retain or delete the original document.
        _ = Task.Run(() =>
        {
            string? copy = null;
            try
            {
                var name = slot == "phone" ? "phone.xap" : "artwork";
                using (var cursor = ContentResolver!.Query(uri, new[] { Android.Provider.IOpenableColumns.DisplayName }, null, null, null))
                    if (cursor?.MoveToFirst() == true && Path.GetExtension(cursor.GetString(0) ?? "").Equals(".zip", StringComparison.OrdinalIgnoreCase)) name = "artwork.zip";
                var folder = Path.Combine(dataRoot, "package-picks", "copy-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(folder);
                copy = Path.Combine(folder, name);
                using (var input = ContentResolver!.OpenInputStream(uri) ?? throw new IOException("Could not open document."))
                using (var output = File.Create(copy))
                {
                    var buffer = new byte[128 * 1024];
                    long total = 0;
                    int count;
                    while ((count = input.Read(buffer)) > 0)
                    {
                        lifetime.Token.ThrowIfCancellationRequested();
                        if ((total += count) > 2L * 1024 * 1024 * 1024) throw new IOException("Package exceeds the size limit.");
                        output.Write(buffer, 0, count);
                    }
                }
                Log("package-copy=complete slot=" + slot + " bytes=" + new FileInfo(copy).Length);
                var selected = copy;
                pending.Enqueue(() =>
                {
                    try { setup?.TakePickedFile(slot, selected); }
                    finally { if (File.Exists(selected)) File.Delete(selected); if (installer is not null) installer.PickerOpen = false; }
                });
                copy = null;
            }
            catch (Exception error)
            {
                Log("package-copy-failure=" + error);
                pending.Enqueue(() => { setup?.SetStatus("Could not copy that file. Try again."); if (installer is not null) installer.PickerOpen = false; });
            }
            finally { if (copy is not null && File.Exists(copy)) File.Delete(copy); }
        });
    }

    private bool RunOriginal(ActiveAssetSet active)
    {
        var root = active.Root;
        var saves = Path.Combine(dataRoot, "Saves");
        DesktopStorage.Configure(saves);
        DesktopPictures.Configure(Path.Combine(dataRoot, "Pictures"));
        PlayerIdentity.Configure(saves);
        GameContent.Configure(Path.Combine(root, "content"));
        AssetChoices.Initialize(Directory.Exists(Path.Combine(root, "optional-art")) ? Path.Combine(root, "optional-art") : null,
            null, null, null);
        _ = typeof(BridgeRuntime).Assembly;
        var assembly = Assembly.LoadFrom(active.DllPath);
        var type = assembly.GetType("ZombiesWP7.Game1", true)!;
        using var game = (Game)Activator.CreateInstance(type)!;
        game.Content.Dispose();
        game.Content = new Port.IosGame.CaseMappedContentManager(game.Services, Path.Combine(root, "content/Content"), Log);
        DesktopDisplay.Configure(game, mobile: true);
        void Orientation(bool responsive) => RunOnUiThread(() => RequestedOrientation = responsive
            ? ScreenOrientation.FullSensor : ScreenOrientation.SensorLandscape);
        DesktopDisplay.ResponsiveOrientationChanged += Orientation;
        Orientation(DesktopDisplay.ResponsiveOrientationEnabled);
        DesktopInput.Configure(game);
        DesktopInput.NativeBackButtonEnabled = true;
        DesktopAchievements.Configure();
        var session = new AndroidSession(game, type, !File.Exists(Path.Combine(saves, "savegame")));
        game.Components.Add(session);
        var reimport = false;
        DesktopDisplay.ReimportRequested = () =>
        {
            session.Pause();
            session.Save();
            File.WriteAllText(Path.Combine(dataRoot, "open-importer"), "1");
            reimport = true;
            game.Exit();
        };
        game.Deactivated += (_, _) => { session.Pause(); session.Save(); };
        try { Log("original-game=starting"); game.Run(); }
        finally { DesktopDisplay.ResponsiveOrientationChanged -= Orientation; DesktopDisplay.ReimportRequested = null; }
        Log("original-game=ended");
        return reimport;
    }

    internal static void Log(string message)
    {
        Android.Util.Log.Info("Z0MB1ES", message);
        lock (logLock)
        {
            if (current?.dataRoot is { Length: > 0 } root)
                File.AppendAllText(Path.Combine(root, "android-session.log"), DateTimeOffset.UtcNow.ToString("O") + " " + message + "\n");
        }
    }
}

internal sealed class AndroidSession(Game game, Type type, bool firstSave) : DrawableGameComponent(game)
{
    private const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private bool ready;
    private int updates;
    private int frames;
    private bool controllerConnected;
    private bool controllerActive;

    public override void Initialize()
    {
        base.Initialize();
        DesktopDisplay.ApplyStartup(windowed: false);
        MainActivity.Log($"graphics={GraphicsDevice.PresentationParameters.BackBufferWidth}x{GraphicsDevice.PresentationParameters.BackBufferHeight}");
    }

    public override void Update(GameTime gameTime)
    {
        var pad = AndroidGamePad.GetState(PlayerIndex.One);
        var active = pad.IsConnected && (pad.ThumbSticks.Left.LengthSquared() > 0.04f ||
            pad.ThumbSticks.Right.LengthSquared() > 0.04f || pad.Buttons != default || pad.DPad != default ||
            pad.Triggers.Left > 0.1f || pad.Triggers.Right > 0.1f);
        if (pad.IsConnected != controllerConnected || active != controllerActive)
        {
            controllerConnected = pad.IsConnected;
            controllerActive = active;
            MainActivity.Log($"controller-player-one connected={pad.IsConnected} active={active} left={pad.ThumbSticks.Left} right={pad.ThumbSticks.Right} triggers={pad.Triggers.Left}/{pad.Triggers.Right} a={pad.Buttons.A} start={pad.Buttons.Start} dpad={pad.DPad.Up}/{pad.DPad.Down}/{pad.DPad.Left}/{pad.DPad.Right}");
        }
        TouchPanel.DisplayWidth = (int)DesktopDisplay.SceneSize.X;
        TouchPanel.DisplayHeight = (int)DesktopDisplay.SceneSize.Y;
        if (!ready && (bool)type.GetField("loadcomplete", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(Game)!)
        {
            ready = true;
            if (firstSave)
            {
                var player = type.GetField("player", Flags)!.GetValue(null)!;
                var settings = player.GetType().GetField("settings")!.GetValue(player)!;
                settings.GetType().GetField("showTouchPads")!.SetValue(settings, true);
            }
            MainActivity.Log("original-menu=loaded");
        }
        if (ready) DesktopDisplay.AttachSettingsMenu();
        DesktopDisplay.Update();
        if (++updates % 1800 == 0) MainActivity.Log($"progress updates={updates} frames={frames}");
        base.Update(gameTime);
    }

    public override void Draw(GameTime gameTime)
    {
        if (++frames == 1) MainActivity.Log("original-game=first-frame");
        base.Draw(gameTime);
    }

    internal void Save()
    {
        if (!ready) return;
        object Read(string name) => type.GetField(name, Flags)!.GetValue(null)!;
        try { DesktopStorage.Write(Read("profile"), Read("player"), Read("custom")); }
        catch (Exception error) { MainActivity.Log("save-failure=" + error); }
    }

    internal void Pause()
    {
        if (!ready) return;
        try
        {
            var state = (int)type.Assembly.GetType("IMAK3Z0MB1EGAEM.director.GameState", true)!.GetField("state", Flags)!.GetValue(null)!;
            if (state is not (3 or 5 or 7)) return;
            var manager = type.Assembly.GetType("Viking_x86.director.TimeMgr", true)!.GetMethod("CurTMgr", Flags)!.Invoke(null, null)!;
            const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            if (Convert.ToInt32(manager.GetType().GetField("playMode", instance)!.GetValue(manager)) != 1) return;
            manager.GetType().GetMethod("Pause", instance)!.Invoke(manager, [0]);
            type.Assembly.GetType("IMAK3Z0MB1EGAEM.menu.Menu", true)!.GetMethod("SetLevel", Flags)!.Invoke(null, [2]);
            MainActivity.Log("game=paused-on-background");
        }
        catch (Exception error) { MainActivity.Log("pause-failure=" + error); }
    }
}
