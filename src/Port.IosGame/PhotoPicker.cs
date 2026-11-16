using Foundation;
using CoreGraphics;
using ImageIO;
using PhotosUI;
using UIKit;
using Port.Hooks;

namespace Port.IosGame;

// PHPicker grants access only to pictures the player chooses. Originals never
// enter game content: normalize orientation and retain bounded private thumbnails.
internal sealed class PhotoPicker(UIViewController root, Action changed) : PHPickerViewControllerDelegate
{
    private PHPickerViewController? picker;
    private bool importing;
    internal bool IsBusy => importing || picker?.PresentingViewController is not null;

    internal void Show()
    {
        if (root.PresentedViewController is not null) return;
        var folder = Path.Combine(GameData.Root, "Pictures");
        var count = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.png").Length : 0;
        if (count >= DesktopPictures.MaximumPictures) { GameUi.Message(root, "Creator photos", $"The {DesktopPictures.MaximumPictures}-photo limit has been reached. Remove a photo in the creator to make room."); return; }
        using var config = new PHPickerConfiguration { Filter = PHPickerFilter.ImagesFilter, SelectionLimit = Math.Min(DesktopPictures.MaximumPhotoSelection, DesktopPictures.MaximumPictures - count) };
        picker = new PHPickerViewController(config) { Delegate = this };
        root.PresentViewController(picker, true, null);
    }

    public override async void DidFinishPicking(PHPickerViewController controller, PHPickerResult[] results)
    {
        if (importing) return;
        importing = true;
        try
        {
            controller.DismissViewController(true, null);
            var imported = 0;
            var failed = 0;
            var folder = Path.Combine(GameData.Root, "Pictures");
            Directory.CreateDirectory(folder);
            foreach (var result in results.Take(DesktopPictures.MaximumPhotoSelection))
            {
                // The provider's temporary URL expires when its callback returns.
                // Decode and save inside that callback, before completing the task.
                var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                result.ItemProvider.LoadFileRepresentation("public.image", (url, error) =>
                {
                    try
                    {
                        if (error is not null || url is null) throw new IOException("Photo provider could not load the chosen image.");
                        using var source = CGImageSource.FromUrl(url) ?? throw new InvalidDataException("Unsupported photo.");
                        using var image = source.CreateThumbnail(0, new CGImageThumbnailOptions
                        { CreateThumbnailFromImageAlways = true, CreateThumbnailWithTransform = true, MaxPixelSize = 400 })
                            ?? throw new InvalidDataException("Could not decode the photo.");
                        using var colorSpace = CGColorSpace.CreateSrgb();
                        using var bitmap = new CGBitmapContext(IntPtr.Zero, image.Width, image.Height,
                            8, image.Width * 4, colorSpace, CGBitmapFlags.PremultipliedLast | CGBitmapFlags.ByteOrder32Big);
                        bitmap.DrawImage(new CGRect(0, 0, image.Width, image.Height), image);
                        using var normalized = bitmap.ToImage() ?? throw new InvalidDataException("Could not convert the photo to sRGB.");
                        using var thumbnail = UIImage.FromImage(normalized);
                        using var png = thumbnail.AsPNG() ?? throw new InvalidDataException("Could not normalize the photo.");
                        if (Directory.GetFiles(folder, "*.png").Length >= DesktopPictures.MaximumPictures)
                            throw new IOException("The creator photo limit has been reached.");
                        // Never reuse an ID after removal: saved masks must not attach
                        // to a different picture on the next import or restart.
                        var path = Path.Combine(folder, $"photo-{DateTime.UtcNow.Ticks:D19}-{Guid.NewGuid():N}.png");
                        try
                        {
                            if (!png.Save(path + ".tmp", false, out var saveError))
                                throw new IOException(saveError?.LocalizedDescription ?? "Could not store the photo.");
                            File.Move(path + ".tmp", path);
                        }
                        finally { if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp"); }
                        completion.TrySetResult(true);
                    }
                    catch (Exception failure)
                    {
                        GameTrace.Write("photo-import-failure=" + failure);
                        completion.TrySetResult(false);
                    }
                });
                if (await completion.Task) imported++; else failed++;
            }
            if (imported > 0) changed(); // GPU upload is queued on the game's update thread.
            GameTrace.Write($"creator-photos imported={imported} failed={failed}");
            if (results.Length > 0)
                GameUi.Message(root, "Creator photos", $"Added {imported} photos." + (failed > 0 ? $" {failed} could not be imported; details are in the game log." : " Your photos are ready in the creator."));
        }
        catch (Exception failure)
        {
            GameTrace.Write("photo-picker-failure=" + failure);
            GameUi.Message(root, "Creator photos", "Photo import could not finish. Existing pictures are kept. Details are in the game log.");
        }
        finally { importing = false; }
    }
}
