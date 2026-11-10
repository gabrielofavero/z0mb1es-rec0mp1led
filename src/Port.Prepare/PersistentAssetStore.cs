using System.Security.Cryptography;
using System.Text.Json;

namespace Port.Prepare;

/// <summary>Host-provided optional source support. The phone art remains complete without it.</summary>
public interface IOptionalArtImporter
{
    (string Version, long SourceBytes) Preview(string kind, string path);
    void Prepare(string kind, string path, string phoneRoot, string artRoot,
        Action<string>? progress, CancellationToken cancellationToken);
    void Compose(string phoneRoot, string artRoot, Action<string>? progress,
        CancellationToken cancellationToken);
}

public sealed record ArtImport(string Kind, string Path);
public sealed record AssetImportSelection(string? PhoneXap, ArtImport? Normal, ArtImport? Viking);
public sealed record AssetImportPreview(string PhoneVersion, long SourceBytes,
    long EstimatedPeakBytes, long AvailableBytes, string? NormalVersion, string? VikingVersion);
public sealed record ActiveAssetSet(string Root, string DllPath, string ContentRoot,
    string ArtRoot, string PhoneVersion);

/// <summary>Atomic app-data import. Paths in the on-disk manifest are relative; no source paths persist.</summary>
public sealed class PersistentAssetStore(string assetDirectory, string saveDirectory,
    Func<string, string, Action<string>?, CancellationToken, GamePreparationResult> prepare,
    IOptionalArtImporter? optionalArt = null)
{
    private const int Format = 1;
    private readonly string root = Path.GetFullPath(assetDirectory);
    private readonly string saves = Path.GetFullPath(saveDirectory);
    private string Active => Path.Combine(root, "current");
    private string Previous => Path.Combine(root, "previous");
    private sealed record Entry(string Path, long Bytes, string Sha256);
    private sealed record Manifest(int Format, string PhoneVersion, string PhoneXapSha256,
        string? NormalKind, string? VikingKind, List<Entry> Files);

    public AssetImportPreview Preview(AssetImportSelection selection)
    {
        CheckPaths();
        var current = TryOpen();
        if (selection.PhoneXap is null && current is null)
            throw new InvalidDataException("A supported phone XAP is required for the first import or repair.");
        var phone = selection.PhoneXap is null ? null : PreparationEngine.ValidateXap(selection.PhoneXap);
        var manifest = current is null ? null : ReadManifest(Active);
        if (selection.Normal is { } normal && normal.Kind is not ("i-maed-steam" or "i-maed-360"))
            throw new ArgumentException("Unsupported I MAED source kind.");
        if (selection.Viking is { Kind: not "time-viking-360" })
            throw new ArgumentException("Unsupported Time Viking source kind.");
        if ((selection.Normal is not null || selection.Viking is not null) && optionalArt is null)
            throw new InvalidOperationException("This host has no optional art importer.");
        var normalInfo = selection.Normal is null ? ((string Version, long SourceBytes)?)null :
            optionalArt!.Preview(selection.Normal.Kind, selection.Normal.Path);
        var vikingInfo = selection.Viking is null ? ((string Version, long SourceBytes)?)null :
            optionalArt!.Preview(selection.Viking.Kind, selection.Viking.Path);
        var previousBytes = manifest?.Files.Sum(file => file.Bytes) ?? 0;
        var sourceBytes = (phone?.XapBytes ?? 0) + (normalInfo?.SourceBytes ?? 0) +
            (vikingInfo?.SourceBytes ?? 0);
        // Includes old active set, new set, extraction workspace and converter scratch.
        var estimate = checked(previousBytes * 2 +
            (phone?.UncompressedBytes ?? previousBytes) * 3 + sourceBytes * 2);
        var drive = new DriveInfo(Path.GetPathRoot(root)!);
        return new AssetImportPreview(phone?.SupportedVersion ?? manifest!.PhoneVersion,
            sourceBytes, estimate, drive.AvailableFreeSpace,
            normalInfo?.Version, vikingInfo?.Version);
    }

    public ActiveAssetSet? TryOpen()
    {
        CheckPaths();
        if (!Directory.Exists(Active) && Directory.Exists(Previous))
            Directory.Move(Previous, Active);
        if (!Directory.Exists(Active)) return null;
        try { return Verify(Active); }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException or
            UnauthorizedAccessException or KeyNotFoundException)
        {
            Console.Error.WriteLine($"ASSET_REPAIR_REQUIRED {error.Message}");
            if (Directory.Exists(Previous))
            {
                try
                {
                    Verify(Previous);
                    var damaged = Path.Combine(root, ".damaged-" + Guid.NewGuid().ToString("N"));
                    Directory.Move(Active, damaged);
                    try { Directory.Move(Previous, Active); }
                    catch { Directory.Move(damaged, Active); throw; }
                    DeleteTree(damaged);
                    return Verify(Active);
                }
                catch (Exception recoveryError) when (recoveryError is IOException or
                    InvalidDataException or JsonException or UnauthorizedAccessException)
                {
                    Console.Error.WriteLine($"ASSET_BACKUP_UNAVAILABLE {recoveryError.Message}");
                }
            }
            return null;
        }
    }

    public ActiveAssetSet Import(AssetImportSelection selection, Action<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var preview = Preview(selection);
        if (preview.EstimatedPeakBytes > preview.AvailableBytes)
            throw new IOException("Not enough free space for a safe import.");
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(root);
        var stage = Path.Combine(root, ".stage-" + Guid.NewGuid().ToString("N"));
        var work = Path.Combine(root, ".work-" + Guid.NewGuid().ToString("N"));
        var committed = false;
        try
        {
            Directory.CreateDirectory(stage);
            string phoneVersion;
            string xapSha;
            if (selection.PhoneXap is { } xap)
            {
                progress?.Invoke("prepare-phone");
                cancellationToken.ThrowIfCancellationRequested();
                var result = prepare(xap, work, progress, cancellationToken);
                CopyTree(result.ExtractedRoot, Path.Combine(stage, "phone-original"), cancellationToken);
                CopyTree(result.PreparedDllDirectory, Path.Combine(stage, "game"), cancellationToken);
                CopyTree(result.ContentDirectory, Path.Combine(stage, "content"), cancellationToken);
                phoneVersion = result.Identity.SupportedVersion;
                xapSha = result.Identity.XapSha256;
            }
            else
            {
                progress?.Invoke("reuse-phone");
                var old = ReadManifest(Active);
                foreach (var folder in new[] { "phone-original", "game", "content" })
                    CopyTree(Path.Combine(Active, folder), Path.Combine(stage, folder), cancellationToken);
                phoneVersion = old.PhoneVersion;
                xapSha = old.PhoneXapSha256;
            }
            var priorArt = Path.Combine(Active, "optional-art");
            var art = Path.Combine(stage, "optional-art");
            if (TryOpen() is not null && Directory.Exists(priorArt))
                CopyTree(priorArt, art, cancellationToken);
            if (selection.Normal is { } normal)
            {
                foreach (var kind in new[] { "i-maed-steam", "i-maed-360", "endless-360" })
                    DeleteTree(Path.Combine(art, kind));
                Directory.CreateDirectory(art);
                File.WriteAllText(Path.Combine(art, "normal-source.json"),
                    JsonSerializer.Serialize(new { kind = normal.Kind }) + "\n");
                optionalArt!.Prepare(normal.Kind, normal.Path, Path.Combine(stage, "phone-original"),
                    art, progress, cancellationToken);
            }
            if (selection.Viking is { } viking)
            {
                DeleteTree(Path.Combine(art, "time-viking-360"));
                DeleteTree(Path.Combine(art, "endless-360"));
                optionalArt!.Prepare(viking.Kind, viking.Path, Path.Combine(stage, "phone-original"),
                    art, progress, cancellationToken);
            }
            if (optionalArt is not null && Directory.Exists(art))
                optionalArt!.Compose(Path.Combine(stage, "phone-original"), art, progress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Invoke("verify");
            PreparedGameVerifier.Verify(Path.Combine(stage, "game", "ZombiesWP7.dll"),
                Path.Combine(stage, "content"));
            ValidateNoLinks(stage);
            var files = Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories)
                .Select(file => new Entry(Path.GetRelativePath(stage, file).Replace('\\', '/'),
                    new FileInfo(file).Length, Hash(file)))
                .OrderBy(file => file.Path, StringComparer.Ordinal).ToList();
            File.WriteAllText(Path.Combine(stage, "asset-manifest.json"),
                JsonSerializer.Serialize(new Manifest(Format, phoneVersion, xapSha,
                    ReadNormalKind(art), Directory.Exists(Path.Combine(art, "time-viking-360"))
                        ? "time-viking-360" : null, files)) + "\n");
            Verify(stage);
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Invoke("activate");
            if (Directory.Exists(Previous)) DeleteTree(Previous);
            if (Directory.Exists(Active)) Directory.Move(Active, Previous);
            try { Directory.Move(stage, Active); }
            catch
            {
                if (!Directory.Exists(Active) && Directory.Exists(Previous))
                    Directory.Move(Previous, Active);
                throw;
            }
            ActiveAssetSet active;
            try { active = Verify(Active); }
            catch
            {
                DeleteTree(Active);
                if (Directory.Exists(Previous)) Directory.Move(Previous, Active);
                throw;
            }
            committed = true;
            BestEffortDelete(Previous);
            return active;
        }
        finally
        {
            BestEffortDelete(stage);
            BestEffortDelete(work);
            if (!committed && !Directory.Exists(Active) && Directory.Exists(Previous))
                Directory.Move(Previous, Active);
        }
    }

    private ActiveAssetSet Verify(string directory)
    {
        ValidateNoLinks(directory);
        var manifest = ReadManifest(directory);
        if (manifest.Format != Format || manifest.Files is null || manifest.Files.Count == 0 ||
            manifest.Files.Select(file => file.Path).Distinct(StringComparer.Ordinal).Count() != manifest.Files.Count)
            throw new InvalidDataException("Invalid asset manifest.");
        var listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in manifest.Files)
        {
            if (!Safe(entry.Path) || !listed.Add(entry.Path))
                throw new InvalidDataException("Unsafe or duplicate asset path.");
            var path = Path.Combine(directory, entry.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path) || new FileInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                new FileInfo(path).Length != entry.Bytes || Hash(path) != entry.Sha256)
                throw new InvalidDataException($"Asset hash differs: {entry.Path}");
        }
        if (Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Any(path => Path.GetRelativePath(directory, path) != "asset-manifest.json" &&
                !listed.Contains(Path.GetRelativePath(directory, path).Replace('\\', '/'))))
            throw new InvalidDataException("Unlisted asset file.");
        var dll = Path.Combine(directory, "game", "ZombiesWP7.dll");
        var content = Path.Combine(directory, "content");
        PreparedGameVerifier.Verify(dll, content);
        return new ActiveAssetSet(directory, dll, content,
            Path.Combine(directory, "optional-art"), manifest.PhoneVersion);
    }

    private static Manifest ReadManifest(string directory) =>
        JsonSerializer.Deserialize<Manifest>(File.ReadAllText(Path.Combine(directory, "asset-manifest.json")))
        ?? throw new InvalidDataException("Empty asset manifest.");
    private void CheckPaths()
    {
        var relation = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (root.Equals(saves, relation) || root.StartsWith(saves + Path.DirectorySeparatorChar, relation) ||
            saves.StartsWith(root + Path.DirectorySeparatorChar, relation))
            throw new ArgumentException("Asset and save locations must be separate.");
    }
    private static bool Safe(string path) => path.Length != 0 && !Path.IsPathRooted(path) &&
        path.Split('/').All(part => part.Length != 0 && part is not ("." or "..") &&
            !part.Contains('\\') && !part.Contains(':'));
    private static string? ReadNormalKind(string art)
    {
        var file = Path.Combine(art, "normal-source.json");
        if (!File.Exists(file)) return null;
        using var document = JsonDocument.Parse(File.ReadAllText(file));
        return document.RootElement.GetProperty("kind").GetString();
    }
    private static string Hash(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
    private static void CopyTree(string source, string target, CancellationToken token)
    {
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }
    private static void DeleteTree(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }
    private static void BestEffortDelete(string path)
    {
        try { DeleteTree(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { Console.Error.WriteLine($"ASSET_CLEANUP_DEFERRED {path}: {error.Message}"); }
    }
    private static void ValidateNoLinks(string directory)
    {
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.TryPop(out var current))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                var attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    throw new InvalidDataException($"Linked asset path is forbidden: {entry}");
                if (attributes.HasFlag(FileAttributes.Directory)) pending.Push(entry);
            }
        }
    }
}
