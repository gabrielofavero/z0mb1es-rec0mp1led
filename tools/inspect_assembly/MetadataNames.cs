using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

internal sealed class MetadataNames(MetadataReader md) : ISignatureTypeProvider<string, object?>
{
    public string Type(TypeDefinitionHandle h)
    {
        var t = md.GetTypeDefinition(h);
        var prefix = t.GetDeclaringType().IsNil ? md.GetString(t.Namespace) : Type(t.GetDeclaringType());
        return (prefix.Length == 0 ? "" : prefix + (t.GetDeclaringType().IsNil ? "." : "+")) + md.GetString(t.Name);
    }

    public string Type(TypeReferenceHandle h)
    {
        var t = md.GetTypeReference(h);
        var prefix = t.ResolutionScope.Kind == HandleKind.TypeReference
            ? Type((TypeReferenceHandle)t.ResolutionScope) : md.GetString(t.Namespace);
        return (prefix.Length == 0 ? "" : prefix + (t.ResolutionScope.Kind == HandleKind.TypeReference ? "+" : ".")) + md.GetString(t.Name);
    }

    public string Scope(EntityHandle h) => h.Kind switch
    {
        HandleKind.AssemblyReference => md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)h).Name),
        HandleKind.ModuleReference => md.GetString(md.GetModuleReference((ModuleReferenceHandle)h).Name),
        HandleKind.TypeReference => Scope(md.GetTypeReference((TypeReferenceHandle)h).ResolutionScope),
        HandleKind.ModuleDefinition => "local module",
        _ => h.Kind.ToString()
    };

    public string Entity(EntityHandle h) => h.Kind switch
    {
        HandleKind.TypeDefinition => Type((TypeDefinitionHandle)h),
        HandleKind.TypeReference => Type((TypeReferenceHandle)h),
        HandleKind.TypeSpecification => md.GetTypeSpecification((TypeSpecificationHandle)h).DecodeSignature(this, null),
        HandleKind.MethodDefinition or HandleKind.MemberReference or HandleKind.MethodSpecification => Member(h),
        HandleKind.ModuleReference => md.GetString(md.GetModuleReference((ModuleReferenceHandle)h).Name),
        _ => h.Kind.ToString()
    };

    public string Member(EntityHandle h) => h.Kind switch
    {
        HandleKind.MethodDefinition => Method((MethodDefinitionHandle)h),
        HandleKind.MemberReference => Reference((MemberReferenceHandle)h),
        HandleKind.MethodSpecification => MethodSpec((MethodSpecificationHandle)h),
        _ => Entity(h)
    };

    private string Method(MethodDefinitionHandle h)
    {
        var m = md.GetMethodDefinition(h);
        return $"{Type(m.GetDeclaringType())}::{md.GetString(m.Name)} {MethodSignature(m.DecodeSignature(this, null))}";
    }

    private string Reference(MemberReferenceHandle h)
    {
        var m = md.GetMemberReference(h);
        return $"{Entity(m.Parent)}::{md.GetString(m.Name)} {MemberSignature(m)}";
    }

    private string MethodSpec(MethodSpecificationHandle h)
    {
        var s = md.GetMethodSpecification(h);
        return $"{Member(s.Method)}<{string.Join(", ", s.DecodeSignature(this, null))}>";
    }

    public string MemberSignature(MemberReference m) => m.GetKind() == MemberReferenceKind.Field
        ? m.DecodeFieldSignature(this, null) : MethodSignature(m.DecodeMethodSignature(this, null));

    public string MethodSignature(MethodSignature<string> s) =>
        $"{s.ReturnType} ({string.Join(", ", s.ParameterTypes)}) [generic:{s.GenericParameterCount}; cc:{s.Header.CallingConvention}]";

    public string TokenMember(int token)
    {
        var h = MetadataTokens.EntityHandle(token);
        return h.Kind is HandleKind.MethodDefinition or HandleKind.MemberReference or HandleKind.MethodSpecification
            ? Member(h) : "";
    }

    public bool NeedsAdaptation(int token)
    {
        var h = MetadataTokens.EntityHandle(token);
        if (h.Kind == HandleKind.MethodSpecification) h = md.GetMethodSpecification((MethodSpecificationHandle)h).Method;
        if (h.Kind != HandleKind.MemberReference) return false;
        var parent = md.GetMemberReference((MemberReferenceHandle)h).Parent;
        if (parent.Kind == HandleKind.TypeReference)
            return Scope(md.GetTypeReference((TypeReferenceHandle)parent).ResolutionScope) is string scope &&
                (scope.StartsWith("Microsoft.Xna.Framework", StringComparison.Ordinal) ||
                 scope.StartsWith("Microsoft.Phone", StringComparison.Ordinal) ||
                 scope.StartsWith("Microsoft.Devices", StringComparison.Ordinal));
        return parent.Kind == HandleKind.TypeSpecification &&
            Entity(parent).Contains("Microsoft.Xna", StringComparison.Ordinal);
    }

    public string GetArrayType(string elementType, ArrayShape shape) =>
        $"{elementType}[{new string(',', shape.Rank - 1)}]";
    public string GetByReferenceType(string elementType) => elementType + "&";
    public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr " + MethodSignature(signature);
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
        $"{genericType}<{string.Join(", ", typeArguments)}>";
    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) =>
        $"{(isRequired ? "modreq" : "modopt")}({modifier}) {unmodifiedType}";
    public string GetPinnedType(string elementType) => elementType + " pinned";
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => Type(handle);
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => Type(handle);
    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
}
