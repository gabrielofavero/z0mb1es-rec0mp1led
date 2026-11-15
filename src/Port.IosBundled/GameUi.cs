using Foundation;
using UIKit;
using Microsoft.Xna.Framework;
using SDL3;

namespace Port.IosGame;

internal static class GameUi
{
    private static UIWindow? failureWindow;
    private static PhotoPicker? photos;

    internal static UIViewController Root(Game game)
    {
        var pointer = SDL.SDL_GetPointerProperty(SDL.SDL_GetWindowProperties(game.Window.Handle),
            SDL.SDL_PROP_WINDOW_UIKIT_WINDOW_POINTER, IntPtr.Zero);
        var window = ObjCRuntime.Runtime.GetNSObject<UIWindow>(pointer)
            ?? throw new InvalidOperationException("SDL did not expose its UIKit window.");
        return window.RootViewController ?? throw new InvalidOperationException("SDL window has no view controller.");
    }

    internal static void PickPhotos(Game game, MobileSession session)
    {
        var root = Root(game);
        // UIAlertAction handlers run while the menu is dismissing.
        WhenReady(root, () =>
        {
            if (photos?.IsBusy == true) return;
            photos = new PhotoPicker(root, session.ReloadPictures);
            photos.Show();
        });
    }

    internal static void Message(UIViewController root, string title, string message)
    {
        var alert = UIAlertController.Create(title, message, UIAlertControllerStyle.Alert);
        alert.AddAction(UIAlertAction.Create("OK", UIAlertActionStyle.Default, null));
        WhenReady(root, () => root.PresentViewController(alert, true, null));
    }

    internal static void Share(UIViewController root)
    {
        var files = new List<NSObject> { NSUrl.FromFilename(GameTrace.Path) };
        if (File.Exists(GameTrace.Path + ".previous.txt")) files.Add(NSUrl.FromFilename(GameTrace.Path + ".previous.txt"));
        var share = new UIActivityViewController(files.ToArray(), null);
        if (share.PopoverPresentationController is { } popover)
        { popover.SourceView = root.View!; popover.SourceRect = root.View!.Bounds; }
        WhenReady(root, () => root.PresentViewController(share, true, null));
    }

    private static async void WhenReady(UIViewController root, Action action)
    {
        try
        {
            // An action sheet dismisses after its handler returns.
            for (var attempt = 0; attempt < 40 && root.PresentedViewController is not null; attempt++)
                await Task.Delay(50);
            if (root.PresentedViewController is null) action();
        }
        catch (Exception error) { GameTrace.Write("system-ui-failure=" + error); }
    }

    internal static void StartWatchdog() => _ = Task.Run(async () =>
    {
        for (var i = 0; i < 4; i++)
        {
            await Task.Delay(5000);
            GameTrace.Write("watchdog=alive seconds=" + ((i + 1) * 5) + " last=" + Volatile.Read(ref GameTrace.LastBoundary));
        }
    });

    internal static void LogWindows(string boundary)
    {
        try
        {
            GameTrace.Write($"ui-state boundary={boundary} mainThread={NSThread.IsMain} application={UIApplication.SharedApplication.ApplicationState}");
            foreach (var scene in UIApplication.SharedApplication.ConnectedScenes.OfType<UIWindowScene>())
            {
                GameTrace.Write($"ui-scene state={scene.ActivationState} windows={scene.Windows.Length}");
                foreach (var window in scene.Windows)
                {
                    var root = window.RootViewController;
                    GameTrace.Write($"ui-window hidden={window.Hidden} key={window.IsKeyWindow} alpha={window.Alpha} level={window.WindowLevel} bounds={window.Bounds} " +
                        $"root={root?.GetType().Name} frame={root?.View?.Frame} ambiguous={root?.View?.HasAmbiguousLayout}");
                }
            }
        }
        catch (Exception error) { GameTrace.Write("ui-state-failure=" + error); }
    }

    internal static void ShowFailure(Exception error)
    {
        GameTrace.Write("failure-ui=creating");
        var scene = UIApplication.SharedApplication.ConnectedScenes.OfType<UIWindowScene>().First();
        var controller = new UIViewController();
        controller.View!.BackgroundColor = UIColor.SystemBackground;
        var text = new UITextView { Editable = false, Text = "Game startup failed\n\n" + error.GetBaseException().Message +
            "\n\nThe game trace is also available in Files → On My iPhone → Z0MB1ES.", TranslatesAutoresizingMaskIntoConstraints = false };
        var share = new UIButton(UIButtonType.System) { TranslatesAutoresizingMaskIntoConstraints = false };
        share.SetTitle("Share game log", UIControlState.Normal);
        share.TouchUpInside += (_, _) => Share(controller);
        controller.View.AddSubview(text);
        controller.View.AddSubview(share);
        var safe = controller.View.SafeAreaLayoutGuide;
        NSLayoutConstraint.ActivateConstraints([
            text.TopAnchor.ConstraintEqualTo(safe.TopAnchor, 12), text.LeadingAnchor.ConstraintEqualTo(safe.LeadingAnchor, 16),
            text.TrailingAnchor.ConstraintEqualTo(safe.TrailingAnchor, -16), text.BottomAnchor.ConstraintEqualTo(share.TopAnchor, -8),
            share.LeadingAnchor.ConstraintEqualTo(safe.LeadingAnchor), share.TrailingAnchor.ConstraintEqualTo(safe.TrailingAnchor),
            share.BottomAnchor.ConstraintEqualTo(safe.BottomAnchor, -12), share.HeightAnchor.ConstraintEqualTo(44)
        ]);
        failureWindow = new UIWindow(scene) { RootViewController = controller,
            WindowLevel = UIWindowLevel.Normal + 2 };
        failureWindow.MakeKeyAndVisible();
        GameTrace.Write("failure-ui=ready");
        LogWindows("failure");
    }

}
