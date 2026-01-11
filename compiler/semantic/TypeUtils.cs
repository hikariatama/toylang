using ToyLang.Syntax;

namespace ToyLang.Semantic;

public static class TypeUtils
{
    public static SimpleType FromTypeRef(TypeRef t) => new(t.Name, t.TypeArguments.Select(FromTypeRef).ToList());
    public static bool IsBoolean(SimpleType t) => t.Equals(SimpleType.Boolean);
    public static bool IsInteger(SimpleType t) => t.Equals(SimpleType.Integer);
    public static bool IsReal(SimpleType t) => t.Equals(SimpleType.Real);
    public static bool IsString(SimpleType t) => t.Equals(SimpleType.String);
    public static bool IsArray(SimpleType t) => string.Equals(t.Name, "Array", StringComparison.Ordinal) && t.TypeArgs.Count == 1;
    public static SimpleType? ArrayElementType(SimpleType t) => IsArray(t) ? t.TypeArgs[0] : null;
    public static bool IsList(SimpleType t) => string.Equals(t.Name, "List", StringComparison.Ordinal) && t.TypeArgs.Count == 1;
    public static SimpleType? ListElementType(SimpleType t) => IsList(t) ? t.TypeArgs[0] : null;
    public static bool IsMap(SimpleType t) => string.Equals(t.Name, "Map", StringComparison.Ordinal) && t.TypeArgs.Count == 2;
    public static (SimpleType Key, SimpleType Value)? MapElementTypes(SimpleType t)
        => IsMap(t) ? (t.TypeArgs[0], t.TypeArgs[1]) : null;
    public static bool Same(SimpleType a, SimpleType b) => a.Equals(b);
    public static bool Numeric(SimpleType t) => IsInteger(t) || IsReal(t);
    public static SimpleType Promote(SimpleType a, SimpleType b)
    {
        if (IsReal(a) || IsReal(b)) return SimpleType.Real;
        return SimpleType.Integer;
    }
}
