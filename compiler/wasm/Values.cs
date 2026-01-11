using ToyLang.Syntax;

namespace ToyLang.Wasm;

public sealed record class InstanceInfo(string ClassName);

public sealed record class ArrayInfo(ValueType ElementType)
{
    public int ElementTag => ElementType.Kind switch
    {
        ValueKind.Bool => 1,
        ValueKind.F64 => 2,
        ValueKind.String => 3,
        ValueKind.Array => 4,
        _ => 0,
    };
}

public sealed record class ListInfo(ValueType ElementType)
{
    public int ElementTag => ElementType.Kind switch
    {
        ValueKind.I32 => 0,
        ValueKind.Bool => 1,
        ValueKind.F64 => 2,
        ValueKind.String => 3,
        ValueKind.Array => 4,
        ValueKind.Instance => 5,
        ValueKind.List => 6,
        ValueKind.Map => 7,
        _ => 0,
    };
}

public sealed record class MapInfo(ValueType KeyType, ValueType ValueType);

public enum ValueKind
{
    I32,
    Bool,
    F64,
    String,
    Array,
    Instance,
    List,
    Map
}

public readonly record struct ValueType(ValueKind Kind, ArrayInfo? Array = null, InstanceInfo? Instance = null, ListInfo? List = null, MapInfo? Map = null)
{
    public static readonly ValueType I32 = new(ValueKind.I32);
    public static readonly ValueType Bool = new(ValueKind.Bool);
    public static readonly ValueType F64 = new(ValueKind.F64);
    public static readonly ValueType String = new(ValueKind.String);

    public static ValueType ForInstance(string className) => new(ValueKind.Instance, null, new InstanceInfo(className));
    public static ValueType ForMap(ValueType keyType, ValueType valueType) => new(ValueKind.Map, null, null, null, new MapInfo(keyType, valueType));

    public bool Is(ValueKind kind) => Kind == kind;

    public static ValueType MapValueType(TypeRef type)
    {
        if (string.Equals(type.Name, "Array", StringComparison.Ordinal))
        {
            var element = type.TypeArguments.Count == 1
                ? MapValueType(type.TypeArguments[0])
                : I32;
            return new ValueType(ValueKind.Array, new ArrayInfo(element));
        }

        if (string.Equals(type.Name, "List", StringComparison.Ordinal))
        {
            var element = type.TypeArguments.Count == 1
                ? MapValueType(type.TypeArguments[0])
                : I32;
            return new ValueType(ValueKind.List, null, null, new ListInfo(element));
        }

        if (string.Equals(type.Name, "Boolean", StringComparison.Ordinal))
            return Bool;
        if (string.Equals(type.Name, "Real", StringComparison.Ordinal))
            return F64;
        if (string.Equals(type.Name, "String", StringComparison.Ordinal))
            return String;
        if (string.Equals(type.Name, "Integer", StringComparison.Ordinal))
            return I32;

        if (string.Equals(type.Name, "Map", StringComparison.Ordinal))
        {
            ValueType? keyType = null;
            ValueType? valueType = null;
            if (type.TypeArguments.Count >= 1)
                keyType = MapValueType(type.TypeArguments[0]);
            if (type.TypeArguments.Count >= 2)
                valueType = MapValueType(type.TypeArguments[1]);
            return ForMap(keyType ?? I32, valueType ?? I32);
        }

        return ForInstance(type.Name);
    }
}
