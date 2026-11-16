using Port.Analysis;
using System.IO.Compression;
using System.Text.Json;
using System.Security.Cryptography;
using Port.Prepare;
using Port.Content;
using System.Runtime.InteropServices;
using System.Text;
using Mono.Cecil;
using Port.Art;
using Port.IosGame;

if (args is ["--first-launch-warning"])
{
    await FirstLaunchWarningChecks.Run();
    return;
}

if (args is ["--creator-photo-check", var creatorGame])
{
    CreatorPhotoChecks.Run(creatorGame);
    return;
}

if (args is ["--creator-drag-check", var dragGame, var dragNativeDirectory])
{
    CreatorDragChecks.Run(dragGame, dragNativeDirectory);
    return;
}

if (args is ["--mobile-display-check", var displayGame, var displayNativeDirectory])
{
    MobileDisplayChecks.Run(displayGame, displayNativeDirectory);
    return;
}

if (args is ["--music-playback-check", var musicGame, var clip, var nativeDirectory])
{
    MusicPlaybackChecks.Run(musicGame, clip, nativeDirectory);
    return;
}

if (args is ["--mobile-menu-check", var mobileGame])
{
    MobileMenuChecks.Run(mobileGame);
    return;
}

if (args is ["--content-case-check", var contentRoot])
{
    using var manager = new CaseMappedContentManager(new Microsoft.Xna.Framework.GameServiceContainer(), contentRoot);
    var open = typeof(CaseMappedContentManager).GetMethod("OpenStream", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
    Stream Open(string name) => (Stream)open.Invoke(manager, [name])!;
    var count = 0;
    foreach (var file in Directory.EnumerateFiles(contentRoot, "*", SearchOption.AllDirectories)
                 .Where(p => Path.GetExtension(p).Equals(".xnb", StringComparison.OrdinalIgnoreCase)))
    {
        var relative = Path.GetRelativePath(contentRoot, file).Replace('\\', '/')[..^4];
        using var stream = Open(relative.ToUpperInvariant().Replace('/', '\\'));
        using var expected = File.OpenRead(file);
        if (((FileStream)stream).Name != Path.GetFullPath(file) || !SHA256.HashData(stream).SequenceEqual(SHA256.HashData(expected)))
            throw new Exception("Case-normalized lookup changed bytes or canonical path: " + relative);
        count++;
    }
    using (var font = Open("arial"))
        if (Path.GetFileName(((FileStream)font).Name) != "Arial.xnb") throw new Exception("Lowercase arial did not resolve to the original font.");
    foreach (var name in new[] { "../Arial", "gfx/../../Arial", "/Arial", "C:\\Arial", "", "missing-asset" })
    {
        try { using var bad = Open(name); throw new Exception("Invalid or missing asset accepted: " + name); }
        catch (System.Reflection.TargetInvocationException error) when (error.InnerException is Microsoft.Xna.Framework.Content.ContentLoadException) { }
    }
    Console.WriteLine("CONTENT_CASE_OK assets=" + count + "; lowercase arial maps to original Arial.xnb; invalid paths rejected");
    return;
}

if (args is ["--bundled-ipa", var personalIpa])
{
    using var zip = ZipFile.OpenRead(personalIpa);
    var entry = zip.Entries.Single(e => e.FullName.EndsWith(".app/Port.IosBundled.dll", StringComparison.Ordinal));
    var prefix = entry.FullName[..^"Port.IosBundled.dll".Length];
    using var input = entry.Open();
    using var memory = new MemoryStream();
    input.CopyTo(memory);
    memory.Position = 0;
    using var assembly = AssemblyDefinition.ReadAssembly(memory);
    VibrationPackageChecks.Run(assembly, "RunOriginal");
    foreach (var (name, method) in new[] { ("Program", "NativeMain"), ("Program", "RunOriginal"),
        ("GameUi", "StartWatchdog"), ("GameUi", "LogWindows"), ("GameUi", "ShowFailure"),
        ("CaseMappedContentManager", "OpenStream"), ("PhotoPicker", "DidFinishPicking") })
        if (assembly.MainModule.GetType("Port.IosGame." + name)?.Methods.Any(m => m.Name == method && m.HasBody) != true)
            throw new Exception("Personal package lost " + name + "." + method);
    if (assembly.MainModule.Types.Any(t => t.Name is "ImportShell" or "ImportController"))
        throw new Exception("Personal build unexpectedly contains the importer interface.");
    if (assembly.MainModule.GetType("Port.IosGame.GameUi").Methods.Any(m => m.Name is "AttachMenu" or "ShowMenu" or "ShowAssetMenu"))
        throw new Exception("Personal build still contains the tools overlay.");
    var update = assembly.MainModule.GetType("Port.IosGame.MobileSession").Methods.Single(m => m.Name == "Update");
    if (!update.Body.Instructions.Any(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "Port.Hooks.DesktopDisplay" && m.Name == "AttachSettingsMenu"))
        throw new Exception("Personal game does not install the shared Settings menu.");
    var original = assembly.MainModule.GetType("Port.IosGame.Program").Methods.Single(m => m.Name == "RunOriginal");
    if (original.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "set_ReimportRequested"))
        throw new Exception("Personal game unexpectedly enables asset replacement.");
    if (!original.Body.Instructions.Any(i => i.Operand is MethodReference method && method.Name == ".ctor" &&
            method.DeclaringType.FullName == "Port.IosGame.CaseMappedContentManager"))
        throw new Exception("Personal game does not use the case-mapped content manager.");
    if (!original.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "set_NativeBackButtonEnabled"))
        throw new Exception("Personal game does not enable touch Back.");
    foreach (var name in new[] { "game/ZombiesWP7.dll", "content/content-manifest.json", "bundled-manifest.json",
        "optional-art/i-maed-360/current.json", "optional-art/time-viking-360/current.json", "optional-art/endless-360/current.json" })
        if (zip.GetEntry(prefix + "preinstalled/" + name) is null) throw new Exception("Personal payload missing " + name);
    using (var gameInput = zip.GetEntry(prefix + "preinstalled/game/ZombiesWP7.dll")!.Open())
    using (var gameBytes = new MemoryStream())
    {
        gameInput.CopyTo(gameBytes);
        gameBytes.Position = 0;
        using var packagedGame = AssemblyDefinition.ReadAssembly(gameBytes);
        foreach (var (type, method, hook) in new[]
        {
            ("IMAK3Z0MB1EGAEM.menu.CustomMapper", "Click", "HandleMapperClick"),
            ("ZombiesWP7.menu.MenuItem", "TryClick", "TryClickPhotoAction")
        })
            if (!packagedGame.MainModule.GetType(type).Methods.Single(m => m.Name == method).Body.Instructions
                .Any(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "Port.Hooks.DesktopPictures" && m.Name == hook))
                throw new Exception("Personal package lost creator photo hook: " + hook);
    }
    Console.WriteLine("PERSONAL_BUNDLE_CODE_OK; device gameplay remains unverified");
    return;
}

if (args is ["--art-decode", var phoneRoot, var pixelOutput])
{
    Directory.CreateDirectory(pixelOutput);
    var rows = ArtData.Resource("asset-swap-inventory.json")["textures"]!.AsArray();
    var hashes = new Dictionary<string, string>();
    foreach (var row in rows.Where(r => r!.Text("source") == "phone"))
    {
        var name = row!.Text("path");
        var image = TexturePixels.Decode(File.ReadAllBytes(Path.Combine(phoneRoot, "Content", name)), row!);
        hashes.Add(name, ArtData.Hash(image.Rgba));
        var png = TexturePixels.CropPng(image, [0, 0, image.Width, image.Height], null);
        var path = Path.Combine(pixelOutput, name + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, png);
    }
    File.WriteAllText(Path.Combine(pixelOutput, "pixel-hashes.json"), JsonSerializer.Serialize(hashes));
    Console.WriteLine("PORTABLE_TEXTURE_DECODE_OK count=" + hashes.Count);
    return;
}

if (args is ["--game-bundle", var ipa])
{
    using var zip = ZipFile.OpenRead(ipa);
    var entry = zip.Entries.Single(e => e.FullName.EndsWith(".app/Port.IosGame.dll", StringComparison.Ordinal));
    var prefix = entry.FullName[..^"Port.IosGame.dll".Length];
    foreach (var name in new[] { "FNA", "Port.Bridge", "Port.Hooks", "Port.Prepare", "Port.Analysis", "Port.Art", "Port.Content", "Mono.Cecil" })
        if (zip.GetEntry(prefix + name + ".dll") is null) throw new Exception("Missing packaged host dependency: " + name);
    using var input = entry.Open();
    using var memory = new MemoryStream();
    input.CopyTo(memory);
    memory.Position = 0;
    using var assembly = AssemblyDefinition.ReadAssembly(memory);
    VibrationPackageChecks.Run(assembly, "CreateOriginal");
    var original = assembly.MainModule.GetType("Port.IosGame.Program").Methods.Single(m => m.Name == "CreateOriginal");
    if (!original.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "set_ReimportRequested"))
        throw new Exception("Importer game does not enable asset management from Settings.");
    if (assembly.MainModule.GetType("Port.IosGame.GameUi").Methods.Any(m => m.Name == "AttachMenu"))
        throw new Exception("Importer game still contains the fixed tools button.");
    if (!original.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "set_NativeBackButtonEnabled"))
        throw new Exception("iOS game does not enable touch Back.");
    foreach (var (typeName, method) in new[] {
        ("Program", "NativeMain"), ("Program", "CreateOriginal"), ("ImportController", "ViewDidLoad"),
        ("ImportController", "ViewDidAppear"), ("GameUi", "ShowImporter"), ("GameUi", "ShowAssetMenu"), ("HostCoordinator", "Begin"),
        ("ImportController", "Pick"), ("MobileSession", "Update"), ("PhotoPicker", "DidFinishPicking") })
        if (assembly.MainModule.GetType("Port.IosGame." + typeName)?.Methods.Any(m => m.Name == method && m.HasBody) != true)
            throw new Exception("Packaged iOS host lost " + typeName + "." + method);
    Console.WriteLine("IOS_GAME_BUNDLE_OK; original-game device execution remains unproven");
    return;
}

if (args is ["--prepare", var package, var staged, var checkRoot, var audioLibrary])
{
    Directory.CreateDirectory(checkRoot);
    var saves = Path.Combine(checkRoot, "Saves");
    Directory.CreateDirectory(saves);
    var sentinel = Path.Combine(saves, "preserved.txt");
    File.WriteAllText(sentinel, "preserve saves");
    using var converter = new CheckAudio(audioLibrary);
    var store = new PersistentAssetStore(Path.Combine(checkRoot, "Assets"), saves,
        (input, work, log, token) => IosGamePreparation.Prepare(input, work, staged, converter, log, token));
    var active = store.Import(new AssetImportSelection(package, null, null), Console.WriteLine);
    PreparedGameVerifier.Verify(active.DllPath, active.ContentRoot);
    var hash = SHA256.HashData(File.ReadAllBytes(active.DllPath));
    var invalid = Path.Combine(checkRoot, "invalid.xap");
    File.WriteAllText(invalid, "unsupported");
    try { store.Import(new AssetImportSelection(invalid, null, null)); throw new Exception("Invalid XAP accepted."); }
    catch (PreparationException) { }
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    try { store.Import(new AssetImportSelection(package, null, null), cancellationToken: cancelled.Token); throw new Exception("Cancellation ignored."); }
    catch (OperationCanceledException) { }
    var reopened = store.TryOpen() ?? throw new Exception("Failed repeat lost the active game.");
    if (!SHA256.HashData(File.ReadAllBytes(reopened.DllPath)).SequenceEqual(hash) ||
        File.ReadAllText(sentinel) != "preserve saves") throw new Exception("Repeat changed game or saves.");
    var badInputs = Path.Combine(checkRoot, "bad-catalog");
    Directory.CreateDirectory(Path.Combine(badInputs, "abi-runtime"));
    File.WriteAllText(Path.Combine(badInputs, "abi-runtime", "bad.dll"), "changed catalog");
    var rejectedWorkspace = Path.Combine(checkRoot, "rejected-workspace");
    try
    {
        IosGamePreparation.Prepare(package, rejectedWorkspace, badInputs, converter);
        throw new Exception("Changed iOS runtime catalog accepted.");
    }
    catch (PreparationException) { }
    if (Path.Exists(rejectedWorkspace)) throw new Exception("Changed runtime catalog mutated workspace.");
    Console.WriteLine("IOS_IN_PROCESS_PREPARATION_OK hash=" + Convert.ToHexString(hash).ToLowerInvariant());
    return;
}

if (args is not ["--review", var xap, var inputs, var output])
    throw new ArgumentException("Expected --review <supported.xap> <staged-inputs> <ignored-output>.");
Directory.CreateDirectory(output);
var inventory = Path.Combine(output, "evidence.json");
GameAnalysis.WriteInventory(xap, Path.Combine(inputs, "preparation/supported-versions.json"), inventory);
using (var zip = System.IO.Compression.ZipFile.OpenRead(xap))
    zip.GetEntry("ZombiesWP7.dll")!.ExtractToFile(Path.Combine(output, "original.dll"), overwrite: true);
var report = Path.Combine(output, "ios-abi-report.json");
GameAnalysis.WriteAbiReport(inventory, Path.Combine(output, "original.dll"),
    Path.Combine(inputs, "preparation/FNA.dll"), Path.Combine(inputs, "preparation/Port.Bridge.dll"),
    report, Path.Combine(inputs, "preparation/supported-versions.json"),
    Directory.GetFiles(Path.Combine(inputs, "abi-runtime"), "*.dll"), ".NET 10.0.10 ios-arm64 (metadata catalog)");
using var result = JsonDocument.Parse(File.ReadAllBytes(report));
var counts = result.RootElement.GetProperty("counts");
foreach (var name in new[] { "unresolvedTypes", "unresolvedMembers", "unresolvedSlots", "unresolvedInheritance", "unresolvedSpecs" })
    if (counts.GetProperty(name).GetInt32() != 0) throw new InvalidDataException("Unresolved target ABI: " + name);
Console.WriteLine("IOS_TARGET_ABI_SHA256 " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(report))).ToLowerInvariant());

sealed class CheckAudio : IContentAudioConverter, IDisposable
{
    private readonly nint handle;
    private readonly ConvertAudio convert;
    public CheckAudio(string path)
    {
        handle = NativeLibrary.Load(Path.GetFullPath(path));
        convert = Marshal.GetDelegateForFunctionPointer<ConvertAudio>(NativeLibrary.GetExport(handle, "z0_audio_convert"));
        ToolSha256 = System.Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    }
    public string ToolSha256 { get; }
    public void Convert(string input, string output, Action<long>? progress)
    {
        var error = new StringBuilder(512);
        if (convert(input, output, error, error.Capacity) != 0) throw new InvalidDataException(error.ToString());
    }
    public void Dispose() => NativeLibrary.Free(handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ConvertAudio([MarshalAs(UnmanagedType.LPUTF8Str)] string input,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string output, [Out] StringBuilder error, int capacity);
}
