using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Media;
using Android.OS;
using Android.OS.Ext;
using Android.Provider;
using Port.Hooks;
using Uri = Android.Net.Uri;
using Path = System.IO.Path;
using OperationCanceledException = System.OperationCanceledException;

namespace Port.AndroidGame;

// Only selected URIs are read. Keep orientation-correct, bounded private PNGs;
// never retain URI permissions or write to the player's original photos.
internal sealed class PhotoPicker(Activity activity, string folder, Action changed, CancellationToken cancellation)
{
    private const int Request = 43;
    private const int ThumbnailSize = 400;
    private bool busy;
    private bool awaitingResult;
    private int selectionLimit;

    internal void Show()
    {
        if (busy || cancellation.IsCancellationRequested) return;
        try
        {
            var count = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.png").Length : 0;
            selectionLimit = Math.Min(DesktopPictures.MaximumPhotoSelection, DesktopPictures.MaximumPictures - count);
            if (selectionLimit <= 0)
            {
                Message($"The {DesktopPictures.MaximumPictures}-photo limit has been reached. Remove a photo in the creator to make room.");
                return;
            }
            busy = true;
            awaitingResult = true;
            // API 30-32 can receive the system picker through a modular update.
            // The .NET API annotations do not describe Android SDK extensions.
#pragma warning disable CA1416
            if (OperatingSystem.IsAndroidVersionAtLeast(33) ||
                OperatingSystem.IsAndroidVersionAtLeast(30) && SdkExtensions.GetExtensionVersion((int)BuildVersionCodes.R) >= 2)
            {
                var intent = new Intent(MediaStore.ActionPickImages);
                intent.SetType("image/*");
                if (selectionLimit > 1)
                    intent.PutExtra(MediaStore.ExtraPickImagesMax, Math.Min(selectionLimit, MediaStore.PickImagesMaxLimit));
                try { activity.StartActivityForResult(intent, Request); return; }
                catch (ActivityNotFoundException) { } // Fall back to the document provider.
            }
#pragma warning restore CA1416
            var fallback = new Intent(Intent.ActionOpenDocument);
            fallback.AddCategory(Intent.CategoryOpenable);
            fallback.SetType("image/*");
            fallback.PutExtra(Intent.ExtraAllowMultiple, selectionLimit > 1);
            fallback.AddFlags(ActivityFlags.GrantReadUriPermission);
            activity.StartActivityForResult(fallback, Request);
        }
        catch (Exception error)
        {
            busy = false;
            awaitingResult = false;
            MainActivity.Log("photo-picker-failure=" + error);
            Message("Could not open the photo picker. Please try again.");
        }
    }

    internal bool HandleResult(int request, Result result, Intent? data)
    {
        if (request != Request) return false;
        if (!awaitingResult) return true;
        awaitingResult = false;
        if (result != Result.Ok || data is null) { busy = false; return true; }
        var selected = new List<Uri>();
        void Add(Uri? uri)
        {
            if (uri is not null && selected.Count < selectionLimit && !selected.Any(existing => existing.Equals(uri)))
                selected.Add(uri);
        }
        if (data.ClipData is { } clip)
            for (var i = 0; i < clip.ItemCount && selected.Count < selectionLimit; i++) Add(clip.GetItemAt(i)?.Uri);
        Add(data.Data);
        // Providers can fetch remote images: decoding and storage must not block
        // either the UI or the game. The completion queues GPU work separately.
        _ = Task.Run(() => Import(selected));
        return true;
    }

    private void Import(List<Uri> selected)
    {
        var imported = 0;
        var failed = 0;
        try
        {
            if (selected.Count == 0) return;
            Directory.CreateDirectory(folder);
            foreach (var uri in selected)
            {
                cancellation.ThrowIfCancellationRequested();
                if (Directory.GetFiles(folder, "*.png").Length >= DesktopPictures.MaximumPictures) break;
                try
                {
                    using var image = Decode(uri);
                    // Unique paths keep saved masks from attaching to a replacement.
                    var path = Path.Combine(folder, $"photo-{DateTime.UtcNow.Ticks:D19}-{Guid.NewGuid():N}.png");
                    try
                    {
                        using (var output = File.Create(path + ".tmp"))
                            if (!image.Compress(Bitmap.CompressFormat.Png!, 100, output))
                                throw new IOException("Could not normalize the photo.");
                        cancellation.ThrowIfCancellationRequested();
                        File.Move(path + ".tmp", path);
                        imported++;
                    }
                    finally { if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp"); }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error) { failed++; MainActivity.Log("photo-import-failure=" + error); }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            MainActivity.Log("photo-picker-failure=" + error);
            activity.RunOnUiThread(() => Message("Photo import could not finish. Existing pictures are kept. Details are in the game log."));
        }
        finally
        {
            if (imported > 0 && !cancellation.IsCancellationRequested) changed();
            MainActivity.Log($"creator-photos imported={imported} failed={failed}");
            activity.RunOnUiThread(() =>
            {
                busy = false;
                if (failed > 0) Message($"{failed} photos could not be imported. Details are in the game log.");
            });
        }
    }

    private Bitmap Decode(Uri uri)
    {
        var resolver = activity.ContentResolver!;
        if (OperatingSystem.IsAndroidVersionAtLeast(28))
        {
            using var source = ImageDecoder.CreateSource(resolver, uri);
            using var listener = new ThumbnailDecoder();
            // ImageDecoder applies EXIF orientation, including mirrored images.
            return ImageDecoder.DecodeBitmap(source, listener);
        }
        return DecodeLegacy(uri);
    }

    internal Bitmap DecodeLegacy(Uri uri)
    {
        var resolver = activity.ContentResolver!;
        using var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
        using (var input = resolver.OpenInputStream(uri) ?? throw new IOException("Could not open photo."))
            BitmapFactory.DecodeStream(input, null, bounds)?.Dispose();
        if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0) throw new InvalidDataException("Unsupported photo.");
        var sample = 1;
        while (Math.Max(bounds.OutWidth, bounds.OutHeight) / sample > ThumbnailSize * 2) sample *= 2;
        using var options = new BitmapFactory.Options { InSampleSize = sample, InPreferredConfig = Bitmap.Config.Argb8888 };
        if (OperatingSystem.IsAndroidVersionAtLeast(26)) options.InPreferredColorSpace = ColorSpace.Get(ColorSpace.Named.Srgb!);
        using var stream = resolver.OpenInputStream(uri) ?? throw new IOException("Could not open photo.");
        using var decoded = BitmapFactory.DecodeStream(stream, null, options) ?? throw new InvalidDataException("Could not decode photo.");
        var orientation = 1;
        using (var metadata = resolver.OpenInputStream(uri) ?? throw new IOException("Could not open photo."))
        {
            try
            {
                using var exif = new ExifInterface(metadata);
                orientation = exif.GetAttributeInt(ExifInterface.TagOrientation, 1);
            }
            catch (Java.IO.IOException) { } // Formats without EXIF still decode.
        }
        using var matrix = new Matrix();
        switch (orientation)
        {
            case 2: matrix.SetScale(-1, 1); break;
            case 3: matrix.SetRotate(180); break;
            case 4: matrix.SetScale(1, -1); break;
            case 5: matrix.SetRotate(90); matrix.PostScale(-1, 1); break;
            case 6: matrix.SetRotate(90); break;
            case 7: matrix.SetRotate(90); matrix.PostScale(1, -1); break;
            case 8: matrix.SetRotate(270); break;
        }
        var scale = Math.Min(1f, ThumbnailSize / (float)Math.Max(decoded.Width, decoded.Height));
        matrix.PostScale(scale, scale);
        // CreateBitmap may return its input for an identity transform. Copy it
        // so disposing the decoded source cannot dispose the returned image.
        using var transformed = Bitmap.CreateBitmap(decoded, 0, 0, decoded.Width, decoded.Height, matrix, true)!;
        return transformed.Copy(Bitmap.Config.Argb8888!, false) ?? throw new IOException("Could not normalize photo.");
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android28.0")]
    private sealed class ThumbnailDecoder : Java.Lang.Object, ImageDecoder.IOnHeaderDecodedListener
    {
        public void OnHeaderDecoded(ImageDecoder decoder, ImageDecoder.ImageInfo info, ImageDecoder.Source source)
        {
            var size = info.Size;
            var scale = Math.Min(1d, ThumbnailSize / (double)Math.Max(size.Width, size.Height));
            decoder.SetTargetSize(Math.Max(1, (int)(size.Width * scale)), Math.Max(1, (int)(size.Height * scale)));
            decoder.Allocator = ImageDecoderAllocator.Software;
            using var colorSpace = ColorSpace.Get(ColorSpace.Named.Srgb!);
            decoder.SetTargetColorSpace(colorSpace!);
        }
    }

    private void Message(string message)
    {
        if (cancellation.IsCancellationRequested || activity.IsFinishing || activity.IsDestroyed) return;
        new AlertDialog.Builder(activity)!.SetTitle("Creator photos")!.SetMessage(message)!
            .SetPositiveButton("OK", (_, _) => { })!.Show();
    }
}
