using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Port.Prepare;

if (args.Length != 1) throw new ArgumentException("Expected an ignored fixture output directory.");
var root = Path.GetFullPath(args[0]);
Directory.CreateDirectory(root);
var targetPath = Path.Combine(root, "Target.Fixture.dll");
var originalPath = Path.Combine(root, "Fixture.Input.dll");
using (var target = AssemblyDefinition.CreateAssembly(
           new AssemblyNameDefinition("Target.Fixture", new Version(1, 0)), "Target.Fixture", ModuleKind.Dll))
{
    var widget = new TypeDefinition("Fixture", "Widget", TypeAttributes.Public | TypeAttributes.Class,
        target.MainModule.TypeSystem.Object);
    target.MainModule.Types.Add(widget);
    var ctor = new MethodDefinition(".ctor", MethodAttributes.Public | MethodAttributes.SpecialName |
        MethodAttributes.RTSpecialName, target.MainModule.TypeSystem.Void);
    var objectCtor = target.MainModule.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!);
    ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
    ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, objectCtor));
    ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
    widget.Methods.Add(ctor);
    target.Write(targetPath);
}
using (var source = AssemblyDefinition.CreateAssembly(
           new AssemblyNameDefinition("Fixture.Input", new Version(1, 0)), "Fixture.Input", ModuleKind.Dll))
{
    var legacy = new AssemblyNameReference("Legacy.Fixture", new Version(1, 0));
    source.MainModule.AssemblyReferences.Add(legacy);
    var widget = new TypeReference("Fixture", "Widget", source.MainModule, legacy);
    var factory = new TypeDefinition("Fixture", "Factory", TypeAttributes.Public | TypeAttributes.Class,
        source.MainModule.TypeSystem.Object);
    source.MainModule.Types.Add(factory);
    var create = new MethodDefinition("Create", MethodAttributes.Public | MethodAttributes.Static, widget);
    var ctor = new MethodReference(".ctor", source.MainModule.TypeSystem.Void, widget) { HasThis = true };
    create.Body.Instructions.Add(Instruction.Create(OpCodes.Newobj, ctor));
    create.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
    factory.Methods.Add(create);
    source.Write(originalPath);
}

using var read = ModuleDefinition.ReadModule(originalPath);
var corePath = typeof(object).Assembly.Location;
var targets = new Dictionary<string, string>
{
    ["Target.Fixture"] = targetPath,
    ["System.Private.CoreLib"] = corePath
};
var types = read.GetTypeReferences().Select(t => new TypeRetarget(t.MetadataToken.ToInt32(),
    t.FullName, t.Scope.Name, t.Scope.Name == "Legacy.Fixture" ? "Target.Fixture" :
    "System.Private.CoreLib")).ToArray();
var members = read.GetMemberReferences().Select(m => new MemberAnchor(m.MetadataToken.ToInt32(),
    m.DeclaringType.FullName, m.Name, m.FullName)).ToArray();
var sourceHash = Hash(originalPath);
var plan = new RewritePlan(sourceHash, types, members, targets, "authored fixture");
var first = IlRewriter.Rewrite(originalPath, Path.Combine(root, "first"), plan);
var second = IlRewriter.Rewrite(originalPath, Path.Combine(root, "second"), plan);
if (first.PreparedSha256 != second.PreparedSha256 ||
    Hash(Path.Combine(root, "first", "patch-manifest.json")) !=
    Hash(Path.Combine(root, "second", "patch-manifest.json")))
    throw new Exception("Fixture rewrite was not deterministic.");
if (Hash(originalPath) != sourceHash) throw new Exception("Fixture original was modified.");
var tampered = Path.Combine(root, "tampered.dll");
using (var changed = ModuleDefinition.ReadModule(originalPath, new ReaderParameters { InMemory = true }))
{
    var method = changed.Types.Single(t => t.Name == "Factory").Methods.Single(m => m.Name == "Create");
    method.Body.Instructions[0] = Instruction.Create(OpCodes.Ldnull);
    changed.Write(tampered);
}
try
{
    IlRewriter.Rewrite(tampered, Path.Combine(root, "tampered-output"), plan);
    throw new Exception("Tampered fixture was accepted.");
}
catch (PreparationException error) when (error.Message.StartsWith("Unexpected source DLL", StringComparison.Ordinal)) { }
if (Path.Exists(Path.Combine(root, "tampered-output")))
    throw new Exception("Tampered fixture left output.");
Console.WriteLine($"REWRITE_FIXTURE_OK {first.PreparedSha256}");

static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
