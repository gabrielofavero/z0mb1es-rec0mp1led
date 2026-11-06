using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

internal sealed class TargetCatalog : IDisposable
{
    private readonly List<TargetAssembly> assemblies = [];
    private readonly Dictionary<string, List<TargetType>> types = new(StringComparer.Ordinal);
    public TargetCatalog(string fnaPath, string? bridgePath = null)
    {
        Add(fnaPath, "FNA");
        var fna = assemblies[0];
        if (fna.Name != "FNA" || fna.Reader.GetAssemblyDefinition().Version.ToString() != "26.11.0.0")
            throw new InvalidDataException("Target is not the pinned FNA 26.11 managed assembly");
        if (bridgePath is not null)
        {
            Add(bridgePath, "Port.Bridge");
            if (assemblies[1].Name != "Port.Bridge")
                throw new InvalidDataException("Bridge target is not Port.Bridge");
        }
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            ?? throw new InvalidOperationException(".NET trusted platform assembly list unavailable");
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
            if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(fnaPath), StringComparison.OrdinalIgnoreCase))
                Add(path, "runtime");
    }

    private void Add(string path, string category)
    {
        var assembly = new TargetAssembly(path, category);
        assemblies.Add(assembly);
        foreach (var handle in assembly.Reader.TypeDefinitions)
        {
            var name = assembly.Names.Type(handle);
            if (!types.TryGetValue(name, out var list)) types[name] = list = [];
            list.Add(new TargetType(assembly, handle, name));
        }
    }

    public IReadOnlyList<TargetType> Find(string name, string category) =>
        types.TryGetValue(name, out var list)
            ? list.Where(t => t.Assembly.Category == category).ToArray()
            : [];

    public TargetType? FindBase(TargetType type)
    {
        var name = type.BaseName;
        if (name is null) return null;
        name = name.Split('<')[0];
        return Find(name, type.Assembly.Category).FirstOrDefault()
            ?? Find(name, "FNA").FirstOrDefault()
            ?? Find(name, "runtime").FirstOrDefault();
    }

    public void Dispose()
    {
        foreach (var a in assemblies) a.Dispose();
    }
}

internal sealed class TargetAssembly : IDisposable
{
    private readonly FileStream stream;
    private readonly PEReader pe;
    public MetadataReader Reader { get; }
    public MetadataNames Names { get; }
    public ExactNames Exact { get; }
    public string Name { get; }
    public string Category { get; }
    public string Path { get; }
    public TargetAssembly(string path, string category)
    {
        Path = path;
        Category = category;
        stream = File.OpenRead(path);
        pe = new PEReader(stream);
        Reader = pe.GetMetadataReader();
        Names = new MetadataNames(Reader);
        Exact = new ExactNames(Reader);
        Name = Reader.GetString(Reader.GetAssemblyDefinition().Name);
    }
    public void Dispose() { pe.Dispose(); stream.Dispose(); }
}

internal sealed record TargetType(TargetAssembly Assembly, TypeDefinitionHandle Handle, string Name)
{
    public TypeDefinition Definition => Assembly.Reader.GetTypeDefinition(Handle);
    public string? BaseName => Definition.BaseType.IsNil ? null : Assembly.Names.Entity(Definition.BaseType);
    public bool IsInterface => Definition.Attributes.HasFlag(System.Reflection.TypeAttributes.Interface);
    public bool IsValueType => BaseName is "System.ValueType" or "System.Enum";

    public IEnumerable<(string Signature, string Display)> Members(string name, MemberReferenceKind kind)
    {
        var reader = Assembly.Reader;
        if (kind == MemberReferenceKind.Method)
            foreach (var h in Definition.GetMethods())
            {
                var m = reader.GetMethodDefinition(h);
                if (reader.GetString(m.Name) == name)
                    yield return (Assembly.Exact.Method(m), Assembly.Names.MethodSignature(m.DecodeSignature(Assembly.Names, null)));
            }
        else
            foreach (var h in Definition.GetFields())
            {
                var f = reader.GetFieldDefinition(h);
                if (reader.GetString(f.Name) == name)
                    yield return (Assembly.Exact.Field(f), f.DecodeSignature(Assembly.Names, null));
            }
    }

    public IEnumerable<(string Name, string Type, string? Add, string? Remove)> Events()
    {
        var reader = Assembly.Reader;
        foreach (var h in Definition.GetEvents())
        {
            var e = reader.GetEventDefinition(h);
            var a = e.GetAccessors();
            yield return (reader.GetString(e.Name), Assembly.Names.Entity(e.Type),
                a.Adder.IsNil ? null : reader.GetString(reader.GetMethodDefinition(a.Adder).Name),
                a.Remover.IsNil ? null : reader.GetString(reader.GetMethodDefinition(a.Remover).Name));
        }
    }
}
