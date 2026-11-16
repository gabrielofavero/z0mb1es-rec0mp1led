using Foundation;
using Port.Prepare;
using Port.IosProbe;
using Port.Art;

namespace Port.IosGame;

internal static class GameData
{
    internal static string Root => Path.Combine(NSFileManager.DefaultManager.GetUrls(
        NSSearchPathDirectory.ApplicationSupportDirectory, NSSearchPathDomain.User)[0].Path!, "Z0MB1ES-ios-game");
    internal static string Saves => Path.Combine(Root, "Saves");
    internal static string Imports => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Imports");
    internal static void EnsureImportFolder()
    {
        Directory.CreateDirectory(Imports);
        var instructions = Path.Combine(Imports, "README.txt");
        if (!File.Exists(instructions)) File.WriteAllText(instructions, ImportFolder.Instructions + "\n");
    }
    internal static void CleanInterruptedImports()
    {
        var assets = Path.Combine(Root, "Assets");
        if (!Directory.Exists(assets)) return;
        foreach (var folder in Directory.EnumerateDirectories(assets))
        {
            var name = Path.GetFileName(folder);
            if (!(name.StartsWith(".stage-", StringComparison.Ordinal) || name.StartsWith(".work-", StringComparison.Ordinal)) ||
                !Guid.TryParseExact(name[(name.IndexOf('-') + 1)..], "N", out _)) continue;
            try { Directory.Delete(folder, recursive: true); GameTrace.Write("interrupted-import=cleaned"); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { GameTrace.Write("interrupted-import-cleanup=" + error.Message); }
        }
    }
    internal static PersistentAssetStore Store => new(Path.Combine(Root, "Assets"), Saves,
        (xap, work, progress, cancellation) => IosGamePreparation.Prepare(xap, work,
            NSBundle.MainBundle.BundlePath, new NativeAudioConverter(File.ReadAllText(
                Path.Combine(NSBundle.MainBundle.BundlePath, "converter-identity.txt")).Trim()),
            progress, cancellation), new PortableArtImporter());

    private static string ImportFlag => Path.Combine(Root, "open-importer");
    internal static bool ImportRequested => File.Exists(ImportFlag);
    internal static void RequestImport(bool requested)
    {
        if (requested) { Directory.CreateDirectory(Root); File.WriteAllText(ImportFlag, "import"); }
        else File.Delete(ImportFlag);
    }
}

internal static class GameTrace
{
    private static readonly object Gate = new();
    internal static string Path => System.IO.Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.MyDocuments), "ios-game-trace.txt");

    internal static void Write(string message)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                if (File.Exists(Path) && new FileInfo(Path).Length > 4 * 1024 * 1024)
                    File.Move(Path, Path + ".previous.txt", overwrite: true);
                File.AppendAllText(Path, $"{DateTime.UtcNow:O} {message}\n");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    internal sealed class Writer : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        public override void WriteLine(string? value) { if (value is not null) GameTrace.Write(value); }
        public override void Write(string? value) { if (!string.IsNullOrEmpty(value)) GameTrace.Write(value); }
    }
}
