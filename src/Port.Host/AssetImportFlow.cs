using Port.Prepare;

namespace Port.Host;

public sealed record ImportChoices(bool ReplacePhone, bool ReplaceNormal, string? NormalKind,
    bool ReplaceViking);
public sealed record AssetImportOutcome(ActiveAssetSet? Active, bool Changed);

/// <summary>Native host UI for onboarding and later repair/reimport.</summary>
public interface IAssetImportView
{
    Task<ImportChoices?> ChooseAsync(bool hasValidInstall, CancellationToken cancellationToken);
    Task<bool> ConfirmAsync(AssetImportPreview preview, CancellationToken cancellationToken);
    void Progress(string stage);
    void Error(string message);
}

/// <summary>Uses the same host picker and transaction for first launch and Settings reimport.</summary>
public static class AssetImportFlow
{
    public static async Task<AssetImportOutcome> RunAsync(PersistentAssetStore store,
        IGameFilePicker picker, IAssetImportView view, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(picker);
        ArgumentNullException.ThrowIfNull(view);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var active = store.TryOpen();
            var choices = await view.ChooseAsync(active is not null, cancellationToken);
            if (choices is null) return new AssetImportOutcome(active, false);
            try
            {
                if (active is null && !choices.ReplacePhone)
                    throw new InvalidDataException("Select a supported phone XAP to import or repair.");
                var phone = choices.ReplacePhone
                    ? await picker.PickGamePackageAsync(cancellationToken) : null;
                if (choices.ReplacePhone && phone is null) continue;
                if (choices.ReplaceNormal && choices.NormalKind is not ("i-maed-steam" or "i-maed-360"))
                    throw new InvalidDataException("Choose a supported I MAED source type.");
                var normal = choices.ReplaceNormal
                    ? await picker.PickOptionalArtAsync(choices.NormalKind!, cancellationToken) : null;
                if (choices.ReplaceNormal && normal is null) continue;
                var viking = choices.ReplaceViking
                    ? await picker.PickOptionalArtAsync("time-viking-360", cancellationToken) : null;
                if (choices.ReplaceViking && viking is null) continue;
                var selection = new AssetImportSelection(phone,
                    normal is null ? null : new ArtImport(choices.NormalKind!, normal),
                    viking is null ? null : new ArtImport("time-viking-360", viking));
                var preview = store.Preview(selection);
                if (!await view.ConfirmAsync(preview, cancellationToken)) continue;
                return new AssetImportOutcome(store.Import(selection, view.Progress, cancellationToken), true);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or
                UnauthorizedAccessException or PreparationException or ArgumentException)
            {
                view.Error(error.Message);
            }
        }
    }
}
