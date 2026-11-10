using System.Text.Json;
using Port.Host;
using Port.Prepare;

internal static class StandaloneImportChecks
{
    public static void Run(string programPath, string xap, string normal, string viking, string output)
    {
        var program = Path.GetFullPath(programPath);
        var root = Path.GetFullPath(output);
        var inputs = Path.Combine(root, "inputs");
        Directory.CreateDirectory(inputs);
        foreach (var (source, name) in new[] { (xap, "phone.xap"), (normal, "i-maed-360"), (viking, "time-viking-360") })
            File.Copy(source, Path.Combine(inputs, name));
        var converterHash = File.ReadAllText(Path.Combine(program, "config/macos-converter.sha256"))
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        var tools = new GamePreparationTools(Path.Combine(program, "tools/inspect_assembly"),
            Path.Combine(program, "tools/abi/abi_check"), program,
            Path.Combine(program, "config/supported-versions.json"), Path.Combine(program, "FNA.dll"),
            Path.Combine(program, "Port.Bridge.dll"), Path.Combine(program, "Port.Hooks.dll"),
            Path.Combine(program, "tools/ffmpeg"), converterHash);
        var data = Path.Combine(root, "app-data");
        var importer = new ExternalOptionalArtImporter(Path.Combine(program, "tools/Port.AssetImporter"),
            Path.Combine(program, "tools/decode_360_xnb"), Path.Combine(program, "FNA.dll"),
            Path.Combine(data, "art-work"));
        var store = new PersistentAssetStore(Path.Combine(data, "assets"), Path.Combine(data, "saves"),
            (source, work, progress, token) => GamePreparationPipeline.Prepare(source, work, tools,
                progress, cancellationToken: token), importer);
        var active = store.Import(new AssetImportSelection(Path.Combine(inputs, "phone.xap"),
            new ArtImport("i-maed-360", Path.Combine(inputs, "i-maed-360")),
            new ArtImport("time-viking-360", Path.Combine(inputs, "time-viking-360"))), Console.WriteLine);
        foreach (var kind in new[] { "i-maed-360", "time-viking-360" })
        {
            var folder = Path.Combine(active.ArtRoot, kind);
            using var pointer = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "current.json")));
            var generation = pointer.RootElement.GetProperty("generation").GetString()!;
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, generation, "manifest.json")));
            var imported = manifest.RootElement.GetProperty("elements").EnumerateArray()
                .Count(row => row.GetProperty("selection").GetString() == kind);
            if (imported == 0) throw new Exception($"No artwork imported for {kind}.");
            Console.WriteLine($"STANDALONE_ART_OK {kind} imported={imported}");
        }
        Directory.Move(inputs, Path.Combine(root, "offline-inputs"));
        if (store.TryOpen() is null) throw new Exception("Restart depended on the original package paths.");
        Console.WriteLine("STANDALONE_IMPORT_OK bundled-tools/both-360-sources/source-free-restart");
    }
}
