using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Media;
using Android.OS;
using Android.Provider;
using System.Security.Cryptography;
using Uri = Android.Net.Uri;
using Path = System.IO.Path;

namespace Port.AndroidGame;

// A separate test app exercises the real Android decoder and storage. Intercept
// only presentation, so checks run unattended without opening another activity.
[Activity(MainLauncher = true, Exported = true)]
public sealed class MainActivity : Activity
{
    private Intent? launched;
    private int request;
    private int launches;
    private static int batches;
    internal static void Log(string message)
    {
        Android.Util.Log.Info("PhotoChecks", message);
        if (message.StartsWith("creator-photos ", StringComparison.Ordinal)) Interlocked.Increment(ref batches);
    }

    public override void StartActivityForResult(Intent? intent, int requestCode)
    {
        launched = intent;
        request = requestCode;
        launches++;
    }

    protected override async void OnCreate(Bundle? state)
    {
        base.OnCreate(state);
        try { await Check(); Log("ANDROID_PHOTOS_OK"); }
        catch (Exception error) { Log("ANDROID_PHOTOS_FAILED " + error); }
        finally { Finish(); }
    }

    private async Task Check()
    {
        var root = Path.Combine(CacheDir!.AbsolutePath, "photos-" + Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(root, "Pictures");
        Directory.CreateDirectory(root);
        try
        {
            // Asymmetric image detects both EXIF rotation and mirroring.
            var original = Path.Combine(root, "original.jpg");
            using (var bitmap = Bitmap.CreateBitmap(1200, 600, Bitmap.Config.Argb8888!)!)
            {
                using var canvas = new Canvas(bitmap);
                canvas.DrawColor(Color.Blue);
                using var paint = new Paint { Color = Color.Red };
                canvas.DrawRect(0, 0, 600, 600, paint);
                paint.Color = Color.Green;
                canvas.DrawRect(0, 300, 600, 600, paint);
                paint.Color = Color.Yellow;
                canvas.DrawRect(600, 300, 1200, 600, paint);
                using var output = File.Create(original);
                Require(bitmap.Compress(Bitmap.CompressFormat.Jpeg!, 95, output), "Fixture encode failed.");
            }
            using (var exif = new ExifInterface(original))
            {
                exif.SetAttribute(ExifInterface.TagOrientation, "6");
                exif.SaveAttributes();
            }
            var hash = SHA256.HashData(File.ReadAllBytes(original));
            var changed = 0;
            var picker = new PhotoPicker(this, folder, () => Interlocked.Increment(ref changed), CancellationToken.None);
            picker.Show();
            Require(launched?.Type == "image/*", "Picker did not filter images.");
            var before = launches;
            picker.Show();
            Require(launches == before, "Picker opened twice.");
            Require(!picker.HandleResult(request + 1, Result.Ok, null), "Picker consumed another request.");
            picker.HandleResult(request, Result.Canceled, null);
            Require(changed == 0 && !Directory.Exists(folder), "Cancel changed the collection.");

            picker.Show();
            Require(launches == before + 1, "Cancel blocked reopening.");
            var uri = Uri.FromFile(new Java.IO.File(original))!;
            using var result = new Intent();
            result.SetData(uri);
            result.ClipData = ClipData.NewRawUri("photo", uri);
            picker.HandleResult(request, Result.Ok, result);
            picker.HandleResult(request, Result.Ok, result); // Duplicate callbacks must not import twice.
            await WaitFor(() => changed == 1);
            await Task.Delay(150); // Let the UI completion reset the busy guard.
            var photos = Directory.GetFiles(folder, "*.png");
            Require(photos.Length == 1, "URI deduplication or duplicate callback guard failed.");
            using (var thumbnail = BitmapFactory.DecodeFile(photos[0])!)
            {
                Require(thumbnail.Width == 200 && thumbnail.Height == 400, "Thumbnail size/orientation differs.");
                Require(Matches(Color.Green, thumbnail.GetPixel(25, 25)) &&
                    Matches(Color.Blue, thumbnail.GetPixel(175, 375)), "EXIF rotation was lost.");
                if (OperatingSystem.IsAndroidVersionAtLeast(26))
                    Require(thumbnail.ColorSpace?.IsSrgb == true, "Thumbnail is not sRGB.");
            }
            Require(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(original))), "Import modified the original.");
            Require(Directory.GetFiles(folder, "*.tmp").Length == 0, "Import left temporary files.");
            CheckLegacy(picker, original, root);

            picker.Show();
            var completed = Volatile.Read(ref batches);
            using var missing = new Intent();
            missing.SetData(Uri.FromFile(new Java.IO.File(Path.Combine(root, "missing.jpg"))));
            picker.HandleResult(request, Result.Ok, missing);
            await WaitFor(() => Volatile.Read(ref batches) > completed);
            await Task.Delay(150);
            Require(changed == 1 && Directory.GetFiles(folder, "*.png").Length == 1 &&
                Directory.GetFiles(folder, "*.tmp").Length == 0, "Failed import changed existing photos or left staging files.");

            // The document fallback can return more than the requested limit.
            for (var i = 1; i < 255; i++) File.Copy(photos[0], Path.Combine(folder, $"existing-{i}.png"));
            picker.Show();
            Require(!launched!.GetBooleanExtra(Intent.ExtraAllowMultiple, false), "Last slot requested multiple documents.");
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
                Require(!launched.HasExtra(MediaStore.ExtraPickImagesMax), "Last slot requested multiple photos.");
            using var overflow = new Intent();
            overflow.ClipData = ClipData.NewRawUri("photos", uri);
            var second = Path.Combine(root, "second.jpg");
            File.Copy(original, second);
            overflow.ClipData!.AddItem(new ClipData.Item(Uri.FromFile(new Java.IO.File(second))!));
            picker.HandleResult(request, Result.Ok, overflow);
            await WaitFor(() => changed == 2);
            await Task.Delay(150);
            Require(Directory.GetFiles(folder, "*.png").Length == 256, "Import exceeded the collection limit.");
            before = launches;
            picker.Show();
            Require(launches == before, "Full collection still launched a picker.");
            Log("cancel/reopen; duplicate callbacks/URIs; EXIF rotation; all legacy EXIF transforms; 400px sRGB PNG; original unchanged; failure recovery; final-slot limit");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static bool Matches(Color expected, int pixel) =>
        Math.Abs(expected.R - Color.GetRedComponent(pixel)) < 30 &&
        Math.Abs(expected.G - Color.GetGreenComponent(pixel)) < 30 &&
        Math.Abs(expected.B - Color.GetBlueComponent(pixel)) < 30;

    private static void CheckLegacy(PhotoPicker picker, string original, string root)
    {
        var path = Path.Combine(root, "orientations.jpg");
        File.Copy(original, path);
        var r = Color.Red; var b = Color.Blue; var g = Color.Green; var y = Color.Yellow;
        Color[][] corners = [[r, b, g, y], [b, r, y, g], [y, g, b, r], [g, y, r, b],
            [r, g, b, y], [g, r, y, b], [y, b, g, r], [b, y, r, g]];
        for (var orientation = 1; orientation <= 8; orientation++)
        {
            using (var exif = new ExifInterface(path))
            {
                exif.SetAttribute(ExifInterface.TagOrientation, orientation.ToString());
                exif.SaveAttributes();
            }
            using var image = picker.DecodeLegacy(Uri.FromFile(new Java.IO.File(path))!);
            Require(image.Width == (orientation >= 5 ? 200 : 400) &&
                image.Height == (orientation >= 5 ? 400 : 200), "Legacy thumbnail bounds differ.");
            var positions = new[] { (25, 25), (image.Width - 25, 25),
                (25, image.Height - 25), (image.Width - 25, image.Height - 25) };
            for (var i = 0; i < positions.Length; i++)
                Require(Matches(corners[orientation - 1][i], image.GetPixel(positions[i].Item1, positions[i].Item2)),
                    $"Legacy EXIF orientation {orientation} differs at corner {i}.");
        }
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(50);
        Require(condition(), "Photo import completion timed out.");
    }
}
