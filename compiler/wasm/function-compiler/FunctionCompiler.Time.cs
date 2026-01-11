using ToyLang.Syntax;

namespace ToyLang.Wasm;

internal sealed partial class FunctionCompiler
{
    private ValueType? EmitTimeCall(string member, IReadOnlyList<Expression> arguments)
    {
        switch (member)
        {
            case "Sleep":
                EnsureArgumentCount(arguments, 1);
                var durationType = RequireValue(arguments[0]);
                Coerce(durationType, ValueType.F64);
                EmitCallByKey("time.Sleep");
                return null;
            case "PerfCounter":
                EnsureArgumentCount(arguments, 0);
                EmitCallByKey("time.PerfCounter");
                return ValueType.F64;
            case "Unix":
                EnsureArgumentCount(arguments, 0);
                EmitCallByKey("time.Unix");
                return ValueType.F64;
            default:
                throw new NotSupportedException($"Time.{member} is not supported in the wasm backend.");
        }
    }
}
