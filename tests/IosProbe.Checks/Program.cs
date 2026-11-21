using System.Runtime.InteropServices;
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
        using var native = new LocalLibrary(library);
        NativeLibrary.SetDllImportResolver(typeof(NativeAudioConverter).Assembly,
            (name, _, _) => name == "__Internal" ? native.Handle : nint.Zero);
        var mobile = new NativeAudioConverter(native.ToolSha256);
        if (mobile.OutputExtension != ".qoa") throw new Exception("iOS converter must produce QOA.");
        try { mobile.Convert(badXap, Path.Combine(root, "bad.qoa"), null); throw new Exception("Invalid audio accepted."); }
        catch (InvalidDataException) { }
        if (File.Exists(Path.Combine(root, "bad.qoa"))) throw new Exception("Failed conversion left an output file.");
        var output = ExecutionProbe.ImportXap(xap, imports, mobile,
            s => { if (!s.StartsWith("content=", StringComparison.Ordinal)) Console.WriteLine(s); });
        if (Directory.GetFiles(Path.Combine(output, "content"), "*.qoa", SearchOption.AllDirectories).Length != 3)
            throw new Exception("Expected exactly three converted songs.");
        foreach (var song in Directory.GetFiles(Path.Combine(output, "content"), "*.qoa", SearchOption.AllDirectories))
        {
            using var stream = File.OpenRead(song);
            Span<byte> header = new byte[16];
            stream.ReadExactly(header);
            if (!header[..4].SequenceEqual("qoaf"u8) || header[8] != 2 ||
                (header[9] << 16 | header[10] << 8 | header[11]) != 44100)
                throw new Exception("Invalid QOA music: " + song);
            var samples = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header[4..8]);
            var expected = Path.GetFileNameWithoutExtension(song) switch
            {
                "endless" => 302.04517, "epicopus" => 817.94612, "timeviking" => 907.66803,
                _ => throw new Exception("Unexpected song: " + song)
            };
            if (Math.Abs(samples / 44100.0 - expected) > 0.15)
                throw new Exception("Converted music duration changed: " + song);
        }
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

sealed class LocalLibrary : IDisposable
{
    public nint Handle { get; }
    public string ToolSha256 { get; }
    public LocalLibrary(string library)
    {
        Handle = NativeLibrary.Load(Path.GetFullPath(library));
        ToolSha256 = System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(library))).ToLowerInvariant();
    }
    public void Dispose() => NativeLibrary.Free(Handle);
}
