using System.Reflection;
using Android.App;
using Android.Content.PM;
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

[Activity(Label = "Z0MB1ES Experimental", MainLauncher = true, Exported = true,
    LaunchMode = LaunchMode.SingleTask, ScreenOrientation = ScreenOrientation.SensorLandscape,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden |
        ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.UiMode)]
public sealed class MainActivity : SDLActivity
{
    private static readonly SDL.SDL_main_func Entry = NativeMain;
    private static MainActivity? current;
    private readonly TaskCompletionSource<bool> acknowledged = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string dataRoot = "";
    private static readonly object logLock = new();

    protected override string[] GetLibraries() => ["SDL3"];

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        current = this;
        dataRoot = FilesDir!.AbsolutePath;
        base.OnCreate(savedInstanceState);
        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
        var saves = Path.Combine(dataRoot, "Saves");
        if (FirstLaunchWarning.IsAcknowledged(saves)) acknowledged.TrySetResult(true);
        else
        {
            var choice = new Android.Widget.CheckBox(this)
            {
                Text = FirstLaunchWarning.CheckboxLabel,
                Checked = FirstLaunchWarning.InitialReduceFlashing(saves)
            };
            new AlertDialog.Builder(this)!.SetTitle(FirstLaunchWarning.Title)!
                .SetMessage(FirstLaunchWarning.Text)!.SetView(choice)!.SetCancelable(false)!
                .SetPositiveButton("Continue", (_, _) =>
                {
                    FirstLaunchWarning.Acknowledge(saves, choice.Checked);
                    acknowledged.TrySetResult(true);
                })!
                .SetNegativeButton("Exit", (_, _) => { acknowledged.TrySetResult(false); Finish(); })!.Show();
        }
        Log("launch variant=personal-bundled framework=FNA.NET renderer=OpenGL arm64");
    }

    protected override void Main()
    {
        if (!acknowledged.Task.GetAwaiter().GetResult()) return;
        SDL.SDL_SetHint("FNA_GRAPHICS_ENABLE_HIGHDPI", "1");
        SDL.SDL_SetHint(SDL.SDL_HINT_MOUSE_TOUCH_EVENTS, "0");
        SDL.SDL_SetHint(SDL.SDL_HINT_TOUCH_MOUSE_EVENTS, "0");
        SDL.SDL_SetHint("SDL_ACCELEROMETER_AS_JOYSTICK", "0");
        SDL.SDL_SetHint("FNA3D_FORCE_DRIVER", "OpenGL");
        SDL.SDL_SetHint("FNA3D_OPENGL_FORCE_ES3", "1");
        SDL.SDL_RunApp(0, IntPtr.Zero, Entry, IntPtr.Zero);
    }

    protected override void OnDestroy()
    {
        acknowledged.TrySetResult(false);
        base.OnDestroy();
    }

    private static int NativeMain(int argc, IntPtr argv)
    {
        try { current!.RunOriginal(); return 0; }
        catch (Exception error)
        {
            Log("game-failure=" + error);
            current?.RunOnUiThread(() => new AlertDialog.Builder(current)!
                .SetTitle("Game could not start")!.SetMessage(error.ToString())!
                .SetPositiveButton("Close", (_, _) => current.Finish())!.Show());
            return 1;
        }
    }

    private void RunOriginal()
    {
        Log("bundled-assets=verifying");
        var root = BundledAssets.Open(Assets!, dataRoot);
        var saves = Path.Combine(dataRoot, "Saves");
        DesktopStorage.Configure(saves);
        DesktopPictures.Configure(Path.Combine(dataRoot, "Pictures"));
        PlayerIdentity.Configure(saves);
        GameContent.Configure(Path.Combine(root, "content"));
        AssetChoices.Initialize(Directory.Exists(Path.Combine(root, "optional-art")) ? Path.Combine(root, "optional-art") : null,
            null, null, null);
        _ = typeof(BridgeRuntime).Assembly;
        var assembly = Assembly.LoadFrom(Path.Combine(root, "game/ZombiesWP7.dll"));
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
        game.Deactivated += (_, _) => session.Pause();
        try { Log("original-game=starting"); game.Run(); }
        finally { DesktopDisplay.ResponsiveOrientationChanged -= Orientation; }
        Log("original-game=ended");
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

    public override void Initialize()
    {
        base.Initialize();
        DesktopDisplay.ApplyStartup(windowed: false);
        MainActivity.Log($"graphics={GraphicsDevice.PresentationParameters.BackBufferWidth}x{GraphicsDevice.PresentationParameters.BackBufferHeight}");
    }

    public override void Update(GameTime gameTime)
    {
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
