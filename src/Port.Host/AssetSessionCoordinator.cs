using Port.Prepare;

namespace Port.Host;

/// <summary>Owns onboarding, repair and game-session replacement for a platform host.</summary>
public static class AssetSessionCoordinator
{
    public static async Task RunAsync(PersistentAssetStore store, IGameFilePicker picker,
        IAssetImportView view,
        Func<ActiveAssetSet, CancellationToken, Task<bool>> runGame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runGame);
        var active = store.TryOpen();
        if (active is null)
        {
            var first = await AssetImportFlow.RunAsync(store, picker, view, cancellationToken);
            active = first.Active;
            if (active is null) return; // Incomplete onboarding is retried next start.
        }
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // The host disposes its game before returning true from this callback.
            if (!await runGame(active, cancellationToken)) return;
            var outcome = await AssetImportFlow.RunAsync(store, picker, view, cancellationToken);
            active = outcome.Active ?? active;
        }
    }
}
