using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mono.Cecil;
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
using var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[3]))!);
resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location)!);
using var target = AssemblyDefinition.ReadAssembly(args[1], new ReaderParameters { AssemblyResolver = resolver });
if (target.Name.Name != "FNA.NET") throw new InvalidDataException("Unexpected framework identity.");
using var game = AssemblyDefinition.ReadAssembly(Path.Combine(source, "ZombiesWP7.dll"), new ReaderParameters { AssemblyResolver = resolver });
using var originalResolver = new DefaultAssemblyResolver();
originalResolver.AddSearchDirectory(source);
using var original = AssemblyDefinition.ReadAssembly(Path.Combine(source, "ZombiesWP7.dll"),
    new ReaderParameters { AssemblyResolver = originalResolver });
// Standalone Cecil TypeRef rows do not carry a class/value flag. Compare the
// resolved definitions in both frameworks rather than TypeReference.IsValueType.
var originalKinds = original.MainModule.GetTypeReferences()
    .Where(type => type.Scope is AssemblyNameReference scope && scope.Name is "FNA" or "Port.Bridge" or "Port.Hooks")
    .ToDictionary(type => type.FullName, type => type.Resolve().IsValueType, StringComparer.Ordinal);
var framework = game.MainModule.AssemblyReferences.Single(reference => reference.Name == "FNA");
framework.Name = target.Name.Name;
framework.Version = target.Name.Version;
framework.PublicKeyToken = target.Name.PublicKeyToken;
var checkedTypes = 0;
var checkedMembers = 0;
var errors = new List<string>();
// Resolve every referenced framework/bridge/hook type and exact member after
// changing the scope, including constructed generic and inherited methods.
foreach (var type in game.MainModule.GetTypeReferences().Where(IsPortType))
{
    try
    {
        var resolved = type.Resolve() ?? throw new InvalidDataException("Unresolved type");
        if (resolved.IsValueType != originalKinds[type.FullName])
            throw new InvalidDataException("Class/value type kind differs");
        checkedTypes++;
    }
    catch (Exception error) { errors.Add(type.FullName + ": " + error.Message); }
}
foreach (var member in game.MainModule.GetMemberReferences().Where(member => IsPortType(member.DeclaringType)))
{
    try
    {
        var resolved = member switch
        {
            MethodReference method => (IMemberDefinition?)method.Resolve(),
            FieldReference field => field.Resolve(),
            _ => throw new InvalidDataException("Unsupported member reference")
        };
        if (resolved is null) throw new InvalidDataException("Unresolved exact member");
        checkedMembers++;
    }
    catch (Exception error) { errors.Add(member.FullName + ": " + error.Message); }
}
Directory.CreateDirectory(output);
File.WriteAllText(Path.Combine(output, "android-abi-report.json"), JsonSerializer.Serialize(new
{
    framework = target.Name.FullName, frameworkSha256 = Hash(args[1]),
    sourcePreparedSha256 = Hash(Path.Combine(source, "ZombiesWP7.dll")),
    bridgeSha256 = Hash(args[2]), hooksSha256 = Hash(args[3]), checkedTypes, checkedMembers, errors
}, new JsonSerializerOptions { WriteIndented = true }));
if (errors.Count != 0) throw new InvalidDataException("Android ABI check failed:\n" + string.Join('\n', errors));
var payload = Path.Combine(output, "preinstalled");
Directory.CreateDirectory(Path.Combine(payload, "game"));
game.Write(Path.Combine(payload, "game/ZombiesWP7.dll"));
var patch = JsonNode.Parse(File.ReadAllText(Path.Combine(source, "patch-manifest.json")))!;
patch["desktopPreparedSha256"] = patch["preparedSha256"]!.GetValue<string>();
patch["preparedSha256"] = Hash(Path.Combine(payload, "game/ZombiesWP7.dll"));
patch["androidFramework"] = target.Name.FullName;
File.WriteAllText(Path.Combine(payload, "game/patch-manifest.json"), patch.ToJsonString());
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
Console.WriteLine($"ANDROID_PAYLOAD_READY types={checkedTypes} members={checkedMembers} files={files.Length} path={payload}");

static bool IsPortType(TypeReference type) => type.GetElementType().Scope is AssemblyNameReference scope &&
    scope.Name is "FNA.NET" or "Port.Bridge" or "Port.Hooks";
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
