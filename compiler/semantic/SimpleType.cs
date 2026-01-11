namespace ToyLang.Semantic;

public sealed class SimpleType
{
    public string Name { get; }
    public IReadOnlyList<SimpleType> TypeArgs { get; }
    public SimpleType(string name, IReadOnlyList<SimpleType>? args = null)
    {
        Name = name;
        TypeArgs = args ?? Array.Empty<SimpleType>();
    }
    public override string ToString() => TypeArgs.Count == 0 ? Name : $"{Name}[{string.Join(",", TypeArgs.Select(a => a.ToString()))}]";
    public override bool Equals(object? obj)
    {
        if (obj is not SimpleType o) return false;
        if (!string.Equals(Name, o.Name, StringComparison.Ordinal)) return false;
        if (TypeArgs.Count != o.TypeArgs.Count) return false;
        for (int i = 0; i < TypeArgs.Count; i++) if (!TypeArgs[i].Equals(o.TypeArgs[i])) return false;
        return true;
    }
    public override int GetHashCode()
    {
        var h = Name.GetHashCode();
        foreach (var a in TypeArgs) h = HashCode.Combine(h, a.GetHashCode());
        return h;
    }
    public static readonly SimpleType Integer = new("Integer");
    public static readonly SimpleType Real = new("Real");
    public static readonly SimpleType Boolean = new("Boolean");
    public static readonly SimpleType String = new("String");
    public static SimpleType ArrayOf(SimpleType t) => new("Array", [t]);
}
