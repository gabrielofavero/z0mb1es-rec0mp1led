using System.Security.Cryptography;
using System.Text.Json;
using Android.Content.Res;

namespace Port.AndroidGame;

internal static class BundledAssets
{
    internal sealed record Entry(string Path, long Bytes, string Sha256);
    internal sealed record Receipt(int Format, string FrameworkSha256, string BridgeSha256, string HooksSha256, List<Entry> Files);

    internal static string Open(AssetManager assets, string dataRoot)
    {
        using var manifestStream = assets.Open("preinstalled/bundled-manifest.json");
        using var memory = new MemoryStream();
        manifestStream.CopyTo(memory);
        var bytes = memory.ToArray();
        var receipt = JsonSerializer.Deserialize<Receipt>(bytes) ?? throw new InvalidDataException("Missing bundled manifest.");
        if (receipt.Format != 1 || receipt.Files.Count < 100) throw new InvalidDataException("Incomplete bundled payload.");
        var root = Path.Combine(dataRoot, "bundled", Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Validate every name before touching the filesystem. Saves live outside
        // this versioned immutable payload and survive replacement APK installs.
        foreach (var entry in receipt.Files)
            if (!Safe(entry.Path) || !names.Add(entry.Path) || entry.Bytes < 0)
                throw new InvalidDataException("Invalid bundled path: " + entry.Path);
        if (Directory.Exists(root))
        {
            Verify(root, receipt);
            return root;
        }
        var temporary = root + "." + Guid.NewGuid().ToString("N") + ".tmp";
        Directory.CreateDirectory(temporary);
        try
        {
            foreach (var entry in receipt.Files)
            {
                var target = Path.Combine(temporary, entry.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var input = assets.Open("preinstalled/" + entry.Path);
                using var output = File.Create(target);
                input.CopyTo(output);
            }
            Verify(temporary, receipt);
            File.WriteAllBytes(Path.Combine(temporary, "bundled-manifest.json"), bytes);
            Directory.Move(temporary, root);
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
        return root;
    }

    private static bool Safe(string path) => path.Length > 0 && !Path.IsPathRooted(path) && !path.Contains('\\') &&
        path.Split('/').All(part => part.Length > 0 && part is not ("." or "..") && !part.Contains(':'));

    private static void Verify(string root, Receipt receipt)
    {
        foreach (var entry in receipt.Files)
        {
            var file = Path.Combine(root, entry.Path);
            using var input = File.OpenRead(file);
            if (input.Length != entry.Bytes || Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant() != entry.Sha256)
                throw new InvalidDataException("Bundled asset differs: " + entry.Path);
        }
    }
}
