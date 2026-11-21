using System.Collections.Immutable;
using System.Reflection.Metadata;

// A comparison key, separate from the human-readable Prompt 1 inventory. In particular,
// class/valuetype, required modifiers, generic arity, and instance calling convention matter.
internal sealed class ExactNames(MetadataReader reader, Action<TypeReferenceHandle, byte>? typeKindUse = null) : ISignatureTypeProvider<string, object?>
{
    private readonly MetadataNames display = new(reader);
    public string Method(MethodSignature<string> s) =>
        $"{s.ReturnType} ({string.Join(", ", s.ParameterTypes)}) [generic:{s.GenericParameterCount}; required:{s.RequiredParameterCount}; header:{s.Header.RawValue:x2}]";
    public string Member(MemberReference m) => m.GetKind() == MemberReferenceKind.Field
        ? m.DecodeFieldSignature(this, null)
        : Method(m.DecodeMethodSignature(this, null));
    public string Method(MethodDefinition m) => Method(m.DecodeSignature(this, null));
    public string Field(FieldDefinition f) => f.DecodeSignature(this, null);
    private static string Kind(string name, byte raw) => raw switch
    {
        0x11 => "valuetype " + name,
        0x12 => "class " + name,
        _ => $"kind{raw:x2} " + name
    };
    public string GetArrayType(string elementType, ArrayShape shape) =>
        $"{elementType}[rank:{shape.Rank}; sizes:{string.Join(",", shape.Sizes)}; lows:{string.Join(",", shape.LowerBounds)}]";
    public string GetByReferenceType(string elementType) => elementType + "&";
    public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr " + Method(signature);
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
        genericType + "<" + string.Join(", ", typeArguments) + ">";
    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) =>
        $"{(isRequired ? "modreq" : "modopt")}({modifier}) {unmodifiedType}";
    public string GetPinnedType(string elementType) => elementType + " pinned";
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte rawTypeKind) =>
        Kind(display.Type(h), rawTypeKind);
    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte rawTypeKind)
    {
        typeKindUse?.Invoke(h, rawTypeKind);
        return Kind(display.Type(h), rawTypeKind);
    }
    public string GetTypeFromSpecification(MetadataReader r, object? context, TypeSpecificationHandle h, byte rawTypeKind) =>
        r.GetTypeSpecification(h).DecodeSignature(this, context);
}
