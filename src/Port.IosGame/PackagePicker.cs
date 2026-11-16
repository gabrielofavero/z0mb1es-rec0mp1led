using Foundation;
using UIKit;
using UniformTypeIdentifiers;

namespace Port.IosGame;

// The system downloads provider/iCloud files before handing us a sandbox copy.
// Keep this delegate alive for the modal controller's entire lifetime.
internal sealed class PackagePicker(UIViewController root, Action<string> selected, Action finished) : UIDocumentPickerDelegate
{
    private UIDocumentPickerViewController? picker;
    private bool completed;

    internal void Show()
    {
        if (root.PresentedViewController is not null) { Finish(); return; }
        // Include unknown extensions and extensionless LIVE packages. Identity is
        // checked by the importer, rather than trusting the provider's file type.
        picker = new UIDocumentPickerViewController([UTTypes.Item], asCopy: true)
        {
            Delegate = this,
            AllowsMultipleSelection = false,
            ShouldShowFileExtensions = true,
            ModalPresentationStyle = UIModalPresentationStyle.FullScreen
        };
        root.PresentViewController(picker, true, null);
    }

    public override void DidPickDocument(UIDocumentPickerViewController controller, NSUrl[] urls)
    {
        try
        {
            if (urls.Length == 1 && urls[0].IsFileUrl && urls[0].Path is { } path && File.Exists(path))
                selected(path); // Immediately move the temporary copy into setup-owned staging.
            else GameTrace.Write("package-picker=no-file");
        }
        catch (Exception error) { GameTrace.Write("package-picker-failure=" + error); }
        finally { controller.DismissViewController(true, Finish); }
    }

    public override void WasCancelled(UIDocumentPickerViewController controller) =>
        controller.DismissViewController(true, Finish);

    private void Finish()
    {
        if (completed) return;
        completed = true;
        finished();
    }
}
