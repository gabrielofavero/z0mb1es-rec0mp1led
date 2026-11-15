using System.Runtime.InteropServices;
using System.Text;
using System.IO.Compression;
using Mono.Cecil;
using Port.Content;
using Port.IosProbe;

if (args is ["--bundle", var ipa])
{
    // Check the shipped IL, not only the pre-link build. The initial device IPA
    // lost scene callbacks and experiment dependencies despite TrimMode=copy.
    using var archive = ZipFile.OpenRead(ipa);
    var entry = archive.Entries.Single(e => e.FullName.EndsWith(".app/Port.IosProbe.dll", StringComparison.Ordinal));
    var prefix = entry.FullName[..^"Port.IosProbe.dll".Length];
    foreach (var dependency in new[] { "Port.Prepare.dll", "Port.Content.dll", "Mono.Cecil.dll" })
        if (archive.GetEntry(prefix + dependency) is null)
            throw new InvalidDataException($"Missing packaged experiment dependency: {dependency}");
    using var input = entry.Open();
    using var buffer = new MemoryStream();
    input.CopyTo(buffer);
    buffer.Position = 0;
    using var assembly = AssemblyDefinition.ReadAssembly(buffer);
    foreach (var (typeName, methods) in new (string, string[])[]
    {
        ("AppDelegate", ["FinishedLaunching", "GetConfiguration"]),
        ("SceneDelegate", ["WillConnect", "get_Window", "set_Window"]),
        ("ProbeController", ["ViewDidLoad", "ViewDidAppear"]),
        ("ExecutionProbe", ["RunGeneratedIl", "ResumeGeneratedIl", "ImportXap", "ResumeImport"]),
        ("NativeAudioConverter", ["Convert"])
    })
    {
        var type = assembly.MainModule.GetType("Port.IosProbe." + typeName)
            ?? throw new InvalidDataException($"Packaged probe lost {typeName}.");
        foreach (var name in methods)
            if (!type.Methods.Any(m => m.Name == name && m.HasBody && m.Body.Instructions.Count > 0))
                throw new InvalidDataException($"Packaged probe lost {typeName}.{name}.");
    }
    Console.WriteLine("packaged-ui-callbacks-and-experiments=preserved; device-execution=unproven");
    return;
}

if (args is ["--resume", var persistent])
{
    Console.WriteLine(ExecutionProbe.ResumeGeneratedIl(persistent));
    return;
}
if (args is ["--generate", var destination])
{
    Console.WriteLine(ExecutionProbe.RunGeneratedIl(destination));
    return;
}
var root = Path.Combine(Path.GetTempPath(), "ios-probe-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var il = Path.Combine(root, "il");
    Console.WriteLine(ExecutionProbe.RunGeneratedIl(il));
    Console.WriteLine(ExecutionProbe.ResumeGeneratedIl(il));
    var dll = Directory.GetFiles(il, "*.dll").Single();
    File.AppendAllText(dll, "corrupt");
    try { ExecutionProbe.ResumeGeneratedIl(il); throw new Exception("Corrupt IL accepted."); }
    catch (InvalidDataException) { Console.WriteLine("corrupt-il=rejected"); }

    var badXap = Path.Combine(root, "bad.xap");
    File.WriteAllText(badXap, "unsupported");
    var imports = Path.Combine(root, "imports");
    try
    {
        ExecutionProbe.ImportXap(badXap, imports, new UnavailableConverter(), Console.WriteLine);
        throw new Exception("Unsupported XAP accepted.");
    }
    catch (Port.Prepare.PreparationException) { }
    if (Directory.Exists(imports)) throw new Exception("Unsupported XAP mutated output.");
    Console.WriteLine("unsupported-xap=rejected-without-mutation");

    if (args is [var xap, var library])
    {
        using var native = new LocalConverter(library);
        var output = ExecutionProbe.ImportXap(xap, imports, native,
            s => { if (!s.StartsWith("content=", StringComparison.Ordinal)) Console.WriteLine(s); });
        if (Directory.GetFiles(Path.Combine(output, "content"), "*.ogg", SearchOption.AllDirectories).Length != 3)
            throw new Exception("Expected exactly three converted songs.");
        // A failed repeat must not mutate the prior successful output.
        var priorManifest = File.ReadAllText(Path.Combine(output, "content", "content-manifest.json"));
        Console.WriteLine(ExecutionProbe.ResumeImport(imports));
        try
        {
            ExecutionProbe.ImportXap(xap, imports, new UnavailableConverter(), _ => { });
            throw new Exception("Converter failure was ignored.");
        }
        catch (InvalidOperationException error) when (error.Message == "injected converter failure") { }
        if (Directory.GetDirectories(imports).Length != 1 ||
            File.ReadAllText(Path.Combine(output, "content", "content-manifest.json")) != priorManifest)
            throw new Exception("Failed import changed previous results.");
        Console.WriteLine(ExecutionProbe.ResumeImport(imports));
        File.AppendAllText(Path.Combine(output, "content", "content-manifest.json"), "corrupt");
        try { ExecutionProbe.ResumeImport(imports); throw new Exception("Corrupt import accepted."); }
        catch (InvalidDataException) { Console.WriteLine("corrupt-persistent-import=rejected"); }
        Console.WriteLine("real-xap-extraction/metadata/three-song-conversion/failed-repeat=passed (desktop only)");
    }
    Console.WriteLine("These checks do not establish iOS execution or game-mode support.");
}
finally { Directory.Delete(root, recursive: true); }

sealed class UnavailableConverter : IContentAudioConverter
{
    public string ToolSha256 => new('a', 64);
    public void Convert(string input, string output, Action<long>? onProgress) =>
        throw new InvalidOperationException("injected converter failure");
}

sealed class LocalConverter : IContentAudioConverter, IDisposable
{
    private readonly nint handle;
    private readonly ConvertAudio convert;
    public LocalConverter(string library)
    {
        handle = NativeLibrary.Load(Path.GetFullPath(library));
        convert = Marshal.GetDelegateForFunctionPointer<ConvertAudio>(NativeLibrary.GetExport(handle, "z0_audio_convert"));
        ToolSha256 = System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(library))).ToLowerInvariant();
    }
    public string ToolSha256 { get; }
    public void Convert(string input, string output, Action<long>? onProgress)
    {
        var error = new StringBuilder(512);
        if (convert(input, output, error, error.Capacity) < 0) throw new InvalidDataException(error.ToString());
    }
    public void Dispose() => NativeLibrary.Free(handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ConvertAudio(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string input,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string output,
        [Out] StringBuilder error, int capacity);
}
