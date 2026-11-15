using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Port.Art;
using Port.Content;
using Port.IosBundled;
using Port.Prepare;

if (args.Length is not (4 or 6))
    throw new ArgumentException("Expected <phone.xap> <staged-ios-inputs> <new-private-output> <mac-audio.dylib> [<zombies-360> <viking-360>].");
var output = Path.GetFullPath(args[2]);
var privateRoot = Path.GetFullPath("private") + Path.DirectorySeparatorChar;
if (!output.StartsWith(privateRoot, StringComparison.Ordinal) || Path.Exists(output))
    throw new ArgumentException("Personal game output must be a new directory under private/.");
PreparationEngine.ValidateXap(args[0]);
using var audio = new NativeAudio(args[3]);
var store = new PersistentAssetStore(Path.Combine(output, "Assets"), Path.Combine(output, "Saves"),
    (xap, work, log, token) => IosGamePreparation.Prepare(xap, work, args[1], audio, log, token), new PortableArtImporter());
var active = store.Import(new AssetImportSelection(args[0], args.Length == 6 ? new ArtImport("i-maed-360", args[4]) : null,
    args.Length == 6 ? new ArtImport("time-viking-360", args[5]) : null), Console.WriteLine);
BundledAssets.Create(active, Path.Combine(output, "preinstalled"));
Console.WriteLine("PERSONAL_PAYLOAD_READY " + Path.Combine(output, "preinstalled"));

sealed class NativeAudio : IContentAudioConverter, IDisposable
{
    private readonly nint handle;
    private readonly ConvertAudio convert;
    public NativeAudio(string path)
    {
        path = Path.GetFullPath(path);
        handle = NativeLibrary.Load(path);
        convert = Marshal.GetDelegateForFunctionPointer<ConvertAudio>(NativeLibrary.GetExport(handle, "z0_audio_convert"));
        using var input = File.OpenRead(path);
        ToolSha256 = System.Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
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
