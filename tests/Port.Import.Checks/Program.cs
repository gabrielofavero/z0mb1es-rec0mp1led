using Port.Prepare;
using Port.Host;

if (args.Length == 2 && args[0] == "--flow-only")
{
    var testRoot = Path.GetFullPath(args[1]);
    var flowPicker = new FakePicker();
    var flowOrdinaryView = new FakeView();
    var prepared = new PersistentAssetStore(Path.Combine(testRoot, "assets"),
        Path.Combine(testRoot, "saves"), (_, _, _, _) =>
            throw new Exception("Ordinary start unexpectedly prepared a XAP."));
    await AssetSessionCoordinator.RunAsync(prepared, flowPicker, flowOrdinaryView,
        (active, _) => Task.FromResult(false));
    if (flowPicker.Calls != 0 || flowOrdinaryView.Choices != 0)
        throw new Exception("Ordinary start requested source packages.");
    var empty = new PersistentAssetStore(Path.Combine(testRoot, "empty-assets"),
        Path.Combine(testRoot, "saves"), (_, _, _, _) =>
            throw new Exception("Incomplete onboarding unexpectedly prepared a XAP."));
    var flowIncompleteView = new FakeView(new ImportChoices(true, false, null, false));
    var flowIncomplete = await AssetImportFlow.RunAsync(empty, flowPicker, flowIncompleteView);
    if (flowIncomplete.Active is not null || flowIncomplete.Changed || flowIncompleteView.Choices != 2)
        throw new Exception("Incomplete onboarding did not return to the importer.");
    Console.WriteLine("IMPORT_FLOW_OK ordinary-start/no-picker/incomplete-onboarding");
    return 0;
}

if (args.Length != 5)
{
    Console.Error.WriteLine("Usage: Port.Import.Checks <xap> <prepared-dll-dir> <content-dir> <extracted-phone-root> <test-root>");
    return 2;
}

var root = Path.GetFullPath(args[4]);
Directory.CreateDirectory(root);
var assets = Path.Combine(root, "assets");
var saves = Path.Combine(root, "saves");
Directory.CreateDirectory(saves);
File.WriteAllText(Path.Combine(saves, "progress.dat"), "saved-run");
const string savedArtChoice = "{\"Format\":1,\"Normal\":\"imported\",\"Viking\":\"original\",\"Endless\":\"original\"}";
File.WriteAllText(Path.Combine(saves, "assets.json"), savedArtChoice);
var xap = Path.Combine(root, "source.xap");
File.Copy(args[0], xap, overwrite: true);
var identity = PreparationEngine.ValidateXap(xap);
foreach (var (kind, file) in new[]
{
    ("i-maed-360", "game/zombies-360"),
    ("time-viking-360", "game/time-vicking-360")
})
{
    if (File.Exists(file) && PinnedOptionalArtSource.Preview(kind, file).SourceBytes == 0)
        throw new Exception($"Pinned {kind} source was not verified.");
}
var fixtureGame = Path.Combine(root, "game-fixture");
Directory.CreateDirectory(fixtureGame);
foreach (var name in new[] { "ZombiesWP7.dll", "patch-manifest.json" })
    File.Copy(Path.Combine(args[1], name), Path.Combine(fixtureGame, name), overwrite: true);
var failPreparation = false;
GamePreparationResult Prepare(string source, string work, Action<string>? progress,
    CancellationToken cancellationToken)
{
    if (failPreparation) throw new IOException("Injected preparation failure.");
    if (PreparationEngine.ValidateXap(source).XapSha256 != identity.XapSha256)
        throw new InvalidDataException("Changed XAP.");
    return new GamePreparationResult(identity, Path.GetFullPath(args[3]), fixtureGame,
        Path.GetFullPath(args[2]));
}
var importer = new FakeArtImporter();
var store = new PersistentAssetStore(assets, saves, Prepare, importer);
if (store.TryOpen() is not null) throw new Exception("First launch was not empty.");
var first = store.Import(new AssetImportSelection(xap, null, null));
if (store.TryOpen()?.DllPath != first.DllPath) throw new Exception("First import did not persist.");
if (Directory.Exists(first.ArtRoot)) throw new Exception("Phone-only import created optional art.");
File.Delete(xap);
if (new PersistentAssetStore(assets, saves, Prepare, importer).TryOpen() is null)
    throw new Exception("Restart required the deleted XAP.");

var bad = Path.Combine(root, "bad.xap");
File.WriteAllBytes(bad, [0x50, 0x4B, 0x03]);
var before = File.ReadAllBytes(Path.Combine(first.Root, "asset-manifest.json"));
try { store.Import(new AssetImportSelection(bad, null, null)); throw new Exception("Bad XAP accepted."); }
catch (PreparationException) { }
if (!File.ReadAllBytes(Path.Combine(first.Root, "asset-manifest.json")).SequenceEqual(before))
    throw new Exception("Bad XAP changed the active import.");

var normalSource = Path.Combine(root, "normal-source.bin");
File.WriteAllText(normalSource, "source-1");
var added = store.Import(new AssetImportSelection(null,
    new ArtImport("i-maed-360", normalSource), null));
if (!File.Exists(Path.Combine(added.ArtRoot, "i-maed-360", "current.json")))
    throw new Exception("Optional addition did not persist.");
File.Delete(normalSource);
if (store.TryOpen() is null) throw new Exception("Optional import required its deleted source.");

var vikingSource = Path.Combine(root, "viking-source.bin");
File.WriteAllText(vikingSource, "source-2");
importer.FailAfterWrite = true;
before = File.ReadAllBytes(Path.Combine(added.Root, "asset-manifest.json"));
try { store.Import(new AssetImportSelection(null, null,
        new ArtImport("time-viking-360", vikingSource)));
    throw new Exception("Failed optional staging accepted."); }
catch (IOException error) when (error.Message == "Injected art staging failure.") { }
importer.FailAfterWrite = false;
if (!File.ReadAllBytes(Path.Combine(added.Root, "asset-manifest.json")).SequenceEqual(before) ||
    Directory.EnumerateDirectories(assets, ".stage-*").Any())
    throw new Exception("Failed optional staging changed active assets or left a stage.");

File.Copy(args[0], xap);
failPreparation = true;
before = File.ReadAllBytes(Path.Combine(added.Root, "asset-manifest.json"));
try { store.Import(new AssetImportSelection(xap, null, null)); throw new Exception("Failure accepted."); }
catch (IOException error) when (error.Message == "Injected preparation failure.") { }
failPreparation = false;
if (!File.ReadAllBytes(Path.Combine(added.Root, "asset-manifest.json")).SequenceEqual(before))
    throw new Exception("Failed replacement changed the active import.");

using (var cancellation = new CancellationTokenSource())
{
    cancellation.Cancel();
    try { store.Import(new AssetImportSelection(xap, null, null), cancellationToken: cancellation.Token);
        throw new Exception("Cancellation accepted."); }
    catch (OperationCanceledException) { }
}
if (store.TryOpen() is null) throw new Exception("Cancellation removed the active import.");

var replaced = store.Import(new AssetImportSelection(xap, null, null));
if (!File.Exists(Path.Combine(replaced.ArtRoot, "i-maed-360", "current.json")))
    throw new Exception("Base replacement lost optional art.");
var corrupt = Path.Combine(replaced.Root, "game", "ZombiesWP7.dll");
using (var stream = File.Open(corrupt, FileMode.Open, FileAccess.Write)) stream.WriteByte(0);
if (store.TryOpen() is not null) throw new Exception("Corrupt active set was launched.");
var repaired = store.Import(new AssetImportSelection(xap, null, null));
if (store.TryOpen() is null || !File.Exists(repaired.DllPath))
    throw new Exception("Repair did not restore launchability.");
if (File.ReadAllText(Path.Combine(saves, "progress.dat")) != "saved-run" ||
    File.ReadAllText(Path.Combine(saves, "assets.json")) != savedArtChoice)
    throw new Exception("Import changed saves or preferences.");
Directory.Move(Path.Combine(assets, "current"), Path.Combine(assets, "previous"));
if (store.TryOpen() is null || !Directory.Exists(Path.Combine(assets, "current")))
    throw new Exception("Interrupted activation did not recover the previous set.");
var picker = new FakePicker();
var ordinaryView = new FakeView();
await AssetSessionCoordinator.RunAsync(store, picker, ordinaryView,
    (active, _) => Task.FromResult(false));
if (picker.Calls != 0 || ordinaryView.Choices != 0)
    throw new Exception("Ordinary start requested source packages.");
var emptyStore = new PersistentAssetStore(Path.Combine(root, "empty-assets"), saves,
    Prepare, importer);
var incompleteView = new FakeView(new ImportChoices(true, false, null, false));
var incomplete = await AssetImportFlow.RunAsync(emptyStore, picker, incompleteView);
if (incomplete.Active is not null || incomplete.Changed || incompleteView.Choices != 2)
    throw new Exception("Incomplete onboarding did not return to the importer.");
Console.WriteLine("IMPORT_CHECKS_OK first/restart/source-delete/add/replace/bad/failure/cancel/repair/recovery/saves/preferences");
return 0;

sealed class FakeArtImporter : IOptionalArtImporter
{
    public bool FailAfterWrite { get; set; }
    public (string Version, long SourceBytes) Preview(string kind, string path) =>
        (kind, new FileInfo(path).Length);
    public void Prepare(string kind, string path, string phoneRoot, string artRoot,
        Action<string>? progress, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(artRoot, kind);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "current.json"), File.ReadAllText(path));
        if (FailAfterWrite) throw new IOException("Injected art staging failure.");
    }
    public void Compose(string phoneRoot, string artRoot, Action<string>? progress,
        CancellationToken cancellationToken)
    {
        var folder = Path.Combine(artRoot, "endless-360");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "current.json"), "composed");
    }
}

sealed class FakePicker : IGameFilePicker
{
    public int Calls { get; private set; }
    public Task<string?> PickGamePackageAsync(CancellationToken cancellationToken)
    { Calls++; return Task.FromResult<string?>(null); }
    public Task<string?> PickOptionalArtAsync(string sourceKind, CancellationToken cancellationToken)
    { Calls++; return Task.FromResult<string?>(null); }
}

sealed class FakeView(params ImportChoices[] answers) : IAssetImportView
{
    public int Choices { get; private set; }
    public Task<ImportChoices?> ChooseAsync(bool hasValidInstall, CancellationToken cancellationToken)
    {
        var choice = Choices < answers.Length ? answers[Choices] : null;
        Choices++;
        return Task.FromResult(choice);
    }
    public Task<bool> ConfirmAsync(AssetImportPreview preview, CancellationToken cancellationToken) =>
        Task.FromResult(true);
    public void Progress(string stage) { }
    public void Error(string message) => throw new Exception(message);
}
