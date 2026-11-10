using System.Security.Cryptography;
using System.Text.Json;

namespace Port.Prepare;

/// <summary>Verifies prepared game files at explicitly supplied locations.</summary>
public static class PreparedGameVerifier
{
    public static void Verify(string dllPath, string contentRoot)
    {
        var dll = Path.GetFullPath(dllPath);
        var content = Path.GetFullPath(contentRoot).TrimEnd(Path.DirectorySeparatorChar);
        using var patch = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(Path.GetDirectoryName(dll)!, "patch-manifest.json")));
        if (patch.RootElement.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidDataException("Unsupported patch manifest.");
        var sourceDll = patch.RootElement.GetProperty("sourceSha256").GetString();
        var preparedDll = patch.RootElement.GetProperty("preparedSha256").GetString();
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(content, "content-manifest.json")));
        if (manifest.RootElement.GetProperty("SchemaVersion").GetInt32() != 1)
            throw new InvalidDataException("Unsupported content manifest.");
        var sourceXap = manifest.RootElement.GetProperty("SourceXapSha256").GetString();
        if (sourceXap is null || sourceDll is null ||
            !PreparationEngine.IsSupportedIdentity(sourceXap, sourceDll) ||
            preparedDll != Hash(dll))
            throw new InvalidDataException("Prepared DLL and content do not match a supported XAP.");
        var outputs = manifest.RootElement.GetProperty("Outputs").EnumerateArray().ToArray();
        if (outputs.Length < 100)
            throw new InvalidDataException("Prepared content manifest is incomplete.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var output in outputs)
        {
            var relative = output.GetProperty("Path").GetString()
                ?? throw new InvalidDataException("Content manifest has an empty output path.");
            if (!IsSafeRelative(relative) || !seen.Add(relative))
                throw new InvalidDataException($"Invalid content path: {relative}");
            var path = Path.GetFullPath(Path.Combine(content, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(content + Path.DirectorySeparatorChar, PathComparison) ||
                !File.Exists(path) || new FileInfo(path).Length != output.GetProperty("Bytes").GetInt64() ||
                Hash(path) != output.GetProperty("Sha256").GetString())
                throw new InvalidDataException($"Prepared content differs from its manifest: {relative}");
        }
        if (!seen.Contains("Content/Arial.xnb"))
            throw new InvalidDataException("Prepared content is missing Arial.xnb.");
    }

    private static bool IsSafeRelative(string path) => path.Length != 0 &&
        !Path.IsPathRooted(path) && !path.Contains('\\') &&
        path.Split('/').All(part => part.Length != 0 && part is not ("." or "..") &&
            !part.Contains(':'));

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
