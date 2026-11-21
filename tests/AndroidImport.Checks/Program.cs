using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Port.Art;
using Port.Content;
using Port.IosGame;
using Port.Prepare;

if (args.Length < 2) throw new ArgumentException("Expected <supported.xap> <host-converter-library> [I-MAED-package] [Time-Viking-package] [existing-check-root].");
var xap = Path.GetFullPath(args[0]);
var hash = Hash(xap);
var existing = args.Length == 5;
var root = Path.GetFullPath(existing ? args[4] : Path.Combine(
    Environment.GetEnvironmentVariable("Z0MB1ES_ANDROID_CHECK_ROOT") ?? "private/android", "import-check-" + Guid.NewGuid().ToString("N")));
Check(Path.GetFileName(root).StartsWith("import-check-", StringComparison.Ordinal), "dedicated check directory required");
var saves = Path.Combine(root, "Saves");
if (existing) Check(File.ReadAllText(Path.Combine(saves, "sentinel")) == "keep saves", "existing check sentinel");
else
{
    Directory.CreateDirectory(saves);
    File.WriteAllText(Path.Combine(saves, "sentinel"), "keep saves");
}
var converter = new NativeConverter(Path.GetFullPath(args[1]));
var preparations = existing ? 1 : 0;
var store = new PersistentAssetStore(Path.Combine(root, "Assets"), saves,
    (source, work, progress, token) =>
    {
        preparations++;
        return AndroidGamePreparation.Prepare(source, work,
            Path.GetFullPath("out/android/import-inputs"), converter, progress, token);
    }, new PortableArtImporter());
if (!existing) Check(store.TryOpen() is null, "fresh install");
var picks = Path.Combine(root, "picks");
if (!existing)
{
    using var setup = new FolderSetup(picks, saves, store, null, true, Console.WriteLine, usesPicker: true);
    Check(setup.Page == SetupPage.Warning, "warning precedes file selection");
    setup.ContinueWarning();
    var copy = Path.Combine(root, "phone.xap");
    File.Copy(xap, copy);
    setup.TakePickedFile("phone", copy);
    Wait(setup);
    Check(setup.Phone is not null && setup.CanImport, "picked package verified");
    if (args.Length >= 4)
    {
        foreach (var (slot, source) in new[] { ("normal", args[2]), ("viking", args[3]) })
        {
            var selected = Path.Combine(root, slot + Path.GetExtension(source));
            File.Copy(source, selected);
            setup.TakePickedFile(slot, selected);
            Wait(setup);
        }
        Check(setup.Normal is not null && setup.Viking is not null, "optional packages verified");
    }
    setup.Import();
    Wait(setup);
    Check(setup.LaunchRequested && setup.Active is not null, "setup hands off verified assets");
    Check(setup.Phone is null && setup.Normal is null && setup.Viking is null, "temporary selections released");
}
Check(!Directory.Exists(picks), "private picker staging removed");
var active = store.TryOpen() ?? throw new Exception("Imported assets unavailable.");
if (args.Length >= 4)
{
    var parity = new ProcessStartInfo("python") { UseShellExecute = false };
    parity.ArgumentList.Add("tests/AndroidImport.Checks/art-parity.py");
    parity.ArgumentList.Add(active.ArtRoot);
    parity.ArgumentList.Add("private/run/current/optional-art");
    using (var comparison = Process.Start(parity)!)
    {
        comparison.WaitForExit();
        Check(comparison.ExitCode == 0, "desktop artwork pixel parity");
    }
    using var reimport = new FolderSetup(picks, saves, store, active, false, Console.WriteLine, usesPicker: true);
    var selected = Path.Combine(root, "replacement-art");
    File.Copy(args[2], selected);
    reimport.TakePickedFile("normal", selected);
    Wait(reimport);
    reimport.Import();
    Wait(reimport);
    Check(reimport.Page == SetupPage.Restart && !reimport.LaunchRequested && preparations == 1,
        "artwork-only reimport reuses phone data and requests a fresh session");
    active = store.TryOpen() ?? throw new Exception("Reimported assets unavailable.");
}
using (var assembly = AssemblyDefinition.ReadAssembly(active.DllPath))
{
    Check(assembly.MainModule.AssemblyReferences.Any(r => r.Name == "FNA.NET") &&
        assembly.MainModule.AssemblyReferences.All(r => r.Name != "FNA"), "Android framework identity");
}
using (var report = JsonDocument.Parse(File.ReadAllText(active.DllPath + ".abi.json")))
    Check(report.RootElement.GetProperty("errors").GetArrayLength() == 0, "exact Android ABI check");
using (var content = JsonDocument.Parse(File.ReadAllText(Path.Combine(active.ContentRoot, "content-manifest.json"))))
    foreach (var entry in content.RootElement.GetProperty("Outputs").EnumerateArray())
    {
        var relative = entry.GetProperty("Path").GetString()!;
        Check(Hash(Path.Combine(active.ContentRoot, relative)) == Hash(Path.Combine("private/run/current", relative)), "desktop content parity: " + relative);
    }
using (var canceled = new CancellationTokenSource())
{
    canceled.Cancel();
    try { store.Import(new(xap, null, null), cancellationToken: canceled.Token); throw new Exception("Cancellation ignored."); }
    catch (OperationCanceledException) { }
}
Check(store.TryOpen() is not null && File.ReadAllText(Path.Combine(saves, "sentinel")) == "keep saves", "cancellation preserves installed assets and saves");
Check(Hash(xap) == hash, "original package unchanged");
Check(FirstLaunchWarning.IsAcknowledged(saves), "warning survives restart");
var corrupt = Path.Combine(active.ContentRoot, "Content/Arial.xnb");
File.WriteAllText(corrupt, "damaged");
Check(store.TryOpen() is null, "damaged content returns to repair");
Console.WriteLine("ANDROID_IMPORT_CHECKS_OK " + root);

static void Check(bool pass, string message) { if (!pass) throw new Exception(message); }
static void Wait(FolderSetup setup)
{
    var timeout = DateTime.UtcNow.AddMinutes(10);
    while (setup.Page == SetupPage.Working && DateTime.UtcNow < timeout) { Thread.Sleep(20); setup.Poll(); }
    Check(setup.Page != SetupPage.Working, "operation timed out");
}
static string Hash(string path)
{
    using var input = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
}
sealed class NativeConverter : IContentAudioConverter
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ConvertAudio([MarshalAs(UnmanagedType.LPUTF8Str)] string input, [MarshalAs(UnmanagedType.LPUTF8Str)] string output);
    private readonly ConvertAudio convert;
    public string ToolSha256 { get; }
    public string OutputExtension => ".qoa";
    public NativeConverter(string library)
    {
        ToolSha256 = System.Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(library))).ToLowerInvariant();
        convert = Marshal.GetDelegateForFunctionPointer<ConvertAudio>(NativeLibrary.GetExport(NativeLibrary.Load(library), "z0_audio_convert"));
    }
    public void Convert(string input, string output, Action<long>? onProgress)
    {
        if (convert(input, output) != 0) throw new InvalidDataException("Native music conversion failed.");
    }
}
