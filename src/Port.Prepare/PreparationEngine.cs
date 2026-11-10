using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Port.Prepare;

/// <summary>Verifies and stages exact supported XAP inputs without changing the original assembly.</summary>
public static class PreparationEngine
{
    public const int FormatVersion = 1;
    public const string ToolVersion = "0.3.0";

    public static PackageIdentity ValidateXap(string xapPath, CancellationToken cancellationToken = default)
    {
        using var package = OpenVerified(xapPath, cancellationToken);
        return package.Identity;
    }

    /// <summary>Checks manifest identities without requiring the original package at launch.</summary>
    public static bool IsSupportedIdentity(string xapSha256, string dllSha256)
    {
        var supported = FindSupported(xapSha256);
        return supported is not null &&
            string.Equals(supported.DllSha256, dllSha256, StringComparison.Ordinal);
    }

    public static PreparationResult Prepare(
        PreparationRequest request,
        Action<PreparationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var final = Path.GetFullPath(request.OutputDirectory);
        var parent = Path.GetDirectoryName(final)
            ?? throw new PreparationException("Output must be a new directory, not a drive root.");
        var leaf = Path.GetFileName(final);
        if (leaf.Length == 0 || Path.Exists(final))
            throw new PreparationException($"Output already exists or is a root path: {final}");

        // Verification holds the source open read-only with FileShare.Read until extraction ends.
        // No output directory, including staging, is created before the package and DLL pass.
        using var package = OpenVerified(request.XapPath, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Invoke(new PreparationProgress("validated", 0, package.Files.Count, null));

        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".{leaf}.stage-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(staging);
            var extractedRoot = Path.Combine(staging, "original-xap");
            Directory.CreateDirectory(extractedRoot);
            var outputFiles = new List<OutputFile>(package.Files.Count);
            foreach (var file in package.Files.OrderBy(f => f.Path, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = Path.GetFullPath(Path.Combine(extractedRoot,
                    file.Path.Replace('/', Path.DirectorySeparatorChar)));
                if (!IsChildOf(extractedRoot, destination))
                    throw new PreparationException($"Unsafe extracted path: {file.Path}");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using var input = file.Entry.Open();
                using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[1024 * 1024];
                long written = 0;
                int count;
                while ((count = input.Read(buffer)) != 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    output.Write(buffer, 0, count);
                    sha.AppendData(buffer, 0, count);
                    written += count;
                }
                output.Flush(flushToDisk: true);
                if (written != file.Entry.Length)
                    throw new PreparationException($"Length changed while extracting {file.Path}");
                var hash = Hex(sha.GetHashAndReset());
                outputFiles.Add(new OutputFile($"original-xap/{file.Path}", written, hash));
                progress?.Invoke(new PreparationProgress("extracting", outputFiles.Count, package.Files.Count, file.Path));
            }

            foreach (var file in outputFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = Path.Combine(staging, file.Path.Replace('/', Path.DirectorySeparatorChar));
                using var verifyStream = File.OpenRead(path);
                if (verifyStream.Length != file.Bytes || Hash(verifyStream) != file.Sha256)
                    throw new PreparationException($"Staged output failed verification: {file.Path}");
            }
            var originalDll = outputFiles.Single(f => f.Path == "original-xap/ZombiesWP7.dll");
            if (originalDll.Sha256 != package.Identity.DllSha256)
                throw new PreparationException("Staged original DLL hash differs from the supported input.");

            var manifest = new ProvenanceManifest(
                FormatVersion, "original-extracted", package.Identity,
                new ToolIdentity("Port.Prepare", ToolVersion, "net10.0"), outputFiles);
            var json = JsonSerializer.Serialize(manifest, JsonOptions) + "\n";
            var manifestBytes = Encoding.UTF8.GetBytes(json);
            var manifestPath = Path.Combine(staging, "provenance.json");
            using (var output = new FileStream(manifestPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(manifestBytes);
                output.Flush(flushToDisk: true);
            }
            progress?.Invoke(new PreparationProgress("verified", outputFiles.Count, package.Files.Count, null));
            cancellationToken.ThrowIfCancellationRequested();
            if (Path.Exists(final)) throw new PreparationException($"Output appeared during preparation: {final}");
            var result = new PreparationResult(final, package.Identity, outputFiles.Count,
                Hex(SHA256.HashData(manifestBytes)));
            Directory.Move(staging, final);
            return result;
        }
        catch
        {
            DeleteOwnedStage(staging, parent, leaf);
            throw;
        }
    }

    private static VerifiedPackage OpenVerified(string xapPath, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(xapPath);
        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var xapBytes = stream.Length;
            var xapHash = Hash(stream);
            var supported = FindSupported(xapHash);
            if (supported is null)
                throw new PreparationException($"Unsupported XAP SHA-256 {xapHash}. Supply the exact supported version.");
            cancellationToken.ThrowIfCancellationRequested();
            stream.Position = 0;
            var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            try
            {
                var files = CheckEntries(archive, supported);
                var dll = files.SingleOrDefault(f => f.Path == "ZombiesWP7.dll")
                    ?? throw new PreparationException("Supported XAP lacks ZombiesWP7.dll.");
                using (var source = dll.Entry.Open())
                {
                    var actualDllHash = Hash(source);
                    if (actualDllHash != supported.DllSha256 || dll.Entry.Length != supported.DllBytes)
                        throw new PreparationException($"Original DLL hash/size mismatch: {actualDllHash}.");
                }
                CheckManifests(archive);
                cancellationToken.ThrowIfCancellationRequested();
                var identity = new PackageIdentity(supported.Id, xapHash, xapBytes,
                    supported.DllSha256, supported.DllBytes, archive.Entries.Count,
                    archive.Entries.Sum(e => e.Length));
                return new VerifiedPackage(stream, archive, identity, files);
            }
            catch { archive.Dispose(); throw; }
        }
        catch { stream.Dispose(); throw; }
    }

    private static List<ValidatedFile> CheckEntries(ZipArchive archive, SupportedVersion supported)
    {
        if (archive.Entries.Count != supported.XapEntries ||
            archive.Entries.Sum(e => e.Length) != supported.XapUncompressedBytes)
            throw new PreparationException("Supported XAP has unexpected entry count or uncompressed size.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<ValidatedFile>();
        foreach (var entry in archive.Entries)
        {
            var raw = entry.FullName;
            var directory = raw.EndsWith("/", StringComparison.Ordinal);
            var name = directory ? raw[..^1] : raw;
            if (name.Length == 0 || name.StartsWith("/", StringComparison.Ordinal) || name.Contains('\\') ||
                name.Split('/').Any(part => !SafeSegment(part)) ||
                ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 ||
                (entry.ExternalAttributes & 0x400) != 0)
                throw new PreparationException($"Unsafe XAP entry path or link: {raw}");
            if (!seen.Add(name)) throw new PreparationException($"Duplicate XAP entry: {raw}");
            if (directory)
            {
                if (entry.Length != 0) throw new PreparationException($"Directory entry has data: {raw}");
            }
            else files.Add(new ValidatedFile(name, entry));
        }
        return files;
    }

    private static bool SafeSegment(string part)
    {
        if (part.Length == 0 || part is "." or ".." || part.EndsWith(' ') || part.EndsWith('.') ||
            part.Any(c => c < 32 || "<>:\"|?*".Contains(c))) return false;
        var stem = part.Split('.')[0];
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)) return false;
        if (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
            stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && stem[3] is >= '1' and <= '9') return false;
        return true;
    }

    private static void CheckManifests(ZipArchive archive)
    {
        using var app = archive.GetEntry("AppManifest.xaml")?.Open()
            ?? throw new PreparationException("AppManifest.xaml is missing.");
        var deployment = ReadXml(app).Root;
        if (deployment?.Attribute("EntryPointAssembly")?.Value != "ZombiesWP7" ||
            deployment.Attribute("EntryPointType")?.Value != "ZombiesWP7.Game1" ||
            !deployment.Descendants().Any(e => e.Name.LocalName == "AssemblyPart" &&
                e.Attribute("Source")?.Value == "ZombiesWP7.dll"))
            throw new PreparationException("XAP app manifest has an unexpected entry point.");
        using var phone = archive.GetEntry("WMAppManifest.xml")?.Open()
            ?? throw new PreparationException("WMAppManifest.xml is missing.");
        var phoneDoc = ReadXml(phone);
        var game = phoneDoc.Descendants().SingleOrDefault(e => e.Name.LocalName == "App");
        if (phoneDoc.Root?.Attribute("AppPlatformVersion")?.Value != "7.1" ||
            game?.Attribute("Version")?.Value != "1.0.0.0" ||
            game.Attribute("RuntimeType")?.Value != "XNA")
            throw new PreparationException("Phone manifest has an unexpected platform or game version.");
    }

    private static XDocument ReadXml(Stream stream)
    {
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 1_000_000
        });
        return XDocument.Load(reader);
    }

    private static SupportedVersion? FindSupported(string xapHash)
    {
        using var resource = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Port.Prepare.supported-versions.json")
            ?? throw new PreparationException("Embedded supported-version manifest is missing.");
        using var document = JsonDocument.Parse(resource);
        foreach (var item in document.RootElement.GetProperty("versions").EnumerateArray())
        {
            if (item.GetProperty("xapSha256").GetString() == xapHash)
                return new SupportedVersion(item.GetProperty("id").GetString()!,
                    item.GetProperty("dllSha256").GetString()!, item.GetProperty("dllBytes").GetInt64(),
                    item.GetProperty("xapEntries").GetInt32(), item.GetProperty("xapUncompressedBytes").GetInt64());
        }
        return null;
    }

    private static void DeleteOwnedStage(string staging, string parent, string leaf)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(staging)) != Path.GetFullPath(parent) ||
            !Path.GetFileName(staging).StartsWith($".{leaf}.stage-", StringComparison.Ordinal))
            throw new PreparationException("Refusing to clean a staging path outside the output parent.");
        if (!Directory.Exists(staging)) return;
        if ((File.GetAttributes(staging) & FileAttributes.ReparsePoint) != 0)
            throw new PreparationException("Refusing to recursively clean a linked staging directory.");
        Directory.Delete(staging, recursive: true);
    }

    private static bool IsChildOf(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != "." && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !Path.IsPathRooted(relative);
    }

    private static string Hash(Stream stream) => Hex(SHA256.HashData(stream));
    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private sealed record SupportedVersion(string Id, string DllSha256, long DllBytes,
        int XapEntries, long XapUncompressedBytes);
    private sealed record ValidatedFile(string Path, ZipArchiveEntry Entry);
    private sealed record OutputFile(string Path, long Bytes, string Sha256);
    private sealed record ToolIdentity(string Name, string Version, string TargetFramework);
    private sealed record ProvenanceManifest(int SchemaVersion, string Phase, PackageIdentity Input,
        ToolIdentity Tool, List<OutputFile> Files);

    private sealed class VerifiedPackage(FileStream stream, ZipArchive archive, PackageIdentity identity,
        List<ValidatedFile> files) : IDisposable
    {
        public PackageIdentity Identity { get; } = identity;
        public List<ValidatedFile> Files { get; } = files;
        public void Dispose() { archive.Dispose(); stream.Dispose(); }
    }
}
