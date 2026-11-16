using Microsoft.Xna.Framework;
using Port.Prepare;

namespace Port.IosGame;

internal static class HostCoordinator
{
    internal static void Run()
    {
        GameData.EnsureImportFolder();
        GameData.CleanInterruptedImports();
        try
        {
            var active = GameData.Store.TryOpen();
            if (active is null || GameData.ImportRequested || !FirstLaunchWarning.IsAcknowledged(GameData.Saves))
                active = Setup(active, allowLaunch: true);
            if (active is null) return;
            GameData.RequestImport(false);
            var (game, monitor) = Program.CreateOriginal(active);
            using (game)
            {
                game.Deactivated += (_, _) =>
                {
                    try { monitor.SaveForBackground(); }
                    catch (Exception error) { GameTrace.Write("background-save-failure=" + error); }
                };
                GameTrace.Write("original-game=starting");
                game.Run();
                GameTrace.Write("original-game=ended frames=" + monitor.Frames);
            }
            if (GameData.ImportRequested) Setup(GameData.Store.TryOpen(), allowLaunch: false);
        }
        catch (Exception error)
        {
            GameTrace.Write("host-failure=" + error);
            // Loaded game assemblies require a fresh process after repair.
            Setup(GameData.Store.TryOpen(), allowLaunch: false, error.GetBaseException().Message);
        }
    }

    private static ActiveAssetSet? Setup(ActiveAssetSet? active, bool allowLaunch, string? failure = null)
    {
        using var setup = new FolderSetup(GameData.Imports, GameData.Saves, GameData.Store, active, allowLaunch,
            GameTrace.Write, () => GameData.RequestImport(false));
        if (failure is not null) setup.SetStatus("Game startup failed: " + failure);
        using (var screen = new FolderInstallerGame(setup, mobile: true, Insets, GameTrace.Write)) screen.Run();
        // Dispose the setup window/device before Game1 creates its window.
        return setup.LaunchRequested ? setup.Active : null;
    }

    private static Vector4 Insets(Game game)
    {
        var window = GameUi.NativeWindow(game);
        if (window.Bounds.Width <= 0 || window.Bounds.Height <= 0) return Vector4.Zero;
        var safe = window.SafeAreaInsets;
        return new Vector4((float)(safe.Left / window.Bounds.Width), (float)(safe.Top / window.Bounds.Height),
            (float)(safe.Right / window.Bounds.Width), (float)(safe.Bottom / window.Bounds.Height));
    }
}
