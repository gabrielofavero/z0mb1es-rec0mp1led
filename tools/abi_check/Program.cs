using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

return AbiCheck.Run(args);

internal static class AbiCheck
{
    private sealed record Resolution(string Category, string Status, string Reason, TargetType? Target,
        string? ProposedTarget = null);

    public static int Run(string[] args)
    {
        if (args.Length == 2 && args[0] == "--self-test") return AbiFixtureChecks.Run(args[1]);
        var withBridge = args.Length == 10 && args[6] == "--bridge" && args[8] == "--output";
        if ((args.Length != 8 && !withBridge) || args[0] != "--inventory" || args[2] != "--dll" ||
            args[4] != "--fna" || (args.Length == 8 && args[6] != "--output"))
        {
            Console.Error.WriteLine("Usage: abi_check --inventory <Prompt1.json> --dll <original.dll> --fna <pinned FNA.dll> [--bridge <Port.Bridge.dll>] --output <report.json>");
            return 2;
        }
        try { return Check(args[1], args[3], args[5], withBridge ? args[7] : null, withBridge ? args[9] : args[7]); }
        catch (Exception e) when (e is IOException or InvalidDataException or BadImageFormatException or
            JsonException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            Console.Error.WriteLine("ABI check failed: " + e.Message);
            return 1;
        }
    }

    private static int Check(string inventoryPath, string dllPath, string fnaPath, string? bridgePath, string outputPath)
    {
        using var versions = JsonDocument.Parse(File.ReadAllText("config/supported-versions.json"));
        var supported = versions.RootElement.GetProperty("versions")[0];
        var expectedHash = supported.GetProperty("dllSha256").GetString()!;
        var dllBytes = File.ReadAllBytes(dllPath);
        var actualHash = Hash(dllBytes);
        if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Unsupported original DLL SHA-256 {actualHash}");
        using var inventory = JsonDocument.Parse(File.ReadAllText(inventoryPath));
        var root = inventory.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
            root.GetProperty("supportedVersion").GetString() != supported.GetProperty("id").GetString() ||
            root.GetProperty("input").GetProperty("dllSha256").GetString() != actualHash ||
            root.GetProperty("input").GetProperty("xapSha256").GetString() != supported.GetProperty("xapSha256").GetString())
            throw new InvalidDataException("Prompt 1 inventory does not identify the verified original DLL");
        using var pe = new PEReader(new MemoryStream(dllBytes));
        var md = pe.GetMetadataReader();
        var n = new MetadataNames(md);
        var exact = new ExactNames(md);
        VerifyInventory(root, md, n);
        var typeKindUses = AuditSignatureTypeKinds(md);
        using var catalog = new TargetCatalog(fnaPath, bridgePath);
        var typeResults = new Dictionary<int, Resolution>();
        var types = root.GetProperty("typeRefs").EnumerateArray().Select(row =>
        {
            var token = row.GetProperty("token").GetInt32();
            var name = row.GetProperty("name").GetString()!;
            var scope = row.GetProperty("scope").GetString()!;
            var result = ResolveType(name, scope, catalog, bridgePath is not null);
            var uses = typeKindUses.GetValueOrDefault(token) ?? [];
            if (result.Target is not null && uses.Any(kind =>
                kind is not (0x11 or 0x12) || (kind == 0x11) != result.Target.IsValueType))
                result = new("unresolved", "unresolved", "CLI class/valuetype signature kind differs from target definition",
                    result.Target, result.ProposedTarget ?? "guarded type rewrite");
            typeResults.Add(token, result);
            return new { token, source = name, scope, category = result.Category, status = result.Status,
                targetAssembly = result.Target?.Assembly.Name, target = result.Target?.Name,
                signatureKinds = uses.Order().Select(v => v == 0x11 ? "valuetype" : v == 0x12 ? "class" : $"unknown-{v:x2}").ToArray(),
                result.Reason, result.ProposedTarget };
        }).ToArray();

        var sourceTypeRefs = md.TypeReferences.ToDictionary(h => n.Type(h), h => MetadataTokens.GetToken(h), StringComparer.Ordinal);
        var sourceDefinitions = md.TypeDefinitions.Select(n.Type).ToHashSet(StringComparer.Ordinal);
        var members = root.GetProperty("memberRefs").EnumerateArray().Select(row =>
        {
            var token = row.GetProperty("token").GetInt32();
            var m = md.GetMemberReference((MemberReferenceHandle)MetadataTokens.EntityHandle(token));
            var name = md.GetString(m.Name);
            var parentName = n.Entity(m.Parent);
            var kind = m.GetKind();
            var signature = n.MemberSignature(m);
            var exactSignature = exact.Member(m);
            var runtimeSemanticRewrite = parentName == "System.IO.IsolatedStorage.IsolatedStorageFile" &&
                name == "GetUserStoreForApplication";
            var parent = ParentResolution(m.Parent, md, n, sourceTypeRefs, sourceDefinitions, typeResults);
            bool? arrayIntrinsic = m.Parent.Kind == HandleKind.TypeSpecification && parentName.EndsWith("[,]", StringComparison.Ordinal)
                ? VerifyArrayIntrinsic(m, md, n, parentName)
                : null;
            var candidates = new List<string>();
            TargetType? matchedType = null;
            if (parent.Status == "verified" && parent.Target is not null)
            {
                foreach (var target in WalkBases(parent.Target, catalog))
                {
                    foreach (var candidate in target.Members(name, kind))
                    {
                        candidates.Add($"{target.Assembly.Name}:{target.Name} {candidate.Display}");
                        if (candidate.Signature == exactSignature) { matchedType = target; break; }
                    }
                    if (matchedType is not null) break;
                }
            }
            var eventVerified = kind == MemberReferenceKind.Method &&
                (name.StartsWith("add_", StringComparison.Ordinal) || name.StartsWith("remove_", StringComparison.Ordinal))
                ? matchedType?.Events().Any(e =>
                    (name.StartsWith("add_", StringComparison.Ordinal) ? e.Add : e.Remove) == name &&
                    e.Name == name[4..])
                : null;
            var status = arrayIntrinsic == true ? "verified" : arrayIntrinsic == false ? "unresolved" :
                parent.Status == "internal" ? "internal" :
                matchedType is null ? "unresolved" : eventVerified == false ? "unresolved" : "verified";
            var reason = status switch
            {
                "internal" => "Member parent is defined in the original assembly",
                _ when arrayIntrinsic == true => "Verified rank-two CLI array intrinsic signature",
                _ when arrayIntrinsic == false => "Invalid rank-two CLI array intrinsic signature",
                "verified" => matchedType == parent.Target
                    ? "Exact metadata signature found on target type"
                    : "Exact metadata signature found on target base type",
                _ when parent.Status != "verified" => parent.Reason,
                _ when eventVerified == false => "Accessor signature exists, but matching event metadata is absent",
                _ when candidates.Count == 0 => "No target member with this name and kind",
                _ => "Target member name exists, but full signature differs"
            };
            return new { token, parent = parentName, name, kind = kind.ToString(), signature, exactSignature,
                category = arrayIntrinsic == true ? ".NET runtime" : status == "verified" ? parent.Category : status == "internal" ? "internal" : "unresolved",
                status, targetAssembly = arrayIntrinsic == true ? "System.Private.CoreLib" : matchedType?.Assembly.Name,
                targetType = arrayIntrinsic == true ? parentName : matchedType?.Name,
                eventVerified, candidates = candidates.Take(12).ToArray(), reason,
                semanticStatus = runtimeSemanticRewrite ? "rewrite-required" : "signature-only",
                proposedTarget = runtimeSemanticRewrite ? "Port.Bridge desktop save service" : status == "unresolved" ?
                    arrayIntrinsic == false ? "runtime adaptation" : parent.ProposedTarget ??
                    (parent.Category == "FNA" ? "Port.Bridge or guarded rewrite" : "runtime adaptation") : null };
        }).ToArray();

        var inheritance = md.TypeDefinitions.Select(h =>
        {
            var t = md.GetTypeDefinition(h);
            var owner = n.Type(h);
            var relationships = new List<object>();
            void Add(EntityHandle reference, string kind)
            {
                if (reference.IsNil) return;
                var r = ParentResolution(reference, md, n, sourceTypeRefs, sourceDefinitions, typeResults);
                var shapeValid = r.Target is null || (kind == "interface") == r.Target.IsInterface;
                relationships.Add(new { kind, source = n.Entity(reference), category = shapeValid ? r.Category : "unresolved",
                    status = shapeValid ? r.Status : "unresolved", targetAssembly = r.Target?.Assembly.Name,
                    reason = shapeValid ? r.Reason : "Target class/interface kind differs" });
            }
            Add(t.BaseType, "base");
            foreach (var h2 in t.GetInterfaceImplementations())
                Add(md.GetInterfaceImplementation(h2).Interface, "interface");
            return new { type = owner, relationships };
        }).ToArray();

        var virtualSlots = md.MethodDefinitions.Select(h => (Handle: h, Method: md.GetMethodDefinition(h)))
            .Where(x => x.Method.Attributes.HasFlag(MethodAttributes.Virtual) &&
                !x.Method.Attributes.HasFlag(MethodAttributes.NewSlot))
            .Select(x =>
            {
                var signature = exact.Method(x.Method);
                var name = md.GetString(x.Method.Name);
                var owner = md.GetTypeDefinition(x.Method.GetDeclaringType());
                var baseEntity = owner.BaseType;
                var internalSlot = false;
                while (baseEntity.Kind == HandleKind.TypeDefinition)
                {
                    var baseType = md.GetTypeDefinition((TypeDefinitionHandle)baseEntity);
                    if (baseType.GetMethods().Any(mh =>
                    {
                        var bm = md.GetMethodDefinition(mh);
                        return md.GetString(bm.Name) == name && exact.Method(bm) == signature &&
                            bm.Attributes.HasFlag(MethodAttributes.Virtual);
                    })) { internalSlot = true; break; }
                    baseEntity = baseType.BaseType;
                }
                var baseResolution = internalSlot ? null :
                    ParentResolution(baseEntity, md, n, sourceTypeRefs, sourceDefinitions, typeResults);
                var found = baseResolution?.Target is not null && WalkBases(baseResolution.Target, catalog)
                    .Any(t => t.Definition.GetMethods().Any(mh =>
                    {
                        var m = t.Assembly.Reader.GetMethodDefinition(mh);
                        return t.Assembly.Reader.GetString(m.Name) == name &&
                            t.Assembly.Exact.Method(m) == signature && m.Attributes.HasFlag(MethodAttributes.Virtual);
                    }));
                return new { token = MetadataTokens.GetToken(x.Handle), owner = n.Type(x.Method.GetDeclaringType()),
                    name, signature, status = internalSlot ? "internal" : found ? "verified" : "unresolved",
                    reason = internalSlot ? "Exact virtual slot exists in original assembly" : found ?
                        "Exact virtual slot exists on target base chain" : "No exact virtual slot found on target base chain" };
            }).ToArray();

        var memberStatuses = members.ToDictionary(x => x.token, x => x.status);
        var methodSpecs = Enumerable.Range(1, md.GetTableRowCount(TableIndex.MethodSpec))
            .Select(MetadataTokens.MethodSpecificationHandle).Select(h =>
        {
            var s = md.GetMethodSpecification(h);
            var arguments = s.DecodeSignature(n, null).ToArray();
            var genericArity = s.Method.Kind switch
            {
                HandleKind.MemberReference => md.GetMemberReference((MemberReferenceHandle)s.Method).DecodeMethodSignature(n, null).GenericParameterCount,
                HandleKind.MethodDefinition => md.GetMethodDefinition((MethodDefinitionHandle)s.Method).DecodeSignature(n, null).GenericParameterCount,
                _ => -1
            };
            var underlyingStatus = s.Method.Kind == HandleKind.MemberReference
                ? memberStatuses[MetadataTokens.GetToken(s.Method)] : "internal";
            var status = genericArity == arguments.Length && underlyingStatus != "unresolved" ? "verified" : "unresolved";
            return new { token = MetadataTokens.GetToken(h), method = n.Member(s.Method),
                arguments,
                exactArguments = s.DecodeSignature(exact, null).ToArray(),
                underlyingMemberToken = MetadataTokens.GetToken(s.Method), status,
                reason = status == "verified" ? "Generic argument count matches resolved method arity" :
                    "Generic method instantiation has unresolved method or wrong argument count" };
        }).ToArray();
        var sourceEvents = md.EventDefinitions.Select(h =>
        {
            var e = md.GetEventDefinition(h);
            return new { token = MetadataTokens.GetToken(h), name = md.GetString(e.Name), type = n.Entity(e.Type) };
        }).ToArray();
        var sourceFields = md.FieldDefinitions.Select(h =>
        {
            var f = md.GetFieldDefinition(h);
            return new { token = MetadataTokens.GetToken(h), owner = n.Type(f.GetDeclaringType()),
                name = md.GetString(f.Name), signature = f.DecodeSignature(n, null), exactSignature = exact.Field(f) };
        }).ToArray();
        var sourceProperties = md.PropertyDefinitions.Select(h =>
        {
            var p = md.GetPropertyDefinition(h);
            var s = p.DecodeSignature(exact, null);
            return new { token = MetadataTokens.GetToken(h), name = md.GetString(p.Name),
                signature = exact.Method(s) };
        }).ToArray();
        var assemblyRefs = root.GetProperty("assemblyRefs").EnumerateArray().Select(row =>
        {
            var name = row.GetProperty("name").GetString()!;
            var rewrite = name switch
            {
                "mscorlib" or "System" => "Retarget 2.0.5.0 Silverlight identity to verified .NET 10 implementation assemblies; review isolated storage calls",
                "Microsoft.Phone" or "Microsoft.Devices.Sensors" => "Retarget phone-only types and members to authored Port.Bridge shims",
                "Microsoft.Xna.Framework.GamerServices" => "Split core XNA types to FNA and missing GamerServices types to Port.Bridge",
                _ => "Retarget XNA 4.0 identity and all dependent metadata signatures to pinned FNA"
            };
            return new { token = row.GetProperty("token").GetInt32(), name,
                version = row.GetProperty("version").GetString(),
                target = name switch
                {
                    "mscorlib" or "System" => ".NET 10 runtime assemblies",
                    "Microsoft.Phone" or "Microsoft.Devices.Sensors" => "Port.Bridge",
                    "Microsoft.Xna.Framework.GamerServices" => "FNA + Port.Bridge",
                    _ => "FNA"
                }, rewrite };
        }).ToArray();
        var requiredActions = types.Where(x => x.status == "unresolved")
            .Select(x => (object)new { kind = "type-shim-or-rewrite", token = x.token,
                source = x.source, target = x.ProposedTarget, reason = x.Reason })
            .Concat(members.Where(x => x.status == "unresolved")
                .Select(x => (object)new { kind = "member-shim-or-rewrite", token = x.token,
                    source = x.parent + "::" + x.name + " " + x.signature,
                    target = x.proposedTarget, reason = x.reason }))
            .Concat(members.Where(x => x.semanticStatus == "rewrite-required")
                .Select(x => (object)new { kind = "runtime-behavior-rewrite", token = x.token,
                    source = x.parent + "::" + x.name + " " + x.signature,
                    target = x.proposedTarget, reason = "Application-scoped isolated storage lacks desktop application identity" }))
            .ToArray();
        var unresolvedTypes = types.Count(x => x.status == "unresolved");
        var unresolvedMembers = members.Count(x => x.status == "unresolved");
        var unresolvedSlots = virtualSlots.Count(x => x.status == "unresolved");
        var unresolvedSpecs = methodSpecs.Count(x => x.status == "unresolved");
        var unresolvedInheritance = inheritance.Sum(x => x.relationships.Count(r =>
            ((dynamic)r).status == "unresolved"));
        var report = new { schemaVersion = 1, tool = bridgePath is null ? "abi_check 0.4.0" : "abi_check 0.5.0", supportedVersion = supported.GetProperty("id").GetString(),
            input = new { dllSha256 = actualHash, inventorySha256 = Hash(File.ReadAllBytes(inventoryPath)),
                fnaSha256 = Hash(File.ReadAllBytes(fnaPath)), fnaAssembly = "FNA",
                bridgeSha256 = bridgePath is null ? null : Hash(File.ReadAllBytes(bridgePath)),
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription },
            counts = new { assemblyRefs = assemblyRefs.Length, typeRefs = types.Length, memberRefs = members.Length,
                methodSpecs = methodSpecs.Length, sourceEvents = sourceEvents.Length,
                sourceFields = sourceFields.Length, sourceProperties = sourceProperties.Length,
                unresolvedTypes, unresolvedMembers, unresolvedSlots, unresolvedInheritance, unresolvedSpecs,
                runtimeSemanticRewrites = members.Count(x => x.semanticStatus == "rewrite-required"),
                requiredActions = requiredActions.Length },
            validation = unresolvedTypes + unresolvedMembers + unresolvedSlots + unresolvedInheritance + unresolvedSpecs +
                members.Count(x => x.semanticStatus == "rewrite-required") == 0 ? "pass" : "fail",
            runtimeBindings = ProbeRuntimeBindings(root.GetProperty("assemblyRefs")),
            runtimeHazards = new[] { new { member = "System.IO.IsolatedStorage.IsolatedStorageFile::GetUserStoreForApplication",
                issue = "Exact member exists, but desktop use requires a ClickOnce/Silverlight application identity; use an authored desktop save service",
                reference = "https://learn.microsoft.com/en-us/dotnet/api/system.io.isolatedstorage.isolatedstoragefile.getuserstoreforapplication" } },
            assemblyRefs, requiredActions, types, members, inheritance, virtualSlots, methodSpecs, sourceEvents,
            sourceFields, sourceProperties };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n";
        var output = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, json, new UTF8Encoding(false));
        Console.WriteLine($"ABI report: {output}");
        Console.WriteLine($"Type refs {types.Length}, member refs {members.Length}, unresolved: types {unresolvedTypes}, members {unresolvedMembers}, slots {unresolvedSlots}, inheritance {unresolvedInheritance}, method specs {unresolvedSpecs}");
        Console.WriteLine($"Validation: {report.validation}");
        return report.validation == "pass" ? 0 : 1;
    }

    private static Resolution ResolveType(string name, string scope, TargetCatalog catalog, bool withBridge)
    {
        var targetCategory = scope.StartsWith("Microsoft.Xna.Framework", StringComparison.Ordinal) ? "FNA" :
            scope is "mscorlib" or "System" ? "runtime" : "Port.Bridge";
        var targets = targetCategory == "Port.Bridge" ?
            (withBridge ? catalog.Find(name, "Port.Bridge").ToArray() : []) :
            catalog.Find(name, targetCategory).ToArray();
        if (targets.Length == 0 && withBridge && targetCategory == "FNA" &&
            name.StartsWith("Microsoft.Xna.Framework.GamerServices.", StringComparison.Ordinal))
            targets = catalog.Find(name, "Port.Bridge").ToArray();
        if (targets.Length == 1)
            return new(targets[0].Assembly.Category == "runtime" ? ".NET runtime" : targets[0].Assembly.Category, "verified",
                "Type definition found in the specified target category", targets[0]);
        var planned = targetCategory == "Port.Bridge" || name.StartsWith("Microsoft.Xna.Framework.GamerServices.", StringComparison.Ordinal)
            ? "Port.Bridge" : targetCategory == "FNA" ? "Port.Bridge or guarded rewrite" : "runtime adaptation";
        return new("unresolved", "unresolved", targets.Length > 1 ? "Ambiguous target type definitions: " + string.Join(", ", targets.Select(t => t.Assembly.Path)) :
            targetCategory == "Port.Bridge" ? "Phone-only type; bridge contract has not been implemented" :
            $"Type definition absent from {targetCategory}", null, planned);
    }

    private static Resolution ParentResolution(EntityHandle parent, MetadataReader md, MetadataNames n,
        Dictionary<string, int> typeRefs, HashSet<string> definitions, Dictionary<int, Resolution> results)
    {
        if (parent.IsNil) return new("internal", "internal", "No external parent", null);
        if (parent.Kind == HandleKind.TypeDefinition || parent.Kind == HandleKind.MethodDefinition)
            return new("internal", "internal", "Parent is defined in original assembly", null);
        var name = n.Entity(parent);
        if (parent.Kind == HandleKind.TypeSpecification) name = name.Split('<')[0];
        if (definitions.Contains(name)) return new("internal", "internal", "Parent is defined in original assembly", null);
        if (parent.Kind == HandleKind.TypeReference)
        {
            var token = MetadataTokens.GetToken(parent);
            if (results.TryGetValue(token, out var resolved)) return resolved;
        }
        if (typeRefs.TryGetValue(name, out var refToken) && results.TryGetValue(refToken, out var result)) return result;
        return new("unresolved", "unresolved", $"Cannot identify external parent type {name} from Prompt 1 type references", null);
    }

    private static IEnumerable<TargetType> WalkBases(TargetType start, TargetCatalog catalog)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (TargetType? t = start; t is not null && seen.Add(t.Assembly.Name + ":" + t.Name); t = catalog.FindBase(t))
            yield return t;
    }

    private static bool VerifyArrayIntrinsic(MemberReference member, MetadataReader reader, MetadataNames names, string parent)
    {
        if (member.GetKind() != MemberReferenceKind.Method) return false;
        var signature = member.DecodeMethodSignature(names, null);
        if (!signature.Header.IsInstance || signature.GenericParameterCount != 0) return false;
        var element = parent[..^3];
        var parameters = signature.ParameterTypes.ToArray();
        if (parameters.Length < 2 || parameters[0] != "Int32" || parameters[1] != "Int32") return false;
        return reader.GetString(member.Name) switch
        {
            ".ctor" => parameters.Length == 2 && signature.ReturnType == "Void",
            "Get" => parameters.Length == 2 && signature.ReturnType == element,
            "Set" => parameters.Length == 3 && parameters[2] == element && signature.ReturnType == "Void",
            "Address" => parameters.Length == 2 && signature.ReturnType == element + "&",
            _ => false
        };
    }

    private static object[] ProbeRuntimeBindings(JsonElement assemblyRows)
    {
        return assemblyRows.EnumerateArray().Where(row =>
            row.GetProperty("name").GetString() is "mscorlib" or "System").Select(row =>
        {
            var name = row.GetProperty("name").GetString()!;
            var version = row.GetProperty("version").GetString()!;
            var token = row.GetProperty("publicKeyOrToken").GetString()!;
            var identity = $"{name}, Version={version}, Culture=neutral, PublicKeyToken={token}";
            string? loaded = null;
            string? error = null;
            try { loaded = Assembly.Load(identity).FullName; }
            catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
            { error = ex.GetType().Name + ": " + ex.Message; }
            return (object)new { sourceIdentity = identity, observedAssembly = loaded, error,
                status = loaded is null ? "unbound" : loaded == identity ? "exact-bound" : "redirected-to-different-identity",
                conclusion = "The full 2.0.5.0 Silverlight identity still requires a guarded rewrite; this probe does not load the original game" };
        }).ToArray();
    }

    private static Dictionary<int, HashSet<byte>> AuditSignatureTypeKinds(MetadataReader md)
    {
        var kinds = new Dictionary<int, HashSet<byte>>();
        var exact = new ExactNames(md, (h, raw) =>
        {
            var token = MetadataTokens.GetToken(h);
            if (!kinds.TryGetValue(token, out var set)) kinds[token] = set = [];
            set.Add(raw);
        });
        foreach (var h in md.FieldDefinitions) md.GetFieldDefinition(h).DecodeSignature(exact, null);
        foreach (var h in md.MethodDefinitions) md.GetMethodDefinition(h).DecodeSignature(exact, null);
        foreach (var h in md.MemberReferences) exact.Member(md.GetMemberReference(h));
        foreach (var h in md.PropertyDefinitions) md.GetPropertyDefinition(h).DecodeSignature(exact, null);
        foreach (var h in Enumerable.Range(1, md.GetTableRowCount(TableIndex.TypeSpec)).Select(MetadataTokens.TypeSpecificationHandle))
            md.GetTypeSpecification(h).DecodeSignature(exact, null);
        foreach (var h in Enumerable.Range(1, md.GetTableRowCount(TableIndex.MethodSpec)).Select(MetadataTokens.MethodSpecificationHandle))
            md.GetMethodSpecification(h).DecodeSignature(exact, null);
        foreach (var h in Enumerable.Range(1, md.GetTableRowCount(TableIndex.StandAloneSig)).Select(MetadataTokens.StandaloneSignatureHandle))
        {
            var s = md.GetStandaloneSignature(h);
            var header = md.GetBlobReader(s.Signature).ReadByte();
            if ((header & 0x0F) == 0x07) s.DecodeLocalSignature(exact, null);
            else s.DecodeMethodSignature(exact, null);
        }
        return kinds;
    }

    private static void VerifyInventory(JsonElement root, MetadataReader md, MetadataNames n)
    {
        static void Count(JsonElement root, string name, int expected)
        {
            if (root.GetProperty(name).GetArrayLength() != expected)
                throw new InvalidDataException($"Prompt 1 inventory {name} count differs from original metadata");
        }
        Count(root, "assemblyRefs", md.AssemblyReferences.Count);
        Count(root, "typeRefs", md.TypeReferences.Count);
        Count(root, "memberRefs", md.MemberReferences.Count);
        static void TokenCoverage(JsonElement rows, IEnumerable<int> expected, string name)
        {
            var actual = rows.EnumerateArray().Select(row => row.GetProperty("token").GetInt32()).ToArray();
            if (actual.Distinct().Count() != actual.Length || !actual.ToHashSet().SetEquals(expected))
                throw new InvalidDataException($"Prompt 1 inventory {name} token coverage differs from original metadata");
        }
        TokenCoverage(root.GetProperty("assemblyRefs"), md.AssemblyReferences.Select(h => MetadataTokens.GetToken(h)), "assemblyRefs");
        TokenCoverage(root.GetProperty("typeRefs"), md.TypeReferences.Select(h => MetadataTokens.GetToken(h)), "typeRefs");
        TokenCoverage(root.GetProperty("memberRefs"), md.MemberReferences.Select(h => MetadataTokens.GetToken(h)), "memberRefs");
        foreach (var row in root.GetProperty("assemblyRefs").EnumerateArray())
        {
            var r = md.GetAssemblyReference((AssemblyReferenceHandle)MetadataTokens.EntityHandle(row.GetProperty("token").GetInt32()));
            if (md.GetString(r.Name) != row.GetProperty("name").GetString() ||
                r.Version.ToString() != row.GetProperty("version").GetString() ||
                (r.Culture.IsNil ? "" : md.GetString(r.Culture)) != row.GetProperty("culture").GetString() ||
                Convert.ToHexString(md.GetBlobBytes(r.PublicKeyOrToken)).ToLowerInvariant() !=
                    row.GetProperty("publicKeyOrToken").GetString())
                throw new InvalidDataException("Prompt 1 assembly reference differs from original metadata");
        }
        foreach (var row in root.GetProperty("typeRefs").EnumerateArray())
        {
            var r = md.GetTypeReference((TypeReferenceHandle)MetadataTokens.EntityHandle(row.GetProperty("token").GetInt32()));
            if (n.Type((TypeReferenceHandle)MetadataTokens.EntityHandle(row.GetProperty("token").GetInt32())) != row.GetProperty("name").GetString() ||
                n.Scope(r.ResolutionScope) != row.GetProperty("scope").GetString())
                throw new InvalidDataException("Prompt 1 type reference differs from original metadata");
        }
        foreach (var row in root.GetProperty("memberRefs").EnumerateArray())
        {
            var r = md.GetMemberReference((MemberReferenceHandle)MetadataTokens.EntityHandle(row.GetProperty("token").GetInt32()));
            if (n.Entity(r.Parent) != row.GetProperty("parent").GetString() ||
                md.GetString(r.Name) != row.GetProperty("name").GetString() ||
                r.GetKind().ToString() != row.GetProperty("kind").GetString() ||
                n.MemberSignature(r) != row.GetProperty("signature").GetString())
                throw new InvalidDataException("Prompt 1 member reference differs from original metadata");
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
