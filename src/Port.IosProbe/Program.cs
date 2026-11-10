using Foundation;
using UIKit;
using UniformTypeIdentifiers;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace Port.IosProbe;

public static class Program
{
    public static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}

[Register("AppDelegate")]
public sealed class AppDelegate : UIApplicationDelegate
{
    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions) => true;

    public override UISceneConfiguration GetConfiguration(UIApplication application,
        UISceneSession connectingSceneSession, UISceneConnectionOptions options) =>
        new("Probe", connectingSceneSession.Role);
}

[Register("SceneDelegate")]
public sealed class SceneDelegate : UIResponder, IUIWindowSceneDelegate
{
    [Export("window")]
    public UIWindow? Window { get; set; }

    [Export("scene:willConnectToSession:options:")]
    public void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions options)
    {
        if (scene is not UIWindowScene windowScene) return;
        Window = new UIWindow(windowScene) { RootViewController = new ProbeController() };
        Window.MakeKeyAndVisible();
    }
}

public sealed class ProbeController : UIViewController
{
    private readonly UITextView output = new() { Editable = false };
    private readonly List<UIButton> buttons = [];
    private readonly object logLock = new();
    private UIDocumentPickerViewController? picker;
    private string root = "";
    private string trace = "";
    private bool started;
    private int updates;

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.SystemBackground;
        root = Path.Combine(NSFileManager.DefaultManager.GetUrls(
            NSSearchPathDirectory.ApplicationSupportDirectory, NSSearchPathDomain.User)[0].Path!,
            "Z0MB1ES-execution-probe");
        Directory.CreateDirectory(root);
        trace = Path.Combine(root, "device-trace.txt");
        var title = new UILabel
        {
            Text = "Execution experiment — no playable game yet",
            Lines = 0,
            TextAlignment = UITextAlignment.Center
        };
        var generate = Button("Test generated IL", async () => await Run(() =>
        {
            Log("generated-il=starting");
            Log(ExecutionProbe.RunGeneratedIl(Path.Combine(root, "il")));
        }));
        var import = Button("Import XAP and convert songs", PickXap);
        var share = Button("Share device trace", Share);
        var stack = new UIStackView([title, generate, import, share, output])
        {
            Axis = UILayoutConstraintAxis.Vertical,
            Spacing = 12,
            TranslatesAutoresizingMaskIntoConstraints = false
        };
        View.AddSubview(stack);
        var safe = View.SafeAreaLayoutGuide;
        NSLayoutConstraint.ActivateConstraints([
            stack.TopAnchor.ConstraintEqualTo(safe.TopAnchor, 12),
            stack.BottomAnchor.ConstraintEqualTo(safe.BottomAnchor, -12),
            stack.LeadingAnchor.ConstraintEqualTo(safe.LeadingAnchor, 16),
            stack.TrailingAnchor.ConstraintEqualTo(safe.TrailingAnchor, -16)
        ]);
    }

    public override async void ViewDidAppear(bool animated)
    {
        base.ViewDidAppear(animated);
        if (started) return;
        started = true;
        Log($"launch utc={DateTime.UtcNow:O} device={UIDevice.CurrentDevice.Model} " +
            $"ios={UIDevice.CurrentDevice.SystemVersion} runtime={RuntimeInformation.FrameworkDescription} " +
            $"arch={RuntimeInformation.ProcessArchitecture} dynamicCodeCompiled={RuntimeFeature.IsDynamicCodeCompiled}");
        Log("Expected build: Release ios-arm64 Mono UseInterpreter=true TrimMode=copy; signing by AltStore. " +
            "Record launch/JIT conditions separately; runtime flags alone do not prove interpreter use.");
        await Run(() =>
        {
            Log("persistent-il=starting");
            Log(ExecutionProbe.ResumeGeneratedIl(Path.Combine(root, "il")));
        });
        await Run(() => Log(ExecutionProbe.ResumeImport(Path.Combine(root, "imports"))));
    }

    private UIButton Button(string title, Action action)
    {
        var button = new UIButton(UIButtonType.System);
        button.SetTitle(title, UIControlState.Normal);
        button.TouchUpInside += (_, _) => action();
        buttons.Add(button);
        return button;
    }

    private void PickXap()
    {
        // Copy mode gives us a local picker-owned file; never retain external security scopes.
        picker = new UIDocumentPickerViewController([UTTypes.Data], asCopy: true)
        {
            AllowsMultipleSelection = false
        };
        picker.WasCancelled += (_, _) => Log("picker=cancelled; previous experiments retained");
        picker.DidPickDocumentAtUrls += async (_, args) =>
        {
            var path = args.Urls.Single().Path;
            if (path is null) { Log("picker=failed (no local path)"); return; }
            var converterIdentity = File.ReadAllText(Path.Combine(NSBundle.MainBundle.BundlePath,
                "converter-identity.txt")).Trim();
            await Run(() =>
            {
                var result = ExecutionProbe.ImportXap(path, Path.Combine(root, "imports"),
                    new NativeAudioConverter(converterIdentity), Log);
                Log($"import=passed persistentAttempt={Path.GetFileName(result)}; no gameplay tested");
            });
        };
        PresentViewController(picker, true, null);
    }

    private async Task Run(Action action)
    {
        foreach (var button in buttons) button.Enabled = false;
        try { await Task.Run(action); }
        catch (Exception error) { Log($"failure={error}"); }
        finally { foreach (var button in buttons) button.Enabled = true; }
    }

    private void Log(string message)
    {
        // Avoid thousands of UI updates while preserving durable boundaries before native/IL calls.
        if (message.StartsWith("content=", StringComparison.Ordinal) && ++updates % 20 != 0) return;
        lock (logLock) File.AppendAllText(trace, $"{DateTime.UtcNow:O} {message}\n");
        InvokeOnMainThread(() => output.Text += message + "\n");
    }

    private void Share()
    {
        var share = new UIActivityViewController([NSUrl.FromFilename(trace)], null);
        if (share.PopoverPresentationController is { } popover)
        {
            popover.SourceView = View!;
            popover.SourceRect = View!.Bounds;
        }
        PresentViewController(share, true, null);
    }
}
