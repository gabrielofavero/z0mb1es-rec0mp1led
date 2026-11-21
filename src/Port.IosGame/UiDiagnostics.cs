using Foundation;
using UIKit;

namespace Port.IosGame;

// These entry points are used by the importer host's existing lifecycle logging.
internal static class UiDiagnostics
{
    internal static void Start() => GameTrace.Write("ui-diagnostics=started");

    internal static async void Schedule(string boundary, UIWindow window)
    {
        try
        {
            await Task.Delay(1000);
            window.BeginInvokeOnMainThread(() => Capture(boundary, window));
        }
        catch (Exception error) { GameTrace.Write("ui-state-failure=" + error); }
    }

    internal static void Capture(string boundary, UIWindow? window)
    {
        try
        {
            var root = window?.RootViewController;
            GameTrace.Write($"ui-state boundary={boundary} mainThread={NSThread.IsMain} " +
                $"application={UIApplication.SharedApplication.ApplicationState} hidden={window?.Hidden} " +
                $"key={window?.IsKeyWindow} bounds={window?.Bounds} root={root?.GetType().Name} " +
                $"frame={root?.View?.Frame} ambiguous={root?.View?.HasAmbiguousLayout}");
        }
        catch (Exception error) { GameTrace.Write("ui-state-failure=" + error); }
    }
}
