using Microsoft.Xna.Framework;
using ObjCRuntime;
using Port.Hooks;
using SDL3;
using UIKit;

namespace Port.IosGame;

internal static class MobileDisplay
{
    internal static void Configure(Game game)
    {
        UIWindow? Window()
        {
            var pointer = SDL.SDL_GetPointerProperty(SDL.SDL_GetWindowProperties(game.Window.Handle),
                SDL.SDL_PROP_WINDOW_UIKIT_WINDOW_POINTER, IntPtr.Zero);
            return pointer == IntPtr.Zero ? null : Runtime.GetNSObject<UIWindow>(pointer);
        }
        void Apply(bool responsive)
        {
            var window = Window();
            var controller = window?.RootViewController;
            if (controller is null) return;
            // SDL reads SDL_ORIENTATIONS in supportedInterfaceOrientations. Ask
            // UIKit to query it again after changing the in-game preference.
            if (OperatingSystem.IsIOSVersionAtLeast(16))
            {
                controller.SetNeedsUpdateOfSupportedInterfaceOrientations();
                if (window?.WindowScene is { } scene)
                {
                    var mask = UIInterfaceOrientationMask.Landscape;
                    if (responsive)
                        mask = UIDevice.CurrentDevice.Orientation == UIDeviceOrientation.Portrait
                            ? UIInterfaceOrientationMask.Portrait
                            : UIInterfaceOrientationMask.AllButUpsideDown;
                    scene.RequestGeometryUpdate(new UIWindowSceneGeometryPreferencesIOS(mask),
                        error => GameTrace.Write("orientation-request=" + error.LocalizedDescription));
                }
            }
            else UIViewController.AttemptRotationToDeviceOrientation();
        }
        Vector4 Insets()
        {
            var window = Window();
            if (window is null || window.Bounds.Width <= 0 || window.Bounds.Height <= 0) return Vector4.Zero;
            var safe = window.SafeAreaInsets;
            return new Vector4((float)(safe.Left / window.Bounds.Width), (float)(safe.Top / window.Bounds.Height),
                (float)(safe.Right / window.Bounds.Width), (float)(safe.Bottom / window.Bounds.Height));
        }
        DesktopDisplay.ResponsiveOrientationChanged += Apply;
        DesktopDisplay.MobileSafeInsets = Insets;
        UIDevice.CurrentDevice.BeginGeneratingDeviceOrientationNotifications();
        Apply(DesktopDisplay.ResponsiveOrientationEnabled);
        game.Exiting += (_, _) =>
        {
            DesktopDisplay.ResponsiveOrientationChanged -= Apply;
            DesktopDisplay.MobileSafeInsets = null;
            UIDevice.CurrentDevice.EndGeneratingDeviceOrientationNotifications();
        };
    }
}
