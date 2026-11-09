using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;
using TypeReference = Mono.Cecil.TypeReference;
using TypeDefinition = Mono.Cecil.TypeDefinition;
using ModuleDefinition = Mono.Cecil.ModuleDefinition;
using MemberReference = Mono.Cecil.MemberReference;
using TypeSpecification = Mono.Cecil.TypeSpecification;

namespace Port.Prepare;

/// <summary>Retargets the exact, verified input's metadata without altering game logic.</summary>
public static class IlRewriter
{
    public const string Version = "0.6.0";
    private const string OriginalDllHash = "7b819cb562826ae4d2592ff741877e76da0e57bfbe48192aabb79319fdb36823";
    private const string FnaHash = "952ecb3d8a869ad1343bd5b761b8a1d19c3af21e9c3cd2fab3dbc75d6893a89d";
    private const string AbiReportHash = "2edec771cbbf30e59541ed851e1865463fc26c26a5c79bb54bbc69228301bd44";
    private const string BridgeAbiReportHash = "27dedb6dc2aa1f2b8a32a4f4f82969400091466554253c67f861b8ff9c7dedc6";
    private const string CurrentBridgeAbiReportHash = "d66bb2521301141ccc056d9b79a138cd8ad3457d468d80a64a17105fc1f608a0";
    private const string LatestBridgeAbiReportHash = "85752835fd6a5516ed5a863a0c4e7452c13453eb324bcca872b219303fa7676e";
    private const string RestartBridgeAbiReportHash = "df47f26c660a457f32c39950972ef77b8259d53b64f84e84ae5be85fef887e75";
    private const string RestoredBridgeAbiReportHash = "300aef24f365fee1a55ea2ea185f0131d891190c94ffbc5c7981bf92a4528482";
    private const string PictureButtonAbiReportHash = "7709d72d3dd18380e54363624c4fb489dcfefcde9030565fea488222c230acd8";
    private const string BridgeHash = "d7b11e67fcc1c6794f214c4d37b6a6bbc566661a20d8b12414cad3f3a462d5c2";
    private const string CurrentBridgeHash = "c2c478c167eb24178e1808f3940156eb588e1dee92b575b81a4786d85dd51ba0";
    private const string LatestBridgeHash = "9648d82be1ecccc706666d89e24c0571de3754dceba3115d050ee6aefafe2421";
    private const string RestartBridgeHash = "208f5a7306a194312f5700380b501f95c0d196497470ab1b3d8f9c71c9829416";
    private const string RestoredBridgeHash = "f7148387e070135e26f0358c0b95875c5c79016f5ece1356fdf21d5c6ed14445";
    private const string PictureButtonBridgeHash = "4667e14c19737b8f7b7ecad8f7091a02d8f2bd3649ff0c5be6cee1f703b7adf2";
    private static readonly string[] LegacyAssemblies =
    [
        "Microsoft.Xna.Framework", "Microsoft.Xna.Framework.Game", "Microsoft.Xna.Framework.Graphics",
        "Microsoft.Xna.Framework.Input.Touch", "Microsoft.Xna.Framework.GamerServices",
        "Microsoft.Devices.Sensors", "Microsoft.Phone"
    ];

    public static RewritePlan ReadPinnedPlan(string reportPath, string fnaPath, string bridgePath,
        string? runtimeAssemblyDirectory = null)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(reportPath));
        var root = document.RootElement;
        var reportHash = Hash(reportPath);
        if (reportHash is not (AbiReportHash or BridgeAbiReportHash or CurrentBridgeAbiReportHash or LatestBridgeAbiReportHash or RestartBridgeAbiReportHash or RestoredBridgeAbiReportHash or PictureButtonAbiReportHash) ||
            root.GetProperty("schemaVersion").GetInt32() != 1 ||
            root.GetProperty("input").GetProperty("dllSha256").GetString() != OriginalDllHash ||
            root.GetProperty("input").GetProperty("fnaSha256").GetString() != FnaHash ||
            root.GetProperty("counts").GetProperty("typeRefs").GetInt32() != 146 ||
            root.GetProperty("counts").GetProperty("memberRefs").GetInt32() != 382 ||
            Hash(fnaPath) != FnaHash)
            throw new PreparationException("ABI report or FNA does not match the pinned input.");
        var bridgeHash = Hash(bridgePath);
        if (bridgeHash is not (BridgeHash or CurrentBridgeHash or LatestBridgeHash or RestartBridgeHash or RestoredBridgeHash or PictureButtonBridgeHash))
            throw new PreparationException($"Bridge DLL differs from the pinned supported bridge: {bridgeHash}.");
        if (reportHash is BridgeAbiReportHash or CurrentBridgeAbiReportHash or LatestBridgeAbiReportHash or RestartBridgeAbiReportHash or RestoredBridgeAbiReportHash or PictureButtonAbiReportHash &&
            root.GetProperty("input").GetProperty("bridgeSha256").GetString() != bridgeHash)
            throw new PreparationException("Bridge DLL differs from the verified ABI report.");

        var targets = new Dictionary<string, string>(StringComparer.Ordinal);
        targets["FNA"] = fnaPath;
        targets["Port.Bridge"] = bridgePath;
        var runtimePaths = runtimeAssemblyDirectory is null
            ? ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            : Directory.GetFiles(runtimeAssemblyDirectory, "*.dll");
        var runtime = runtimePaths?
            .GroupBy(p => Path.GetFileNameWithoutExtension(p), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal)
            ?? throw new PreparationException(".NET runtime assembly catalog is unavailable.");
        var types = new List<TypeRetarget>();
        foreach (var row in root.GetProperty("types").EnumerateArray())
        {
            var target = row.GetProperty("targetAssembly").GetString()
                ?? row.GetProperty("ProposedTarget").GetString()
                ?? throw new PreparationException("ABI report has an unclassified type reference.");
            if (!targets.ContainsKey(target))
                targets[target] = runtime.TryGetValue(target, out var path) ? path :
                    throw new PreparationException($"Target runtime assembly is missing: {target}");
            types.Add(new TypeRetarget(row.GetProperty("token").GetInt32(),
                row.GetProperty("source").GetString()!, row.GetProperty("scope").GetString()!, target));
        }
        var members = root.GetProperty("members").EnumerateArray().Select(row =>
            new MemberAnchor(row.GetProperty("token").GetInt32(),
                row.GetProperty("parent").GetString()!, row.GetProperty("name").GetString()!,
                row.GetProperty("exactSignature").GetString()!)).ToArray();
        return new RewritePlan(OriginalDllHash, types, members, targets, "pinned ABI report");
    }

    public static RewriteResult Rewrite(string sourcePath, string outputDirectory, RewritePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var source = Path.GetFullPath(sourcePath);
        var final = Path.GetFullPath(outputDirectory);
        var privateRoot = Path.GetFullPath(Path.Combine("private", "prepared"));
        if (final != privateRoot &&
            !final.StartsWith(privateRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new PreparationException("Prepared output must be within private/prepared/.");
        if (Path.Exists(final)) throw new PreparationException($"Prepared output already exists: {final}");
        if (Path.GetFullPath(Path.Combine(final, "ZombiesWP7.dll")) == source)
            throw new PreparationException("The prepared output cannot replace the original DLL.");
        var sourceHash = Hash(source);
        if (sourceHash != plan.SourceSha256)
            throw new PreparationException($"Unexpected source DLL SHA-256 {sourceHash}; expected {plan.SourceSha256}.");

        var refs = plan.TargetAssemblies.ToDictionary(p => p.Key, p =>
        {
            var name = AssemblyName.GetAssemblyName(p.Value);
            if (name.Name != p.Key)
                throw new PreparationException($"Target file has the wrong assembly identity: {p.Key}.");
            return new AssemblyNameReference(name.Name!, name.Version!)
            {
                Culture = name.CultureName,
                PublicKeyToken = name.GetPublicKeyToken() ?? []
            };
        }, StringComparer.Ordinal);
        var parent = Path.GetDirectoryName(final) ?? throw new PreparationException("Output cannot be a drive root.");
        Directory.CreateDirectory(parent);
        var stage = Path.Combine(parent, $".{Path.GetFileName(final)}.stage-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(stage);
            var stageDll = Path.Combine(stage, "ZombiesWP7.dll");
            using (var module = ModuleDefinition.ReadModule(source, new ReaderParameters { ReadSymbols = false, InMemory = true }))
            {
                var sourceTypes = module.GetTypeReferences().OrderBy(t => t.MetadataToken.ToInt32()).ToArray();
                var sourceMembers = module.GetMemberReferences().OrderBy(m => m.MetadataToken.ToInt32()).ToArray();
                var byToken = plan.Types.ToDictionary(t => t.Token);
                if (sourceTypes.Length != plan.Types.Count || sourceMembers.Length != plan.Members.Count)
                    throw new PreparationException("Original metadata reference counts differ from the guarded ABI plan.");
                foreach (var type in sourceTypes)
                {
                    if (!byToken.TryGetValue(type.MetadataToken.ToInt32(), out var anchor) ||
                        !SameTypeName(type.FullName, anchor.Source) ||
                        ScopeName(type) != anchor.SourceScope)
                        throw new PreparationException($"Unexpected type anchor at token 0x{type.MetadataToken.ToInt32():X8}.");
                }
                var memberAnchors = plan.Members.ToDictionary(m => m.Token);
                foreach (var member in sourceMembers)
                {
                    if (!memberAnchors.TryGetValue(member.MetadataToken.ToInt32(), out var anchor) ||
                        member.Name != anchor.Name)
                        throw new PreparationException($"Unexpected member anchor at token 0x{member.MetadataToken.ToInt32():X8}.");
                }

                var changedTypeTokens = new HashSet<int>();
                var patches = new List<PatchRecord>();
                foreach (var type in sourceTypes)
                {
                    var anchor = byToken[type.MetadataToken.ToInt32()];
                    var target = refs[anchor.TargetAssembly];
                    if (SameAssembly(type.Scope, target)) continue;
                    changedTypeTokens.Add(anchor.Token);
                    patches.Add(new PatchRecord("type-reference", sourceHash, $"0x{anchor.Token:X8} {anchor.Source}",
                        null, $"{anchor.TargetAssembly}:{anchor.Source}", "scope equals target assembly"));
                    type.Scope = target;
                }

                using var pe = new PEReader(File.OpenRead(source));
                var metadata = pe.GetMetadataReader();
                foreach (var method in AllTypes(module).SelectMany(t => t.Methods).Where(m => m.HasBody))
                {
                    var original = metadata.GetMethodDefinition(System.Reflection.Metadata.Ecma335.MetadataTokens.MethodDefinitionHandle(checked((int)method.MetadataToken.RID)));
                    var bytes = pe.GetMethodBody(original.RelativeVirtualAddress).GetILBytes()
                        ?? throw new PreparationException($"Method body is unavailable: {method.FullName}");
                    var bodyHash = Hex(SHA256.HashData(bytes));
                    var affected = method.Body.Instructions.Where(i =>
                        ReferencesRetargetedOperand(i.Operand, changedTypeTokens)).ToArray();
                    if (affected.Length == 0) continue;
                    patches.Add(new PatchRecord("method-il-operands", sourceHash,
                        $"0x{method.MetadataToken.ToInt32():X8} {method.FullName}", bodyHash,
                        string.Join(", ", affected.Select(i =>
                            $"IL_{i.Offset:X4}:{i.OpCode.Name}->{OperandTarget(i.Operand!)}").Distinct()),
                        "all referenced type scopes equal guarded targets; IL body decodes"));
                }

                module.AssemblyReferences.Clear();
                foreach (var reference in refs.Values.Where(r => sourceTypes.Any(t => ReferenceEquals(t.Scope, r)))
                             .OrderBy(r => r.Name, StringComparer.Ordinal))
                    module.AssemblyReferences.Add(reference);
                module.Write(stageDll, new WriterParameters { WriteSymbols = false });
                VerifyWritten(stageDll, plan, LegacyAssemblies);

                var outputHash = Hash(stageDll);
                var manifest = new PatchManifest(1, Version, sourceHash, outputHash,
                    plan.Description, patches.OrderBy(p => p.Kind).ThenBy(p => p.TargetSignature, StringComparer.Ordinal).ToArray());
                File.WriteAllText(Path.Combine(stage, "patch-manifest.json"),
                    JsonSerializer.Serialize(manifest, JsonOptions) + "\n", new UTF8Encoding(false));
            }
            if (Path.Exists(final)) throw new PreparationException($"Prepared output appeared during staging: {final}");
            Directory.Move(stage, final);
            return new RewriteResult(final, Hash(Path.Combine(final, "ZombiesWP7.dll")));
        }
        catch
        {
            if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
            throw;
        }
    }

    private static void VerifyWritten(string path, RewritePlan plan, IEnumerable<string> forbidden)
    {
        using var pe = new PEReader(File.OpenRead(path));
        var metadata = pe.GetMetadataReader();
        if (plan.SourceSha256 == OriginalDllHash &&
            (metadata.TypeDefinitions.Count != 178 || metadata.MethodDefinitions.Count != 818))
            throw new PreparationException("Prepared game type/method counts changed.");
        // Cecil may omit metadata rows that were present but unreferenced in the input.
        if (metadata.TypeReferences.Count > plan.Types.Count ||
            metadata.MemberReferences.Count > plan.Members.Count)
            throw new PreparationException($"Prepared reference counts grew: types {metadata.TypeReferences.Count}/{plan.Types.Count}, members {metadata.MemberReferences.Count}/{plan.Members.Count}.");
        foreach (var method in metadata.MethodDefinitions)
        {
            var definition = metadata.GetMethodDefinition(method);
            if (definition.RelativeVirtualAddress != 0)
                _ = pe.GetMethodBody(definition.RelativeVirtualAddress).GetILBytes();
        }
        using var module = ModuleDefinition.ReadModule(path, new ReaderParameters { InMemory = true });
        foreach (var type in module.GetTypeReferences())
        {
            var original = plan.Types.SingleOrDefault(t => SameTypeName(t.Source, type.FullName));
            if (original is null) throw new PreparationException($"Unexpected prepared type reference: {type.FullName}");
            if (ScopeName(type) != original.TargetAssembly)
                throw new PreparationException($"Postcondition failed for {type.FullName}: {ScopeName(type)}");
        }
        foreach (var name in forbidden)
            if (module.AssemblyReferences.Any(a => a.Name == name))
                throw new PreparationException($"Legacy assembly reference remains: {name}");
        if (module.AssemblyReferences.Any(a => (a.Name is "mscorlib" or "System") && a.Version.Major == 2))
            throw new PreparationException("Silverlight 2.0.5.0 runtime reference remains.");
        foreach (var method in AllTypes(module).SelectMany(t => t.Methods).Where(m => m.HasBody))
            foreach (var instruction in method.Body.Instructions)
                if (instruction.Operand is MemberReference or TypeReference)
                    _ = instruction.Operand.ToString();
    }

    private static bool ReferencesRetargetedOperand(object? operand, HashSet<int> tokens) => operand switch
    {
        TypeReference type => ReferencesRetargetedType(type, tokens),
        MethodReference method => ReferencesRetargetedType(method.DeclaringType, tokens) ||
            ReferencesRetargetedType(method.ReturnType, tokens) ||
            method.Parameters.Any(p => ReferencesRetargetedType(p.ParameterType, tokens)) ||
            (method is GenericInstanceMethod instance &&
                instance.GenericArguments.Any(t => ReferencesRetargetedType(t, tokens))),
        FieldReference field => ReferencesRetargetedType(field.DeclaringType, tokens) ||
            ReferencesRetargetedType(field.FieldType, tokens),
        _ => false
    };

    private static string OperandTarget(object operand) => operand switch
    {
        TypeReference type => $"{ScopeName(type.GetElementType())}:{type.FullName}",
        MemberReference member => $"{ScopeName(member.DeclaringType.GetElementType())}:{member.FullName}",
        _ => throw new PreparationException("Unexpected retargeted IL operand.")
    };

    private static bool ReferencesRetargetedType(TypeReference type, HashSet<int> tokens)
    {
        if (tokens.Contains(type.MetadataToken.ToInt32())) return true;
        if (type is TypeSpecification specification)
            return ReferencesRetargetedType(specification.ElementType, tokens) ||
                (type is GenericInstanceType generic && generic.GenericArguments.Any(t => ReferencesRetargetedType(t, tokens)));
        return false;
    }

    private static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module)
    {
        foreach (var type in module.Types)
            foreach (var nested in Walk(type)) yield return nested;
        static IEnumerable<TypeDefinition> Walk(TypeDefinition type)
        {
            yield return type;
            foreach (var child in type.NestedTypes)
                foreach (var nested in Walk(child)) yield return nested;
        }
    }
    private static bool SameTypeName(string a, string b) => a.Replace('/', '+') == b.Replace('/', '+');
    private static string ScopeName(TypeReference type) => type.Scope is AssemblyNameReference a ? a.Name : type.Scope.Name;
    private static bool SameAssembly(IMetadataScope scope, AssemblyNameReference target) =>
        scope is AssemblyNameReference a && a.Name == target.Name && a.Version == target.Version &&
        a.Culture == target.Culture && a.PublicKeyToken.SequenceEqual(target.PublicKeyToken);
    private static string Hash(string path) => Hex(SHA256.HashData(File.ReadAllBytes(path)));
    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
}

public sealed record TypeRetarget(int Token, string Source, string SourceScope, string TargetAssembly);
public sealed record MemberAnchor(int Token, string Parent, string Name, string ExactSignature);
public sealed record RewritePlan(string SourceSha256, IReadOnlyList<TypeRetarget> Types,
    IReadOnlyList<MemberAnchor> Members, IReadOnlyDictionary<string, string> TargetAssemblies, string Description);
public sealed record RewriteResult(string OutputDirectory, string PreparedSha256);
public sealed record PatchRecord(string Kind, string SourceSha256, string TargetSignature,
    string? ExpectedBodySha256, string NewTarget, string Postcondition);
public sealed record PatchManifest(int SchemaVersion, string ToolVersion, string SourceSha256,
    string PreparedSha256, string Plan, PatchRecord[] Patches);
