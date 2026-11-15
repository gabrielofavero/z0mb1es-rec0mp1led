using Foundation;
using UIKit;
using UniformTypeIdentifiers;
using Microsoft.Xna.Framework;
using Port.Art;
using Port.Hooks;
using Port.Prepare;
using SDL3;

namespace Port.IosGame;

internal static class GameUi
{
    private static ImportController? importer;
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

    internal static void ShowAssetMenu(Game game, MobileSession session)
    {
        var root = Root(game);
        if (root.PresentedViewController is not null) return;
        var sheet = UIAlertController.Create("Manage assets", null, UIAlertControllerStyle.ActionSheet);
        sheet.AddAction(UIAlertAction.Create("Import / replace game or artwork", UIAlertActionStyle.Default, _ =>
        {
            GameData.RequestImport(true);
            Message(root, "Import ready", "Close Z0MB1ES from the app switcher and reopen it to import safely. Your saves stay in place.");
        }));
        sheet.AddAction(UIAlertAction.Create("Add creator photos", UIAlertActionStyle.Default, _ => PickPhotos(game, session)));
        foreach (var (mode, title) in new[] { ("normal", "I MAED"), ("viking", "Time Viking"), ("endless", "Endless") })
        {
            var label = mode switch { "normal" => AssetChoices.NormalLabel, "viking" => AssetChoices.VikingLabel, _ => AssetChoices.EndlessLabel };
            sheet.AddAction(UIAlertAction.Create(title + " artwork: " + label, UIAlertActionStyle.Default, _ => session.Enqueue(() =>
            {
                var error = AssetChoices.Toggle(mode);
                if (error.Length > 0) root.BeginInvokeOnMainThread(() => Message(root, "Artwork", error));
            })));
        }
        sheet.AddAction(UIAlertAction.Create("Share game log", UIAlertActionStyle.Default, _ => Share(root)));
        sheet.AddAction(UIAlertAction.Create("Return to game", UIAlertActionStyle.Cancel, null));
        Anchor(sheet, root.View!);
        root.PresentViewController(sheet, true, null);
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

    internal static void ShowImporter(string? failure, bool allowLaunch)
    {
        GameTrace.Write("importer-ui=attaching-native-window");
        importer = new ImportController(failure, allowLaunch, HostCoordinator.ImportFinished);
        HostCoordinator.Present(importer);
        GameTrace.Write("importer-ui=attached");
        UiDiagnostics.Schedule("importer", HostCoordinator.Window);
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

    private static void Anchor(UIAlertController alert, UIView view)
    {
        if (alert.PopoverPresentationController is { } popover)
        { popover.SourceView = view; popover.SourceRect = view.Bounds; }
    }
}

internal sealed class ImportController(string? failure, bool allowLaunch, Action ready) : UIViewController
{
    private readonly UITextView status = new() { Editable = false, ScrollEnabled = true };
    private readonly UILabel selected = new() { Lines = 0 };
    private readonly List<UIButton> actions = [];
    private readonly List<NSUrl> scopedUrls = [];
    private UIDocumentPickerViewController? picker;
    private CancellationTokenSource? importCancellation;
    private string? xap;
    private ArtImport? normal;
    private ArtImport? viking;
    private bool hasCurrent;
    private NSObject? backgroundObserver;

    public override void ViewDidLoad()
    {
        GameTrace.Write("importer-view=loading");
        base.ViewDidLoad();
        View!.BackgroundColor = UIColor.SystemBackground;
        hasCurrent = GameData.Store.TryOpen() is not null;
        backgroundObserver = NSNotificationCenter.DefaultCenter.AddObserver(UIApplication.DidEnterBackgroundNotification,
            _ => importCancellation?.Cancel());
        var title = new UILabel { Text = "Z0MB1ES", Font = UIFont.BoldSystemFontOfSize(28), Lines = 0 };
        var subtitle = new UILabel { Text = "Choose your phone XAP. Artwork from I MAED or Time Viking is optional; original phone artwork is always available.", Lines = 0 };
        var phone = Button("Choose phone XAP", () => Pick("phone"));
        var steam = Button("I MAED artwork — Steam ZIP", () => Pick("i-maed-steam"));
        var folder = Button("I MAED artwork — Steam folder", () => Pick("i-maed-steam", directory: true));
        var xbox = Button("I MAED artwork — Xbox 360 LIVE file", () => Pick("i-maed-360"));
        var time = Button("Time Viking artwork — Xbox 360 LIVE file", () => Pick("time-viking-360"));
        var clear = Button("Clear pending artwork choices", () => { normal = viking = null; UpdateSelection(); });
        var prepare = Button("Import and play", Import);
        var play = Button("Play current game", Finish);
        play.Hidden = !hasCurrent || !allowLaunch;
        var share = Button("Share game log", () => GameUi.Share(this));
        var cancel = Button("Cancel import", () => importCancellation?.Cancel());
        actions.Remove(cancel);
        status.HeightAnchor.ConstraintEqualTo(120).Active = true;
        var stack = new UIStackView([title, subtitle, phone, steam, folder, xbox, time, clear, selected, prepare, play, share, cancel, status])
        { Axis = UILayoutConstraintAxis.Vertical, Spacing = 8, TranslatesAutoresizingMaskIntoConstraints = false };
        var scroll = new UIScrollView { TranslatesAutoresizingMaskIntoConstraints = false };
        View.AddSubview(scroll);
        scroll.AddSubview(stack);
        var safe = View.SafeAreaLayoutGuide;
        NSLayoutConstraint.ActivateConstraints([
            scroll.TopAnchor.ConstraintEqualTo(safe.TopAnchor), scroll.BottomAnchor.ConstraintEqualTo(safe.BottomAnchor),
            scroll.LeadingAnchor.ConstraintEqualTo(safe.LeadingAnchor), scroll.TrailingAnchor.ConstraintEqualTo(safe.TrailingAnchor),
            stack.TopAnchor.ConstraintEqualTo(scroll.ContentLayoutGuide.TopAnchor, 12),
            stack.BottomAnchor.ConstraintEqualTo(scroll.ContentLayoutGuide.BottomAnchor, -12),
            stack.LeadingAnchor.ConstraintEqualTo(scroll.ContentLayoutGuide.LeadingAnchor, 16),
            stack.TrailingAnchor.ConstraintEqualTo(scroll.ContentLayoutGuide.TrailingAnchor, -16),
            stack.WidthAnchor.ConstraintEqualTo(scroll.FrameLayoutGuide.WidthAnchor, -32)
        ]);
        UpdateSelection();
        status.Text = failure is null ? "Your saves are kept separately from imported assets." :
            "Game startup failed: " + failure + "\nShare the game log or reimport, then reopen the app.";
        GameTrace.Write("importer-view=loaded");
    }

    public override void ViewDidAppear(bool animated)
    {
        base.ViewDidAppear(animated);
        GameTrace.Write("importer-view=visible");
        UiDiagnostics.Capture("importer-appeared", View?.Window);
    }

    private UIButton Button(string title, Action action)
    {
        var button = new UIButton(UIButtonType.System);
        button.SetTitle(title, UIControlState.Normal);
        button.TouchUpInside += (_, _) => action();
        actions.Add(button);
        return button;
    }

    private void UpdateSelection() => selected.Text =
        "Phone: " + (xap is null ? hasCurrent ? "keep current" : "XAP required" : Path.GetFileName(xap)) +
        "\nI MAED: " + (normal?.Kind ?? (hasCurrent ? "keep current / original" : "original")) +
        "\nTime Viking: " + (viking is null ? hasCurrent ? "keep current / original" : "original" : "Xbox 360");

    private void Pick(string kind, bool directory = false)
    {
        picker = new UIDocumentPickerViewController([directory ? UTTypes.Folder : UTTypes.Data], asCopy: !directory)
        { AllowsMultipleSelection = false };
        picker.WasCancelled += (_, _) => { status.Text = "Selection canceled. Previous choices kept."; };
        picker.DidPickDocumentAtUrls += async (_, args) =>
        {
            var url = args.Urls.Single();
            var path = url.Path;
            if (path is null) { status.Text = "The picker did not supply a local file."; return; }
            if (directory && url.StartAccessingSecurityScopedResource()) scopedUrls.Add(url);
            SetBusy(true);
            try
            {
                var version = await Task.Run(() => kind == "phone" ? PreparationEngine.ValidateXap(path).SupportedVersion :
                    new PortableArtImporter().Preview(kind, path).Version);
                if (kind == "phone") xap = path;
                else if (kind == "time-viking-360") viking = new ArtImport(kind, path);
                else normal = new ArtImport(kind, path);
                status.Text = "Selected: " + version;
                UpdateSelection();
            }
            catch (Exception error)
            {
                GameTrace.Write("source-selection-failure=" + error);
                status.Text = "Unsupported or damaged source: " + error.GetBaseException().Message + "\nPrevious choices kept.";
            }
            finally { SetBusy(false); }
        };
        PresentViewController(picker, true, null);
    }

    private void SetBusy(bool busy) { foreach (var action in actions) action.Enabled = !busy; }

    private async void Import()
    {
        SetBusy(true);
        using var cancellation = new CancellationTokenSource();
        importCancellation = cancellation;
        try
        {
            var progress = new Progress<string>(stage => status.Text = stage);
            await Task.Run(() => GameData.Store.Import(new AssetImportSelection(xap, normal, viking), stage =>
            {
                GameTrace.Write("import-stage=" + stage);
                ((IProgress<string>)progress).Report(stage);
            }, cancellation.Token));
            hasCurrent = true;
            GameTrace.Write("game-import=passed");
            Finish();
        }
        catch (Exception error)
        {
            GameTrace.Write("game-import-failure=" + error);
            status.Text = error is OperationCanceledException ? "Import canceled. Existing assets and saves retained." :
                "Import failed: " + error.GetBaseException().Message + "\nExisting assets and saves retained. Share the game log.";
        }
        finally { importCancellation = null; SetBusy(false); }
    }

    private void Finish()
    {
        GameData.RequestImport(false);
        if (backgroundObserver is not null)
        {
            NSNotificationCenter.DefaultCenter.RemoveObserver(backgroundObserver);
            backgroundObserver.Dispose();
            backgroundObserver = null;
        }
        foreach (var url in scopedUrls) url.StopAccessingSecurityScopedResource();
        scopedUrls.Clear();
        if (allowLaunch) ready();
        else status.Text = "Import ready. Close Z0MB1ES from the app switcher and reopen to play.";
    }
}
