using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mono.Cecil;

namespace Port.Prepare;

/// <summary>Checks every Android framework/bridge/hook reference before retargeting.</summary>
public static class AndroidFrameworkAdapter
{
    public static void Adapt(string sourceDll, string outputDll, string frameworkPath,
        string bridgePath, string hooksPath, string originalReferences, params string[] runtimeDirectories)
    {
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(frameworkPath))!);
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(bridgePath))!);
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(hooksPath))!);
        if (Path.GetDirectoryName(typeof(object).Assembly.Location) is { Length: > 0 } localRuntime)
            resolver.AddSearchDirectory(localRuntime);
        using var target = AssemblyDefinition.ReadAssembly(frameworkPath, new ReaderParameters { AssemblyResolver = resolver });
        if (target.Name.Name != "FNA.NET") throw new InvalidDataException("Unexpected framework identity.");
        using var game = AssemblyDefinition.ReadAssembly(sourceDll, new ReaderParameters { AssemblyResolver = resolver });
        using var originalResolver = new DefaultAssemblyResolver();
        originalResolver.AddSearchDirectory(originalReferences);
        foreach (var directory in runtimeDirectories) { resolver.AddSearchDirectory(directory); originalResolver.AddSearchDirectory(directory); }
        using var original = AssemblyDefinition.ReadAssembly(sourceDll,
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
        Directory.CreateDirectory(Path.GetDirectoryName(outputDll)!);
        File.WriteAllText(outputDll + ".abi.json", JsonSerializer.Serialize(new
        {
            framework = target.Name.FullName, frameworkSha256 = Hash(frameworkPath),
            sourcePreparedSha256 = Hash(sourceDll),
            bridgeSha256 = Hash(bridgePath), hooksSha256 = Hash(hooksPath), checkedTypes, checkedMembers, errors
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (errors.Count != 0) throw new InvalidDataException("Android ABI check failed:\n" + string.Join('\n', errors));
        game.Write(outputDll);
        var patch = JsonNode.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(sourceDll)!, "patch-manifest.json")))!;
        patch["desktopPreparedSha256"] = patch["preparedSha256"]!.GetValue<string>();
        patch["preparedSha256"] = Hash(outputDll);
        patch["androidFramework"] = target.Name.FullName;
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(outputDll)!, "patch-manifest.json"), patch.ToJsonString());
    }
    private static bool IsPortType(TypeReference type) => type.GetElementType().Scope is AssemblyNameReference scope &&
        scope.Name is "FNA.NET" or "Port.Bridge" or "Port.Hooks";
    private static string Hash(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
    }
}
