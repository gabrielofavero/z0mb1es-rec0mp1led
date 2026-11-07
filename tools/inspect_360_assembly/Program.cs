using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length is not (1 or 3) || (args.Length == 3 && args[1] != "--output"))
{
    Console.Error.WriteLine("Usage: inspect_360_assembly <private/extracted/game.exe> [--output <ignored/report.json>]");
    return 2;
}

try
{
    var bytes = File.ReadAllBytes(args[0]);
    using var pe = new PEReader(new MemoryStream(bytes));
    if (!pe.HasMetadata || pe.PEHeaders.CorHeader is null)
        throw new BadImageFormatException("Input has no managed .NET metadata.");
    var md = pe.GetMetadataReader();
    var names = new MetadataNames(md);
    var assembly = md.GetAssemblyDefinition();
    var methods = md.MethodDefinitions.Select(handle =>
    {
        var method = md.GetMethodDefinition(handle);
        var il = method.RelativeVirtualAddress == 0
            ? null : pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();
        return new
        {
            token = $"0x{MetadataTokens.GetToken(handle):x8}",
            rva = method.RelativeVirtualAddress,
            type = names.Type(method.GetDeclaringType()),
            name = md.GetString(method.Name),
            signature = names.MethodSignature(method.DecodeSignature(names, null)),
            ilSha256 = il is null ? null : Convert.ToHexString(SHA256.HashData(il)).ToLowerInvariant()
        };
    }).ToArray();
    var refs = md.AssemblyReferences.Select(handle =>
    {
        var reference = md.GetAssemblyReference(handle);
        return new { name = md.GetString(reference.Name), version = reference.Version.ToString() };
    }).ToArray();
    var serviceReferences = md.MemberReferences.Select(handle =>
    {
        var reference = md.GetMemberReference(handle);
        return new { parent = names.Entity(reference.Parent), name = md.GetString(reference.Name) };
    }).Where(row => row.parent.Contains("GamerServices", StringComparison.OrdinalIgnoreCase) ||
                    row.parent.Contains("Achievement", StringComparison.OrdinalIgnoreCase) ||
                    row.name.Contains("Achievement", StringComparison.OrdinalIgnoreCase)).ToArray();
    var report = new
    {
        schemaVersion = 1,
        input = new { name = Path.GetFileName(args[0]), bytes = bytes.Length,
            sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() },
        assembly = new { name = md.GetString(assembly.Name), version = assembly.Version.ToString(),
            mvid = md.GetGuid(md.GetModuleDefinition().Mvid).ToString(),
            entryPointToken = $"0x{pe.PEHeaders.CorHeader.EntryPointTokenOrRelativeVirtualAddress:x8}",
            metadataVersion = md.MetadataVersion },
        counts = new { types = md.TypeDefinitions.Count, methods = md.MethodDefinitions.Count,
            assemblyRefs = md.AssemblyReferences.Count, typeRefs = md.TypeReferences.Count,
            memberRefs = md.MemberReferences.Count, resources = md.ManifestResources.Count },
        assemblyRefs = refs, serviceReferences, methods
    };
    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n";
    if (args.Length == 3)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
        File.WriteAllText(args[2], json);
        Console.WriteLine($"ASSEMBLY_INVENTORY_OK {report.assembly.name} methods={report.counts.methods} serviceReferences={serviceReferences.Length}");
    }
    else Console.Write(json);
    return 0;
}
catch (Exception error) when (error is IOException or BadImageFormatException or InvalidOperationException)
{
    Console.Error.WriteLine($"ASSEMBLY_INVENTORY_FAIL {error.GetType().Name}: {error.Message}");
    return 1;
}
