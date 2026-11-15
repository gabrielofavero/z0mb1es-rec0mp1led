using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

internal static class AbiFixtureChecks
{
    public static int Run(string path)
    {
        try
        {
            using var assembly = new TargetAssembly(path, "fixture");
            var md = assembly.Reader;
            var n = assembly.Names;
            var exact = assembly.Exact;
            var boxHandle = md.TypeDefinitions.Single(h => n.Type(h) == "Abi.Fixtures.Box`1");
            var box = new TargetType(assembly, boxHandle, n.Type(boxHandle));
            Require(box.Definition.GetFields().Any(h => md.GetString(md.GetFieldDefinition(h).Name) == "Value" &&
                exact.Field(md.GetFieldDefinition(h)) == "!0"), "generic field signature");
            var echo = box.Members("Echo", MemberReferenceKind.Method).ToArray();
            Require(echo.Length == 2 && echo[0].Signature != echo[1].Signature,
                "same-name overloads require full signatures");
            Require(box.Members("Convert", MemberReferenceKind.Method).Single().Signature.Contains("!!0") &&
                box.Members("Convert", MemberReferenceKind.Method).Single().Signature.Contains("generic:1"),
                "generic method arity and arguments");
            Require(box.Members("ReadOnlyRef", MemberReferenceKind.Method).Single().Signature !=
                box.Members("MutableRef", MemberReferenceKind.Method).Single().Signature,
                "required modifier on readonly byref return");
            Require(box.Events().Any(e => e.Name == "Changed" && e.Type.Contains("EventHandler`1") &&
                e.Add == "add_Changed" && e.Remove == "remove_Changed"), "event metadata and accessors");
            Require(md.TypeDefinitions.Any(h => n.Type(h) == "Abi.Fixtures.Box`1+Nested"), "nested generic type");
            var derived = md.TypeDefinitions.Single(h => n.Type(h) == "Abi.Fixtures.Derived");
            Require(n.Entity(md.GetTypeDefinition(derived).BaseType) == "Abi.Fixtures.Base`1<Int32>",
                "generic inheritance");
            Require(md.GetTableRowCount(TableIndex.MethodSpec) > 0, "generic method instantiation");
            Require(box.Members(".ctor", MemberReferenceKind.Method).Any(x => x.Signature.Contains("Void (!0)")),
                "generic constructor");
            Console.WriteLine("ABI_FIXTURES_OK: overloads, generic field/method/constructor, modifiers, event, nested type, inheritance, instantiation");
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("ABI fixture check failed: " + e.Message);
            return 1;
        }
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidDataException(description);
    }
}
