using ToyLang.Syntax;

namespace ToyLang.Wasm;

internal sealed partial class FunctionCompiler
{
    private sealed record MathFunctionDescriptor(string HostKey, int ArgumentCount);

    private static readonly Dictionary<string, MathFunctionDescriptor> s_mathFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cos"] = new("math.Cos", 1),
        ["sin"] = new("math.Sin", 1),
        ["tan"] = new("math.Tan", 1),
        ["acos"] = new("math.Acos", 1),
        ["asin"] = new("math.Asin", 1),
        ["atan"] = new("math.Atan", 1),
        ["atan2"] = new("math.Atan2", 2),
        ["exp"] = new("math.Exp", 1),
        ["log"] = new("math.Log", 1),
        ["sqrt"] = new("math.Sqrt", 1),
        ["pow"] = new("math.Pow", 2),
        ["random"] = new("math.Random", 0),
    };

    private ValueType EmitMathCall(string member, IReadOnlyList<Expression> arguments)
    {
        if (!s_mathFunctions.TryGetValue(member, out var descriptor))
            throw new NotSupportedException($"Math.{member} is not supported in wasm backend.");

        EnsureArgumentCount(arguments, descriptor.ArgumentCount);

        for (var i = 0; i < arguments.Count; i++)
        {
            var argumentType = RequireValue(arguments[i]);
            Coerce(argumentType, ValueType.F64);
        }

        EmitCallByKey(descriptor.HostKey);
        return ValueType.F64;
    }
}
