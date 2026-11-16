using Foundation;
using UIKit;
using Microsoft.Xna.Framework;
using Port.Hooks;
using SDL3;

namespace Port.IosGame;

internal static class GameUi
{
    private static PhotoPicker? photos;

    internal static UIWindow NativeWindow(Game game)
    {
        var pointer = SDL.SDL_GetPointerProperty(SDL.SDL_GetWindowProperties(game.Window.Handle),
            SDL.SDL_PROP_WINDOW_UIKIT_WINDOW_POINTER, IntPtr.Zero);
        var window = ObjCRuntime.Runtime.GetNSObject<UIWindow>(pointer)
            ?? throw new InvalidOperationException("SDL did not expose its UIKit window.");
        return window;
    }

    internal static UIViewController Root(Game game) => NativeWindow(game).RootViewController
        ?? throw new InvalidOperationException("SDL window has no view controller.");

    internal static void Reimport(Game game, MobileSession session)
    {
        session.PauseForMenu();
        session.SaveForBackground();
        GameData.RequestImport(true);
        GameTrace.Write("reimport=opening-setup");
        game.Exit();
    }

    internal static void PickPhotos(Game game, MobileSession session)
    {
        var root = Root(game);
        // Wait for any previous system controller to dismiss.
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

    private static async void WhenReady(UIViewController root, Action action)
    {
        try
        {
            // Presentation can still be finishing when the picker is requested.
            for (var attempt = 0; attempt < 40 && root.PresentedViewController is not null; attempt++)
                await Task.Delay(50);
            if (root.PresentedViewController is null) action();
        }
        catch (Exception error) { GameTrace.Write("system-ui-failure=" + error); }
    }

}
