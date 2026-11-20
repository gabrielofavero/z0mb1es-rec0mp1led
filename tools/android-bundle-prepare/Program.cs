using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Port.Prepare;

if (args.Length != 5)
    throw new ArgumentException("Expected <verified-desktop-stage> <android-FNA.NET.dll> <android-bridge> <android-hooks> <new-private-output>.");
var source = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[4]);
if (!output.StartsWith(Path.GetFullPath("private") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Path.Exists(output))
    throw new ArgumentException("Bundled output must be a new directory under private/.");
PreparedGameVerifier.Verify(Path.Combine(source, "ZombiesWP7.dll"), source);
var pin = JsonNode.Parse(File.ReadAllText("config/android-packaging.json"))!;
if (Hash(args[1]) != pin["fnaAndroidAssemblySha256"]!.GetValue<string>())
    throw new InvalidDataException("Android FNA.NET differs from the reviewed package.");
var payload = Path.Combine(output, "preinstalled");
Directory.CreateDirectory(Path.Combine(payload, "game"));
AndroidFrameworkAdapter.Adapt(Path.Combine(source, "ZombiesWP7.dll"), Path.Combine(payload, "game/ZombiesWP7.dll"),
    args[1], args[2], args[3], source, Path.GetDirectoryName(typeof(object).Assembly.Location)!);
using (var content = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(source, "content-manifest.json"))))
    foreach (var item in content.RootElement.GetProperty("Outputs").EnumerateArray())
    {
        var relative = item.GetProperty("Path").GetString()!;
        var destination = Path.Combine(payload, "content", relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(Path.Combine(source, relative), destination);
    }
File.Copy(Path.Combine(source, "content-manifest.json"), Path.Combine(payload, "content/content-manifest.json"));
if (Directory.Exists(Path.Combine(source, "optional-art")))
    CopyTree(Path.Combine(source, "optional-art"), Path.Combine(payload, "optional-art"));
PreparedGameVerifier.Verify(Path.Combine(payload, "game/ZombiesWP7.dll"), Path.Combine(payload, "content"));
var files = Directory.EnumerateFiles(payload, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
    .Select(path => new { Path = Path.GetRelativePath(payload, path).Replace('\\', '/'), Bytes = new FileInfo(path).Length, Sha256 = Hash(path) }).ToArray();
File.WriteAllText(Path.Combine(payload, "bundled-manifest.json"), JsonSerializer.Serialize(new
{
    Format = 1, FrameworkSha256 = Hash(args[1]), BridgeSha256 = Hash(args[2]), HooksSha256 = Hash(args[3]), Files = files
}));
Console.WriteLine($"ANDROID_PAYLOAD_READY files={files.Length} path={payload}");

static string Hash(string path)
{
    using var input = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
}
static void CopyTree(string source, string target)
{
    foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
    {
        var destination = Path.Combine(target, Path.GetRelativePath(source, file));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(file, destination);
    }
}
