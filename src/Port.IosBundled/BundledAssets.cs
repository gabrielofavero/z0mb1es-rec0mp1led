using System.Security.Cryptography;
using System.Text.Json;
using Port.Prepare;

namespace Port.IosBundled;

// Only the separate personal build includes these files. The importer project
// and its content-free payload audit never opt in to this path.
internal static class BundledAssets
{
    private sealed record Entry(string Path, long Bytes, string Sha256);
    private sealed record Receipt(int Format, string PhoneVersion, List<Entry> Files);

    internal static void Create(ActiveAssetSet active, string output)
    {
        if (Path.Exists(output)) throw new IOException("Personal payload output must be new.");
        PreparedGameVerifier.Verify(active.DllPath, active.ContentRoot);
        Directory.CreateDirectory(output);
        foreach (var folder in new[] { "game", "content", "optional-art" })
        {
            var source = Path.Combine(active.Root, folder);
            if (!Directory.Exists(source)) continue;
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(output, folder, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
        }
        var entries = Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal).Select(path => new Entry(Path.GetRelativePath(output, path).Replace('\\', '/'),
                new FileInfo(path).Length, Hash(path))).ToList();
        File.WriteAllText(Path.Combine(output, "bundled-manifest.json"), JsonSerializer.Serialize(new Receipt(1, active.PhoneVersion, entries)));
        Open(output);
    }

    internal static ActiveAssetSet Open(string root, Action<string>? log = null)
    {
        log?.Invoke("bundled-assets=verifying");
        var receipt = JsonSerializer.Deserialize<Receipt>(File.ReadAllBytes(Path.Combine(root, "bundled-manifest.json")))
            ?? throw new InvalidDataException("Missing personal payload receipt.");
        if (receipt.Format != 1 || receipt.Files.Count == 0) throw new InvalidDataException("Invalid personal payload receipt.");
        var listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in receipt.Files)
        {
            if (!Safe(entry.Path) || !listed.Add(entry.Path)) throw new InvalidDataException("Unsafe or duplicate personal payload path.");
            var path = Path.Combine(root, entry.Path);
            if (!File.Exists(path) || new FileInfo(path).Length != entry.Bytes || Hash(path) != entry.Sha256)
                throw new InvalidDataException("Bundled asset differs: " + entry.Path);
        }
        if (Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Any(path =>
                Path.GetRelativePath(root, path).Replace('\\', '/') is var name && name != "bundled-manifest.json" && !listed.Contains(name)))
            throw new InvalidDataException("Unexpected personal payload file.");
        var dll = Path.Combine(root, "game/ZombiesWP7.dll");
        var content = Path.Combine(root, "content");
        PreparedGameVerifier.Verify(dll, content);
        log?.Invoke("bundled-assets=verified files=" + listed.Count);
        return new ActiveAssetSet(root, dll, content, Path.Combine(root, "optional-art"), receipt.PhoneVersion);
    }

    private static bool Safe(string path) => path.Length > 0 && !Path.IsPathRooted(path) &&
        path.Split('/').All(p => p.Length > 0 && p is not ("." or "..") && !p.Contains('\\') && !p.Contains(':'));
    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
