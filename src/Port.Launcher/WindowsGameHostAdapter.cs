using Microsoft.Xna.Framework;
using Port.Bridge;
using Port.Hooks;
using Port.Host;

internal sealed class WindowsGameHostAdapter : IGameHostAdapter
{
    public void ConfigureNativeLibraries(HostPaths paths)
    {
        // Windows native DLLs are copied beside the launcher by the pinned build.
    }

    public void ConfigureStorage(HostPaths paths)
    {
        DesktopStorage.Configure(paths.SaveDirectory);
        DesktopPictures.Configure(paths.PictureDirectory);
        DesktopPictures.PhotoSelectionRequested = null;
        PlayerIdentity.Configure(DesktopStorage.SaveDirectory);
    }

    public void ConfigureWindow(object game) => DesktopDisplay.Configure((Game)game);
    public void ConfigureInput(object game) => DesktopInput.Configure((Game)game);
}
