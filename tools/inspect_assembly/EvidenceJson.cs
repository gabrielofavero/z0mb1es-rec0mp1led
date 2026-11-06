using System.IO.Compression;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

internal static class EvidenceJson
{
    public static int Run(string[] args)
    {
        try { return RunChecked(args); }
        catch (Exception e) when (e is IOException or InvalidDataException or BadImageFormatException or
            JsonException or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine($"inspection failed: {e.Message}");
            return 1;
        }
    }

    private static int RunChecked(string[] args)
    {
        if (args.Length is not (2 or 4) || args[0] is not ("--dll" or "--xap") ||
            (args.Length == 4 && args[2] != "--output"))
        {
            Console.Error.WriteLine("Usage: --json (--dll <original.dll> | --xap <original.xap>) [--output <report.json>]");
            return 2;
        }
        using var manifest = JsonDocument.Parse(File.ReadAllText("config/supported-versions.json"));
        var supported = manifest.RootElement.GetProperty("versions")[0];
        var xapHash = supported.GetProperty("xapSha256").GetString()!;
        var dllHash = supported.GetProperty("dllSha256").GetString()!;
        byte[] bytes;
        int? xapEntries = null;
        long? xapUncompressedBytes = null;
        if (args[0] == "--xap")
        {
            Check("XAP", Hash(File.ReadAllBytes(args[1])), xapHash);
            using var zip = ZipFile.OpenRead(args[1]);
            xapEntries = zip.Entries.Count;
            xapUncompressedBytes = zip.Entries.Sum(e => e.Length);
            if (xapEntries != supported.GetProperty("xapEntries").GetInt32() ||
                xapUncompressedBytes != supported.GetProperty("xapUncompressedBytes").GetInt64())
                throw new InvalidDataException("Supported XAP has unexpected entry count or uncompressed size");
            var entry = zip.Entries.SingleOrDefault(e => e.FullName == "ZombiesWP7.dll")
                ?? throw new InvalidDataException("ZombiesWP7.dll missing from supported XAP");
            using var input = entry.Open();
            using var buffer = new MemoryStream();
            input.CopyTo(buffer);
            bytes = buffer.ToArray();
        }
        else bytes = File.ReadAllBytes(args[1]);
        Check("DLL", Hash(bytes), dllHash);
        using var pe = new PEReader(new MemoryStream(bytes));
        if (!pe.HasMetadata) throw new BadImageFormatException("No CLI metadata");
        var md = pe.GetMetadataReader();
        var n = new MetadataNames(md);
        var assembly = md.GetAssemblyDefinition();
        if (md.GetString(assembly.Name) != "ZombiesWP7" || assembly.Version.ToString() != "1.0.0.0")
            throw new InvalidDataException("Unexpected assembly identity");

        var assemblyRefs = md.AssemblyReferences.Select(h =>
        {
            var r = md.GetAssemblyReference(h);
            return new { token = MetadataTokens.GetToken(h), name = md.GetString(r.Name), version = r.Version.ToString(),
                culture = r.Culture.IsNil ? "" : md.GetString(r.Culture),
                publicKeyOrToken = Convert.ToHexString(md.GetBlobBytes(r.PublicKeyOrToken)).ToLowerInvariant() };
        }).ToArray();
        var typeRefs = md.TypeReferences.Select(h =>
        {
            var r = md.GetTypeReference(h);
            return new { token = MetadataTokens.GetToken(h), name = n.Type(h), scope = n.Scope(r.ResolutionScope) };
        }).ToArray();
        var memberRefs = md.MemberReferences.Select(h =>
        {
            var r = md.GetMemberReference(h);
            return new { token = MetadataTokens.GetToken(h), parent = n.Entity(r.Parent), name = md.GetString(r.Name),
                kind = r.GetKind().ToString(), signature = n.MemberSignature(r) };
        }).ToArray();
        var inheritance = md.TypeDefinitions.Select(h =>
        {
            var t = md.GetTypeDefinition(h);
            return new { type = n.Type(h), @base = t.BaseType.IsNil ? null : n.Entity(t.BaseType),
                interfaces = t.GetInterfaceImplementations().Select(i => n.Entity(md.GetInterfaceImplementation(i).Interface)).ToArray(),
                overrides = t.GetMethodImplementations().Select(i =>
                {
                    var m = md.GetMethodImplementation(i);
                    return new { body = n.Member(m.MethodBody), declaration = n.Member(m.MethodDeclaration) };
                }).ToArray(),
                virtualReuseSlotCandidates = t.GetMethods().Where(mh =>
                {
                    var flags = md.GetMethodDefinition(mh).Attributes;
                    return flags.HasFlag(System.Reflection.MethodAttributes.Virtual) &&
                        !flags.HasFlag(System.Reflection.MethodAttributes.NewSlot);
                }).Select(mh => n.Member(mh)).ToArray() };
        }).ToArray();
        var methods = md.MethodDefinitions.Select(h =>
        {
            var m = md.GetMethodDefinition(h);
            var virtualSlot = m.Attributes.HasFlag(System.Reflection.MethodAttributes.Virtual)
                ? (m.Attributes.HasFlag(System.Reflection.MethodAttributes.NewSlot) ? "new-slot" : "reuse-slot") : "nonvirtual";
            return new { token = MetadataTokens.GetToken(h), declaringType = n.Type(m.GetDeclaringType()),
                name = md.GetString(m.Name), signature = n.MethodSignature(m.DecodeSignature(n, null)),
                attributes = m.Attributes.ToString(), virtualSlot, bodyRva = m.RelativeVirtualAddress };
        }).ToArray();
        var calls = new List<object>();
        var assetCandidates = new List<object>();
        foreach (var h in md.MethodDefinitions)
        {
            var m = md.GetMethodDefinition(h);
            if (m.RelativeVirtualAddress == 0) continue;
            var instructions = ReadIl(pe.GetMethodBody(m.RelativeVirtualAddress).GetILBytes()
                ?? throw new BadImageFormatException("Missing IL bytes for a method body"));
            var source = $"{n.Type(m.GetDeclaringType())}::{md.GetString(m.Name)} {n.MethodSignature(m.DecodeSignature(n, null))}";
            var loadsContent = instructions.Any(i => i.opcode is "call" or "callvirt" && i.token is int token &&
                n.TokenMember(token).Contains("ContentManager::Load", StringComparison.Ordinal));
            foreach (var i in instructions)
            {
                if (i.opcode is "call" or "callvirt" or "newobj" or "ldftn" or "ldvirtftn" && i.token is int targetToken)
                {
                    var target = n.TokenMember(targetToken);
                    calls.Add(new { source, ilOffset = i.offset, opcode = i.opcode, targetToken, target,
                        adaptationCandidate = n.NeedsAdaptation(targetToken) });
                }
                if (i.opcode == "ldstr" && i.token is int stringToken)
                {
                    var literal = md.GetUserString(MetadataTokens.UserStringHandle(stringToken & 0xFFFFFF));
                    if (loadsContent || literal.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase) ||
                        literal.EndsWith(".wma", StringComparison.OrdinalIgnoreCase) || literal.Contains('/') || literal.Contains('\\'))
                        assetCandidates.Add(new { source, ilOffset = i.offset, literal,
                            basis = loadsContent ? "method calls ContentManager.Load" : "path or asset extension" });
                }
            }
        }
        var report = new { schemaVersion = 1, supportedVersion = supported.GetProperty("id").GetString(),
            input = new { xapSha256 = xapHash, dllSha256 = dllHash, dllBytes = bytes.Length,
                source = args[0] == "--xap" ? "verified XAP" : "verified extracted DLL",
                xapEntries, xapUncompressedBytes },
            assembly = new { name = md.GetString(assembly.Name), version = assembly.Version.ToString(),
                mvid = md.GetGuid(md.GetModuleDefinition().Mvid).ToString() },
            counts = new { types = md.TypeDefinitions.Count, methods = md.MethodDefinitions.Count,
                assemblyRefs = assemblyRefs.Length, typeRefs = typeRefs.Length, memberRefs = memberRefs.Length,
                calls = calls.Count, assetCandidates = assetCandidates.Count },
            assemblyRefs, typeRefs, memberRefs, inheritance, methods, calls, assetCandidates };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n";
        if (args.Length == 2) Console.Write(json);
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[3]))!);
            File.WriteAllText(args[3], json);
            Console.WriteLine($"Wrote {args[3]} (SHA-256 {Hash(System.Text.Encoding.UTF8.GetBytes(json))})");
        }
        return 0;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static void Check(string kind, string actual, string expected)
    {
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Unsupported {kind}: SHA-256 {actual}; expected {expected}");
    }

    private static List<(int offset, string opcode, int? token)> ReadIl(byte[] il)
    {
        var opcodes = typeof(OpCodes).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!)
            .ToDictionary(o => unchecked((ushort)o.Value));
        var result = new List<(int, string, int?)>();
        var bytes = il.AsSpan();
        for (var p = 0; p < bytes.Length;)
        {
            var offset = p;
            ushort code = bytes[p++];
            if (code == 0xFE) code = (ushort)(0xFE00 | bytes[p++]);
            if (!opcodes.TryGetValue(code, out var op)) throw new BadImageFormatException($"Unknown IL opcode 0x{code:X4}");
            var width = op.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.ShortInlineR or OperandType.InlineI or OperandType.InlineBrTarget or OperandType.InlineField or
                    OperandType.InlineMethod or OperandType.InlineSig or OperandType.InlineString or OperandType.InlineTok or
                    OperandType.InlineType => 4,
                OperandType.InlineSwitch => 4 + 4 * System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(p, 4)),
                _ => throw new BadImageFormatException($"Unknown operand type {op.OperandType}")
            };
            if (p + width > bytes.Length) throw new BadImageFormatException("Truncated IL operand");
            int? token = op.OperandType is OperandType.InlineMethod or OperandType.InlineString
                ? System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(p, 4)) : null;
            p += width;
            result.Add((offset, op.Name ?? "?", token));
        }
        return result;
    }
}
