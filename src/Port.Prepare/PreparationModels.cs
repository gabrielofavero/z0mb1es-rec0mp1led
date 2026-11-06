namespace Port.Prepare;

public sealed record PreparationRequest(string XapPath, string OutputDirectory);

public sealed record PackageIdentity(
    string SupportedVersion,
    string XapSha256,
    long XapBytes,
    string DllSha256,
    long DllBytes,
    int ArchiveEntries,
    long UncompressedBytes);

public sealed record PreparationProgress(string Stage, int CompletedFiles, int TotalFiles, string? Path);

public sealed record PreparationResult(
    string OutputDirectory,
    PackageIdentity Input,
    int ExtractedFiles,
    string ProvenanceSha256);

public sealed class PreparationException(string message) : Exception(message);
