using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/inspect_assembly -- <assembly.dll> [replacement.dll]");
    return 2;
}

using var stream = File.OpenRead(args[0]);
using var pe = new PEReader(stream);
if (!pe.HasMetadata)
{
    Console.Error.WriteLine("File has no .NET metadata.");
    return 1;
}

var md = pe.GetMetadataReader();
var asm = md.GetAssemblyDefinition();
Console.WriteLine($"Assembly: {md.GetString(asm.Name)} {asm.Version}");
Console.WriteLine($"Types: {md.TypeDefinitions.Count}; methods: {md.MethodDefinitions.Count}");
Console.WriteLine("Potential integration anchors:");
foreach (var handle in md.TypeDefinitions)
{
    var type = md.GetTypeDefinition(handle);
    var name = md.GetString(type.Name);
    if (name is not ("Game1" or "InputMgr" or "StorageMgr" or "AchievementMgr" or "TrialMgr" or "Music" or "CustomMgr"))
        continue;
    Console.WriteLine($"  {md.GetString(type.Namespace)}.{name}: {string.Join(", ", type.GetMethods().Select(method => md.GetString(md.GetMethodDefinition(method).Name)))}");
}
Console.WriteLine("Assembly references:");
foreach (var handle in md.AssemblyReferences)
{
    var reference = md.GetAssemblyReference(handle);
    Console.WriteLine($"  {md.GetString(reference.Name)} {reference.Version}");
}

Console.WriteLine("External type references by assembly:");
foreach (var group in md.TypeReferences
    .Select(handle => md.GetTypeReference(handle))
    .GroupBy(type => ScopeName(md, type.ResolutionScope))
    .OrderBy(group => group.Key))
{
    Console.WriteLine($"  {group.Key} ({group.Count()} types)");
    foreach (var type in group.OrderBy(type => md.GetString(type.Namespace)).ThenBy(type => md.GetString(type.Name)))
        Console.WriteLine($"    {md.GetString(type.Namespace)}.{md.GetString(type.Name)}");
}
if (args.Length == 2)
{
    using var replacementStream = File.OpenRead(args[1]);
    using var replacementPe = new PEReader(replacementStream);
    var replacement = replacementPe.GetMetadataReader();
    var types = replacement.TypeDefinitions
        .Select(handle => replacement.GetTypeDefinition(handle))
        .Select(type => $"{replacement.GetString(type.Namespace)}.{replacement.GetString(type.Name)}")
        .ToHashSet();
    Console.WriteLine($"Missing XNA types in {Path.GetFileName(args[1])}:");
    foreach (var type in md.TypeReferences.Select(handle => md.GetTypeReference(handle))
        .Where(type => ScopeName(md, type.ResolutionScope).StartsWith("Microsoft.Xna.Framework"))
        .Where(type => !types.Contains($"{md.GetString(type.Namespace)}.{md.GetString(type.Name)}")))
        Console.WriteLine($"  {ScopeName(md, type.ResolutionScope)}: {md.GetString(type.Namespace)}.{md.GetString(type.Name)}");
}
return 0;

static string ScopeName(MetadataReader md, EntityHandle handle) => handle.Kind switch
{
    HandleKind.AssemblyReference => md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)handle).Name),
    HandleKind.ModuleReference => md.GetString(md.GetModuleReference((ModuleReferenceHandle)handle).Name),
    HandleKind.TypeReference => ScopeName(md, md.GetTypeReference((TypeReferenceHandle)handle).ResolutionScope),
    _ => handle.Kind.ToString()
};
