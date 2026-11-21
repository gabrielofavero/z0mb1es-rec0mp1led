namespace Port.Host;

/// <summary>Locations supplied by an OS host; program files and writable data may differ.</summary>
public sealed record HostPaths(string ProgramDirectory, string AssetDirectory,
    string? SaveDirectory, string? PictureDirectory, string LogDirectory)
{
    public HostPaths FullPaths() => new(
        Path.GetFullPath(ProgramDirectory), Path.GetFullPath(AssetDirectory),
        SaveDirectory is null ? null : Path.GetFullPath(SaveDirectory),
        PictureDirectory is null ? null : Path.GetFullPath(PictureDirectory),
        Path.GetFullPath(LogDirectory));
}

/// <summary>The game-facing hooks a platform host must configure before Game.Run.</summary>
public interface IGameHostAdapter
{
    void ConfigureNativeLibraries(HostPaths paths);
    void ConfigureStorage(HostPaths paths);
    void ConfigureWindow(object game);
    void ConfigureInput(object game);
}

/// <summary>System file selection for first import and later repair.</summary>
public interface IGameFilePicker
{
    Task<string?> PickGamePackageAsync(CancellationToken cancellationToken);
    Task<string?> PickOptionalArtAsync(string sourceKind, CancellationToken cancellationToken);
}

/// <summary>System photo selection for the creator; paths or handles must remain valid for copying.</summary>
public interface IGamePhotoPicker
{
    Task<IReadOnlyList<string>> PickPhotosAsync(int maximumCount, CancellationToken cancellationToken);
}
