using Microsoft.Xna.Framework;
using Port.Bridge;
using Port.Hooks;
using Port.Host;

internal sealed class MacGameHostAdapter : IGameHostAdapter
{
    public void ConfigureNativeLibraries(HostPaths paths)
    {
        foreach (var name in new[] { "libSDL3.0.dylib", "libSDL3.dylib",
                     "libFNA3D.0.dylib", "libFNA3D.dylib", "libFAudio.0.dylib",
                     "libFAudio.dylib", "libtheorafile.dylib" })
            if (!File.Exists(Path.Combine(paths.ProgramDirectory, name)))
                throw new FileNotFoundException($"Packaged macOS library is missing: {name}");
    }

    public void ConfigureStorage(HostPaths paths)
    {
        DesktopStorage.Configure(paths.SaveDirectory);
        DesktopPictures.Configure(paths.PictureDirectory);
        DesktopPictures.PhotoSelectionRequested = () => new MacPhotoPicker(paths.ProgramDirectory)
            .PickPhotosAsync(256, CancellationToken.None).GetAwaiter().GetResult();
        PlayerIdentity.Configure(DesktopStorage.SaveDirectory);
    }

    public void ConfigureWindow(object game) => DesktopDisplay.Configure((Game)game);
    public void ConfigureInput(object game) => DesktopInput.Configure((Game)game);
}
