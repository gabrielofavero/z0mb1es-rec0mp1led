using Microsoft.Xna.Framework;
using Port.Bridge;
using Port.Hooks;
using Port.Host;

internal sealed class LinuxGameHostAdapter : IGameHostAdapter
{
    public void ConfigureNativeLibraries(HostPaths paths)
    {
        foreach (var library in new[] { "libSDL3.so.0", "libSDL3.so", "libFNA3D.so.0",
                     "libFNA3D.so", "libFAudio.so.0", "libFAudio.so", "libtheorafile.so" })
            if (!File.Exists(Path.Combine(paths.ProgramDirectory, library)))
                throw new FileNotFoundException($"Packaged Linux library is missing: {library}");
    }

    public void ConfigureStorage(HostPaths paths)
    {
        DesktopStorage.Configure(paths.SaveDirectory);
        DesktopPictures.Configure(paths.PictureDirectory);
        DesktopPictures.PhotoSelectionRequested = () => new LinuxPhotoPicker()
            .PickPhotosAsync(256, CancellationToken.None).GetAwaiter().GetResult();
        PlayerIdentity.Configure(DesktopStorage.SaveDirectory);
    }

    public void ConfigureWindow(object game) => DesktopDisplay.Configure((Game)game);
    public void ConfigureInput(object game) => DesktopInput.Configure((Game)game);
}
