using System.Security.Cryptography;
using System.Text.Json;
using System.Reflection.PortableExecutable;
using Port.Prepare;

// Content-free build inputs: framework/port metadata, a reviewed ABI plan and runtime metadata.
var root = Path.GetFullPath("out/android/import-inputs");
Directory.CreateDirectory(root);
var expected = new HashSet<string>(StringComparer.Ordinal);
void Copy(string source, string relative)
{
    var destination = Path.Combine(root, relative);
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.Copy(source, destination, true);
    expected.Add(relative.Replace('\\', '/'));
}
Copy("config/android-rewrite-abi.json", "android-rewrite-abi.json");
Copy("out/deps/managed/FNA.dll", "metadata/FNA.dll");
foreach (var name in new[] { "Port.Bridge", "Port.Hooks" })
{
    Copy($"src/{name}/bin/Release/net10.0/{name}.dll", $"metadata/{name}.dll");
    Copy($"src/{name.Replace("Port.", "Port.Android")}/bin/Release/net10.0/{name}.dll", $"android/{name}.dll");
}
Copy("out/android/fna-package/lib/net9.0-android35.0/FNA.NET.dll", "android/FNA.NET.dll");
var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
foreach (var path in Directory.GetFiles(runtime, "*.dll"))
{
    using var input = File.OpenRead(path);
    using var pe = new PEReader(input);
    if (pe.HasMetadata) Copy(path, "runtime/" + Path.GetFileName(path));
}
_ = IlRewriter.ReadPinnedPlan(Path.Combine(root, "android-rewrite-abi.json"),
    Path.Combine(root, "metadata/FNA.dll"), Path.Combine(root, "metadata/Port.Bridge.dll"), Path.Combine(root, "runtime"));
foreach (var path in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
    if (!expected.Contains(Path.GetRelativePath(root, path).Replace('\\', '/'))) File.Delete(path);
var entries = expected.Order(StringComparer.Ordinal).Select(relative =>
{
    var path = Path.Combine(root, relative);
    using var input = File.OpenRead(path);
    return new { Path = relative, Bytes = input.Length, Sha256 = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant() };
}).ToArray();
File.WriteAllText(Path.Combine(root, "inputs-manifest.json"), JsonSerializer.Serialize(entries));
Console.WriteLine($"ANDROID_IMPORT_INPUTS_READY files={entries.Length}");
