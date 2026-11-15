using CoreAnimation;
using Foundation;
using Microsoft.Xna.Framework;
using Port.Prepare;
using UIKit;

namespace Port.IosGame;

internal static class HostCoordinator
{
    private static UIWindow? window;
    private static IosDisplayLoop? loop;
    internal static UIWindow Window => window ?? throw new InvalidOperationException("Native host window is unavailable.");

    internal static void Begin()
    {
        EnsureWindow();
        ShowLoading();
        // Return from SDL's startup callback before doing preparation or creating
        // a renderer. UIKit can finish scene activation and commit its layer tree.
        CoreFoundation.DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            try
            {
                var active = GameData.Store.TryOpen();
                if (active is null || GameData.ImportRequested) GameUi.ShowImporter(null, allowLaunch: true);
                else StartOriginal(active);
            }
            catch (Exception error) { ShowFailure(error); }
        });
    }

    private static void EnsureWindow()
    {
        if (window is not null) return;
        var scene = UIApplication.SharedApplication.ConnectedScenes.OfType<UIWindowScene>()
            .OrderBy(s => s.ActivationState == UISceneActivationState.ForegroundActive ? 0 : 1).FirstOrDefault()
            ?? throw new InvalidOperationException("UIKit did not provide a window scene.");
        var bounds = OperatingSystem.IsIOSVersionAtLeast(26)
            ? scene.EffectiveGeometry.CoordinateSpace.Bounds : scene.CoordinateSpace.Bounds;
        window = new UIWindow(scene) { Frame = bounds, WindowLevel = UIWindowLevel.Normal + 2 };
        UiDiagnostics.Start();
        GameTrace.Write("host-window=created");
    }

    internal static void Present(UIViewController controller)
    {
        EnsureWindow();
        Window.RootViewController = controller;
        Window.MakeKeyAndVisible();
        Window.LayoutIfNeeded();
        UiDiagnostics.Capture("host-present", Window);
    }

    internal static void ShowLoading()
    {
        var controller = new UIViewController();
        controller.View!.BackgroundColor = UIColor.SystemBackground;
        var text = new UILabel { Text = "Starting Z0MB1ES…", TranslatesAutoresizingMaskIntoConstraints = false };
        controller.View.AddSubview(text);
        NSLayoutConstraint.ActivateConstraints([
            text.CenterXAnchor.ConstraintEqualTo(controller.View.CenterXAnchor),
            text.CenterYAnchor.ConstraintEqualTo(controller.View.CenterYAnchor)
        ]);
        Present(controller);
    }

    internal static void ImportFinished()
    {
        ShowLoading();
        CoreFoundation.DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            try { StartOriginal(GameData.Store.TryOpen() ?? throw new InvalidDataException("The imported asset set could not be verified.")); }
            catch (Exception error) { ShowFailure(error); }
        });
    }

    private static void StartOriginal(ActiveAssetSet active)
    {
        GameTrace.Write("original-game=creating-session");
        var (game, monitor) = Program.CreateOriginal(active);
        loop = new IosDisplayLoop(game, monitor, ShowFailure);
        loop.Start();
        Window.Hidden = true;
        UiDiagnostics.Capture("game-started", GameUi.NativeWindow(game));
    }

    internal static void ShowFailure(Exception error)
    {
        GameTrace.Write("host-failure=" + error);
        try
        {
            var failed = loop;
            loop = null;
            failed?.Dispose();
        }
        catch (Exception shutdown) { GameTrace.Write("shutdown-failure=" + shutdown); }
        try { GameUi.ShowImporter(error.GetBaseException().Message, allowLaunch: false); }
        catch (Exception fallback) { GameTrace.Write("repair-ui-failure=" + fallback); }
    }
}

internal sealed class IosDisplayLoop(Game game, MobileSession monitor, Action<Exception> failed) : IDisposable
{
    private readonly FnaFrameDriver driver = new(game);
    private readonly List<NSObject> observers = [];
    private CADisplayLink? display;
    private bool disposed;
    private int frames;

    internal void Start()
    {
        GameTrace.Write("game-loop=initializing");
        driver.Start();
        display = CADisplayLink.Create(Frame);
        display.PreferredFrameRateRange = CAFrameRateRange.Create(60, 60, 60);
        display.AddToRunLoop(NSRunLoop.Main, NSRunLoopMode.Common);
        observers.Add(NSNotificationCenter.DefaultCenter.AddObserver(UIApplication.WillResignActiveNotification, _ =>
        {
            if (display is not null) display.Paused = true;
            try { monitor.PauseForMenu(); monitor.SaveForBackground(); }
            catch (Exception error) { GameTrace.Write("background-save-failure=" + error); }
            UiDiagnostics.Capture("application-resigning", GameUi.NativeWindow(game));
        }));
        observers.Add(NSNotificationCenter.DefaultCenter.AddObserver(UIApplication.DidBecomeActiveNotification, _ =>
        {
            game.ResetElapsedTime();
            if (display is not null) display.Paused = false;
            GameTrace.Write("game-loop=foreground");
        }));
        GameTrace.Write("game-loop=display-link-started");
    }

    private void Frame()
    {
        if (disposed) return;
        try
        {
            if (!driver.Step()) { Dispose(); HostCoordinator.ShowLoading(); return; }
            frames++;
            if (frames == 1 || frames == 120)
                UiDiagnostics.Capture("game-display-frame-" + frames, GameUi.NativeWindow(game));
        }
        catch (Exception error) { failed(error); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        display?.Invalidate();
        display?.Dispose();
        display = null;
        foreach (var observer in observers) { NSNotificationCenter.DefaultCenter.RemoveObserver(observer); observer.Dispose(); }
        observers.Clear();
        driver.Dispose();
        GameTrace.Write("game-loop=disposed frames=" + frames);
    }
}
