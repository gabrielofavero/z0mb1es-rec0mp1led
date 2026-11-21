using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Foundation;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input.Touch;
using Port.Bridge;
using Port.Hooks;
using Port.Prepare;
using SDL3;
using UIKit;

namespace Port.IosGame;

public static class Program
{
    private static readonly SDL.SDL_main_func Entry = NativeMain;

    public static void Main(string[] args)
    {
        Console.SetOut(new GameTrace.Writer());
        Console.SetError(new GameTrace.Writer());
        GameTrace.Write($"launch build=20 variant=files-importer ios={UIDevice.CurrentDevice.SystemVersion} " +
            $"runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription} " +
            $"dynamicCodeCompiled={RuntimeFeature.IsDynamicCodeCompiled}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => GameTrace.Write("unhandled=" + e.ExceptionObject);
        // The reviewed FNA is the netstandard build, whose NET7-only static import
        // resolver is absent. All five native dependencies are linked into this image.
        NativeLibrary.SetDllImportResolver(typeof(Game).Assembly,
            (_, _, _) => NativeLibrary.GetMainProgramHandle());
        Environment.SetEnvironmentVariable("FNA_GRAPHICS_ENABLE_HIGHDPI", "1");
        SDL.SDL_SetHint(SDL.SDL_HINT_MOUSE_TOUCH_EVENTS, "0");
        SDL.SDL_SetHint(SDL.SDL_HINT_TOUCH_MOUSE_EVENTS, "0");
        SDL.SDL_SetHint("SDL_ACCELEROMETER_AS_JOYSTICK", "0");
        SDL.SDL_SetHint("SDL_ORIENTATIONS", "Portrait LandscapeLeft LandscapeRight");
        SDL.SDL_RunApp(0, IntPtr.Zero, Entry, IntPtr.Zero);
    }

    [ObjCRuntime.MonoPInvokeCallback(typeof(SDL.SDL_main_func))]
    private static int NativeMain(int argc, IntPtr argv)
    {
        if (SynchronizationContext.Current is null)
            SynchronizationContext.SetSynchronizationContext(new MainThreadContext());
        GameTrace.Write("sdl-main=entered");
        try { HostCoordinator.Run(); return 0; }
        catch (Exception error) { GameTrace.Write("startup-failure=" + error); return 1; }
    }

    internal static (Game Game, MobileSession Monitor) CreateOriginal(ActiveAssetSet active)
    {
        PreparedGameVerifier.Verify(active.DllPath, active.ContentRoot);
        var saveRoot = GameData.Saves;
        DesktopStorage.Configure(saveRoot);
        DesktopPictures.Configure(System.IO.Path.Combine(GameData.Root, "Pictures"), managePhotos: true);

        PlayerIdentity.Configure(saveRoot);
        GameContent.Configure(active.ContentRoot);
        AssetChoices.Initialize(active.ArtRoot, null, null, null);
        _ = typeof(BridgeRuntime).Assembly;
        GameTrace.Write("prepared-game=loading");
        var assembly = Assembly.LoadFrom(active.DllPath);
        GameTrace.Write("prepared-game=loaded");
        var type = assembly.GetType("ZombiesWP7.Game1", throwOnError: true)!;
        var game = (Game)Activator.CreateInstance(type)!;
        GameTrace.Write("game1=created");
        game.Content.Dispose();
        game.Content = new CaseMappedContentManager(game.Services,
            System.IO.Path.Combine(active.ContentRoot, "Content"), GameTrace.Write);
        DesktopDisplay.Configure(game, mobile: true);
        MobileDisplay.Configure(game);
        DeviceVibration.Configure();
        DesktopInput.Configure(game);
        DesktopInput.NativeBackButtonEnabled = true;
        DesktopAchievements.Configure();
        var monitor = new MobileSession(game, type, !File.Exists(System.IO.Path.Combine(saveRoot, "savegame")));
        game.Components.Add(monitor);
        DesktopDisplay.ReimportRequested = () => monitor.Enqueue(() =>
        {
            GameUi.Reimport(game, monitor);
        });
        game.Activated += (_, _) => GameTrace.Write("application=active");
        game.Deactivated += (_, _) =>
        {
            GameTrace.Write("application=inactive");
            monitor.PauseForMenu();
        };
        DesktopPictures.PhotoSelectionRequested = () =>
        {
            GameUi.PickPhotos(game, monitor);
            return Array.Empty<string>();
        };
        GameTrace.Write("original-game=configured");
        return (game, monitor);
    }
}

internal sealed class MainThreadContext : SynchronizationContext
{
    public override SynchronizationContext CreateCopy() => this;
    public override void Post(SendOrPostCallback callback, object? state) =>
        CoreFoundation.DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            var previous = Current;
            SetSynchronizationContext(this);
            try { callback(state); }
            finally { SetSynchronizationContext(previous); }
        });
}
internal sealed class MobileSession(Game game, Type type, bool firstSave) : DrawableGameComponent(game)
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<Action> pending = new();
    internal void Enqueue(Action action) => pending.Enqueue(action);
    internal void PauseForMenu()
    {
        if (!readyLogged) return;
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var state = (int)type.Assembly.GetType("IMAK3Z0MB1EGAEM.director.GameState", true)!.GetField("state", flags)!.GetValue(null)!;
        if (state is not (3 or 5 or 7)) return;
        var manager = type.Assembly.GetType("Viking_x86.director.TimeMgr", true)!.GetMethod("CurTMgr", flags)!.Invoke(null, null)!;
        if (Convert.ToInt32(manager.GetType().GetField("playMode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(manager)) != 1) return;
        manager.GetType().GetMethod("Pause", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(manager, [0]);
        type.Assembly.GetType("IMAK3Z0MB1EGAEM.menu.Menu", true)!.GetMethod("SetLevel", flags)!.Invoke(null, [2]);
        GameTrace.Write("game=paused-for-system-ui");
    }
    internal void ReloadPictures() => Enqueue(() =>
    {
        var custom = type.GetField("custom", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(null);
        if (custom is not null && DesktopPictures.ImportFolder(custom, System.IO.Path.Combine(GameData.Root, "Pictures")))
            DesktopStorage.ReapplyCustom(custom);
    });
    internal void SaveForBackground()
    {
        if (!readyLogged) return;
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        object Read(string name) => type.GetField(name, flags)!.GetValue(null)!;
        DesktopStorage.Write(Read("profile"), Read("player"), Read("custom"));
        GameTrace.Write("background-save=written");
    }
    private readonly System.Diagnostics.Stopwatch elapsed = System.Diagnostics.Stopwatch.StartNew();
    private int updates;
    private bool readyLogged;
    private bool padsSet;
    public int Frames { get; private set; }

    public override void Initialize()
    {
        base.Initialize();
        DesktopDisplay.ApplyStartup(windowed: false);
        GameTrace.Write($"fna-game-graphics={GraphicsDevice.PresentationParameters.BackBufferWidth}x" +
            $"{GraphicsDevice.PresentationParameters.BackBufferHeight}");
    }

    public override void Update(GameTime time)
    {
        updates++;
        while (pending.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception error) { GameTrace.Write("player-action-failure=" + error); }
        }
        TouchPanel.DisplayWidth = (int)DesktopDisplay.SceneSize.X;
        TouchPanel.DisplayHeight = (int)DesktopDisplay.SceneSize.Y;
        if (!Game.IsActive) Game.SuppressDraw();
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var ready = (bool)(type.GetField("loadcomplete", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .GetValue(Game) ?? false);
        var stateType = type.Assembly.GetType("IMAK3Z0MB1EGAEM.director.GameState", true)!;
        var state = (int)stateType.GetField("state", flags)!.GetValue(null)!;
        if (ready && !readyLogged)
        {
            readyLogged = true;
            GameTrace.Write("original-menu=loaded state=" + state);
        }
        if (ready && firstSave && !padsSet)
        {
            var player = type.GetField("player", flags)!.GetValue(null)!;
            var settings = player.GetType().GetField("settings")!.GetValue(player)!;
            settings.GetType().GetField("showTouchPads")!.SetValue(settings, true);
            padsSet = true;
        }
        if (ready) DesktopDisplay.AttachSettingsMenu();
        DesktopDisplay.Update();
        if (updates == 120 || updates % 1800 == 0)
            GameTrace.Write($"game-progress updates={updates} frames={Frames} state={state} elapsed={elapsed.Elapsed.TotalSeconds:F2}");
        base.Update(time);
    }

    public override void Draw(GameTime time)
    {
        Frames++;
        if (Frames == 1) GameTrace.Write("original-game=first-frame");
        base.Draw(time);
    }
}
