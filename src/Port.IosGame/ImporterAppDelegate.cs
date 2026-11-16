using Foundation;
using UIKit;

namespace Port.IosGame;

// Use the same managed UIKit entry/scene ownership as the accepted native probe.
[Register("ImporterAppDelegate")]
public sealed class ImporterAppDelegate : UIApplicationDelegate
{
    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        GameTrace.Write("application=finished-launching");
        return true;
    }

    public override UISceneConfiguration GetConfiguration(UIApplication application,
        UISceneSession connectingSceneSession, UISceneConnectionOptions options)
    {
        GameTrace.Write("scene=configuration-requested");
        // Retain the previous host's configuration name when updating in place.
        return new("SDLSceneConfiguration", connectingSceneSession.Role) { DelegateType = typeof(ImporterSceneDelegate) };
    }
}

[Register("ImporterSceneDelegate")]
public sealed class ImporterSceneDelegate : UIResponder, IUIWindowSceneDelegate
{
    [Export("window")]
    public UIWindow? Window { get; set; }

    [Export("scene:willConnectToSession:options:")]
    public void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions options)
    {
        GameTrace.Write("scene=connecting");
        if (scene is not UIWindowScene windowScene || session.Role != UIWindowSceneSessionRole.Application) return;
        Window = new UIWindow(windowScene);
        try { HostCoordinator.Begin(Window); }
        catch (Exception error) { HostCoordinator.ShowFailure(error); }
    }
}
