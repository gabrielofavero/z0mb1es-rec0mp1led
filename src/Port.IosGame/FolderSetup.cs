using Port.Art;
using Port.Prepare;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Port.IosGame;

internal sealed record ImportFile(string Path, string Kind);
internal sealed record ImportFiles(List<ImportFile> Phone, List<ImportFile> Normal,
    List<ImportFile> Viking, List<string> Notes);

// No UIKit, game assets, or loaded game assembly is needed for discovery/setup.
internal static class ImportFolder
{
    internal const string Instructions = "Add your phone XAP and optional I MAED/Time Viking artwork packages here. " +
        "In Z0MB1ES, tap Rescan, then Import.";

    internal static ImportFiles Scan(string directory, CancellationToken cancellation)
    {
        Directory.CreateDirectory(directory);
        var result = new ImportFiles([], [], [], []);
        var art = new PortableArtImporter();
        foreach (var path in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal).Take(256))
        {
            cancellation.ThrowIfCancellationRequested();
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
            var extension = System.IO.Path.GetExtension(path);
            var folder = Directory.Exists(path);
            try
            {
                if (extension.Equals(".xap", StringComparison.OrdinalIgnoreCase) && !folder)
                {
                    PreparationEngine.ValidateXap(path, cancellation);
                    result.Phone.Add(new(path, "phone"));
                }
                else if (folder || extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    if (folder && !File.Exists(System.IO.Path.Combine(path, "Z0MB1ES.exe"))) continue;
                    art.Preview("i-maed-steam", path);
                    result.Normal.Add(new(path, "i-maed-steam"));
                }
                else if (!folder && IsLive(path))
                {
                    try { art.Preview("i-maed-360", path); result.Normal.Add(new(path, "i-maed-360")); }
                    catch (Exception error) when (IsSourceError(error))
                    {
                        art.Preview("time-viking-360", path);
                        result.Viking.Add(new(path, "time-viking-360"));
                    }
                }
            }
            catch (Exception error) when (IsSourceError(error))
            {
                result.Notes.Add(System.IO.Path.GetFileName(path) + ": " + error.GetBaseException().Message);
            }
        }
        cancellation.ThrowIfCancellationRequested();
        return result;
    }

    private static bool IsLive(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> magic = stackalloc byte[4];
        return stream.Read(magic) == 4 && (magic.SequenceEqual("LIVE"u8) || magic.SequenceEqual("PIRS"u8) || magic.SequenceEqual("CON "u8));
    }

    private static bool IsSourceError(Exception error) => error is IOException or InvalidDataException or
        UnauthorizedAccessException or PreparationException or ArgumentException or System.Text.Json.JsonException;
}

internal enum SetupPage { Warning, Files, Working, Restart }

internal sealed class FolderSetup : IDisposable
{
    private readonly PersistentAssetStore store;
    private readonly string saves;
    private readonly bool allowLaunch;
    private readonly Action<string> log;
    private readonly Action? imported;
    internal bool UsesPicker { get; }
    private bool normalChosen;
    private bool vikingChosen;
    private Task<Action>? operation;
    private CancellationTokenSource? cancellation;
    private string status = "";
    private bool disposed;
    internal string Directory { get; }
    internal SetupPage Page { get; private set; }
    internal bool ReduceFlashing { get; set; }
    internal ActiveAssetSet? Active { get; private set; }
    internal bool LaunchRequested { get; private set; }
    internal bool AllowLaunch => allowLaunch;
    internal ImportFiles Files { get; private set; } = new([], [], [], []);
    internal ImportFile? Phone { get; private set; }
    internal ImportFile? Normal { get; private set; }
    internal ImportFile? Viking { get; private set; }
    internal string Status => Volatile.Read(ref status);
    internal bool CanImport => Phone is not null || Active is not null && (Normal is not null || Viking is not null);
    internal bool CanPlay => allowLaunch && Active is not null && Page == SetupPage.Files;

    internal FolderSetup(string directory, string saves, PersistentAssetStore store,
        ActiveAssetSet? active, bool allowLaunch, Action<string> log, Action? imported = null, bool usesPicker = false)
    {
        Directory = System.IO.Path.GetFullPath(directory);
        this.saves = saves;
        this.store = store;
        this.allowLaunch = allowLaunch;
        this.log = log;
        this.imported = imported;
        UsesPicker = usesPicker;
        if (UsesPicker) System.IO.Directory.CreateDirectory(Directory);
        Active = active;
        ReduceFlashing = FirstLaunchWarning.InitialReduceFlashing(saves);
        Page = FirstLaunchWarning.IsAcknowledged(saves) ? SetupPage.Files : SetupPage.Warning;
        if (Page == SetupPage.Files && !UsesPicker) Rescan();
    }

    internal void ContinueWarning()
    {
        if (Page != SetupPage.Warning) return;
        try
        {
            FirstLaunchWarning.Acknowledge(saves, ReduceFlashing);
            log("flashing-warning=acknowledged reduceFlashing=" + ReduceFlashing);
            Page = SetupPage.Files;
            if (!UsesPicker) Rescan();
        }
        catch (Exception error) { SetStatus("Could not save your choice. " + error.GetBaseException().Message); }
    }

    internal void Rescan()
    {
        if (Page != SetupPage.Files || UsesPicker) return;
        Begin("Checking files...", token =>
        {
            var files = ImportFolder.Scan(Directory, token);
            foreach (var note in files.Notes) log("import-source-rejected=" + note);
            return () =>
            {
                Files = files;
                Phone = files.Phone.FirstOrDefault(f => f == Phone) ?? (Active is null ? files.Phone.FirstOrDefault() : null);
                Normal = files.Normal.FirstOrDefault(f => f == Normal) ?? (normalChosen ? null : PreferredNormal(files));
                Viking = files.Viking.FirstOrDefault(f => f == Viking) ?? (vikingChosen ? null : files.Viking.FirstOrDefault());
                Page = SetupPage.Files;
                SetStatus(files.Notes.Count > 0 ? "Some files could not be verified." : "");
                log("import-folder=scanned phone=" + files.Phone.Count + " artwork=" + (files.Normal.Count + files.Viking.Count));
            };
        });
    }

    // The system picker uses asCopy:true. Only its disposable app-local copy is
    // transferred here; never move or delete a document at its original location.
    internal void TakePickedFile(string slot, string temporaryCopy)
    {
        if (!UsesPicker || Page != SetupPage.Files || slot is not ("phone" or "normal" or "viking")) return;
        string? folder = null;
        try
        {
            folder = System.IO.Path.Combine(Directory, Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(folder);
            var path = System.IO.Path.Combine(folder, System.IO.Path.GetFileName(temporaryCopy));
            File.Move(temporaryCopy, path);
            var staging = folder;
            Begin("Checking file...", token =>
            {
                try
                {
                    var files = ImportFolder.Scan(staging, token);
                    var selected = (slot == "phone" ? files.Phone : slot == "normal" ? files.Normal : files.Viking).FirstOrDefault();
                    if (selected is null) throw new InvalidDataException("The selected package does not match " + slot + ".");
                    token.ThrowIfCancellationRequested();
                    return () =>
                    {
                        ClearPickedFile(slot);
                        switch (slot)
                        {
                            case "phone": Phone = selected; break;
                            case "normal": Normal = selected; break;
                            case "viking": Viking = selected; break;
                        }
                        Page = SetupPage.Files;
                        SetStatus("");
                        log("picker-selection=verified slot=" + slot + " kind=" + selected.Kind);
                    };
                }
                catch { DeletePickedDirectory(staging); throw; }
            });
        }
        catch (Exception error)
        {
            if (folder is not null) DeletePickedDirectory(folder);
            log("picker-selection-failure=" + error);
            SetStatus("Could not read that file.");
        }
    }

    internal void ClearPickedFile(string slot)
    {
        if (!UsesPicker || Page == SetupPage.Warning || Page == SetupPage.Restart) return;
        var selected = slot == "phone" ? Phone : slot == "normal" ? Normal : Viking;
        if (selected is not null) DeletePickedDirectory(System.IO.Path.GetDirectoryName(selected.Path)!);
        switch (slot)
        {
            case "phone": Phone = null; break;
            case "normal": Normal = null; break;
            case "viking": Viking = null; break;
        }
    }

    private void DeletePickedDirectory(string path)
    {
        try { if (System.IO.Directory.Exists(path)) System.IO.Directory.Delete(path, true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { log("picker-cleanup-failure=" + error.Message); }
    }

    internal void Cycle(string slot)
    {
        if (Page != SetupPage.Files) return;
        static ImportFile? Next(List<ImportFile> files, ImportFile? current)
        {
            var index = current is null ? -1 : files.IndexOf(current);
            return index + 1 < files.Count ? files[index + 1] : null;
        }
        switch (slot)
        {
            case "phone": Phone = Next(Files.Phone, Phone); break;
            case "normal": normalChosen = true; Normal = Normal is null ? PreferredNormal(Files) : null; break;
            case "viking": vikingChosen = true; Viking = Viking is null ? Files.Viking.FirstOrDefault() : null; break;
        }
    }

    internal void Import()
    {
        if (Page != SetupPage.Files || !CanImport) return;
        var selection = new AssetImportSelection(Phone?.Path,
            Normal is null ? null : new ArtImport(Normal.Kind, Normal.Path),
            Viking is null ? null : new ArtImport(Viking.Kind, Viking.Path));
        log($"import-selection phone={selection.PhoneXap is not null} normal={selection.Normal?.Kind ?? "keep"} viking={selection.Viking?.Kind ?? "keep"}");
        Begin("Reading your packages...", token =>
        {
            var active = store.Import(selection, stage =>
            {
                log("import-stage=" + stage);
                SetStatus(stage switch
                {
                    "prepare-phone" or "extract" => "Reading the phone package...",
                    "content-in-process" => "Preparing graphics and music...",
                    "verify" or "verified" => "Checking prepared files...",
                    "activate" => "Finishing import...",
                    _ when stage.Contains("360", StringComparison.Ordinal) || stage.Contains("steam", StringComparison.Ordinal) || stage.Contains("endless", StringComparison.Ordinal)
                        => "Preparing optional artwork...",
                    _ => "Preparing the game..."
                });
            }, token);
            return () =>
            {
                Active = active;
                imported?.Invoke();
                log("game-import=passed source=Files");
                if (UsesPicker)
                {
                    ClearPickedFile("phone"); ClearPickedFile("normal"); ClearPickedFile("viking");
                }
                try { SelectImportedArtwork(selection); }
                catch (Exception error)
                {
                    log("artwork-preference-failure=" + error);
                    Page = allowLaunch ? SetupPage.Files : SetupPage.Restart;
                    SetStatus(allowLaunch ? "Imported. Select artwork in Settings." : "Reopen to play.");
                    return;
                }
                Page = allowLaunch ? SetupPage.Files : SetupPage.Restart;
                LaunchRequested = allowLaunch;
                SetStatus("Reopen to play.");
            };
        });
    }

    internal void Play()
    {
        if (CanPlay) LaunchRequested = true;
    }

    internal void Cancel() => cancellation?.Cancel();

    internal void Poll()
    {
        if (operation?.IsCompleted != true) return;
        var completed = operation;
        operation = null;
        try
        {
            var apply = completed.GetAwaiter().GetResult();
            apply();
        }
        catch (Exception error)
        {
            Page = SetupPage.Files;
            log("files-setup-failure=" + error);
            SetStatus(error is OperationCanceledException ? "Canceled." : UsesPicker && error is InvalidDataException
                ? "Choose a supported file for this game." : "Import failed. See log.");
        }
        finally { cancellation?.Dispose(); cancellation = null; }
    }

    internal void SetStatus(string value) => Volatile.Write(ref status, value);

    private static ImportFile? PreferredNormal(ImportFiles files) =>
        files.Normal.FirstOrDefault(f => f.Kind == "i-maed-360") ?? files.Normal.FirstOrDefault();

    private void SelectImportedArtwork(AssetImportSelection selection)
    {
        if (selection.Normal is null && selection.Viking is null) return;
        var path = System.IO.Path.Combine(saves, "assets.json");
        JsonObject preferences = new();
        if (File.Exists(path))
        {
            try { preferences = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new(); }
            catch (JsonException) { }
        }
        preferences["Format"] = 1;
        if (selection.Normal is not null) preferences["Normal"] = "imported";
        if (selection.Viking is not null) preferences["Viking"] = "imported";
        preferences["Endless"] = "imported";
        System.IO.Directory.CreateDirectory(saves);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, preferences.ToJsonString() + "\n");
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private void Begin(string message, Func<CancellationToken, Action> work)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (operation is not null) return;
        Page = SetupPage.Working;
        SetStatus(message);
        cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        operation = Task.Run(() => work(token));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        cancellation?.Cancel();
        // Never leave a transaction running underneath game startup/shutdown.
        try { operation?.GetAwaiter().GetResult(); }
        catch (Exception error) { log("setup-shutdown=" + error.GetBaseException().Message); }
        cancellation?.Dispose();
        if (UsesPicker) DeletePickedDirectory(Directory);
    }
}
