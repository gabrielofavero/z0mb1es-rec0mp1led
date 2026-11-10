using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Port.Prepare;

/// <summary>Validates the exact Steam and 360 source fingerprints before staging.</summary>
public static class PinnedOptionalArtSource
{
    public static (string Version, long SourceBytes) Preview(string kind, string source)
    {
        if (kind == "i-maed-steam")
        {
            using var pin = Embedded("supported-steam-versions.json");
            var root = Path.GetFullPath(source);
            var executable = pin.RootElement.GetProperty("executable");
            long bytes = Check(Path.Combine(root, executable.GetProperty("path").GetString()!),
                executable, "Steam I MAED 1.0.0.0");
            foreach (var row in pin.RootElement.GetProperty("requiredTextures").EnumerateArray())
            {
                var relative = row.GetProperty("path").GetString()!;
                if (!Safe(relative)) throw new InvalidDataException("Unsafe Steam art path.");
                bytes += Check(Path.Combine(root, "Content", relative.Replace('/', Path.DirectorySeparatorChar)),
                    row, "Steam I MAED 1.0.0.0");
            }
            return ("I MAED Steam 1.0.0.0; I MAED and Endless artwork", bytes);
        }
        if (kind is "i-maed-360" or "time-viking-360")
        {
            using var pin = Embedded("supported-360-versions.json");
            var id = kind == "i-maed-360" ? "zombies-360" : "viking-360";
            var package = pin.RootElement.GetProperty("titles").EnumerateArray()
                .Single(item => item.GetProperty("sourceId").GetString() == id)
                .GetProperty("package");
            var label = kind == "i-maed-360" ? "I MAED 360 LIVE" : "Time Viking 360 LIVE";
            var bytes = Check(source, package, label);
            return (kind == "i-maed-360" ? "I MAED 360 pinned LIVE; 121 mapped artworks" :
                "Time Viking 360 pinned LIVE; 270 mapped artworks", bytes);
        }
        throw new ArgumentException("Unsupported optional art source.", nameof(kind));
    }

    private static JsonDocument Embedded(string name)
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Port.Prepare." + name)
            ?? throw new InvalidDataException($"Missing embedded art pin: {name}");
        return JsonDocument.Parse(stream);
    }
    private static bool Safe(string relative) => relative.Length != 0 && !Path.IsPathRooted(relative) &&
        relative.Split('/').All(part => part.Length != 0 && part is not ("." or "..") &&
            !part.Contains('\\') && !part.Contains(':'));
    private static long Check(string path, JsonElement pin, string label)
    {
        var file = new FileInfo(Path.GetFullPath(path));
        if (!file.Exists || file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException($"{label}: required source is missing or linked: {file.Name}");
        if (file.Length != pin.GetProperty("bytes").GetInt64() || Hash(file.FullName) != pin.GetProperty("sha256").GetString())
            throw new InvalidDataException($"{label}: unsupported or damaged source: {file.Name}");
        return file.Length;
    }
    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
