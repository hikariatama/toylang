using ToyLang.Syntax;

namespace ToyLang.Wasm;

internal sealed partial class FunctionCompiler
{
    private ValueType? EmitIoCall(string member, IReadOnlyList<Expression> arguments)
    {
        switch (member.ToLowerInvariant())
        {
            case "print":
                if (arguments.Count != 1)
                    throw new NotSupportedException("IO.print expects exactly one argument.");
                EmitPrint(arguments[0]);
                return null;
            case "println":
            case "printline":
                if (arguments.Count > 1)
                    throw new NotSupportedException("IO.println accepts at most one argument.");
                if (arguments.Count == 1)
                    EmitPrint(arguments[0]);
                EmitCallByKey("io.PrintLine");
                return null;
            case "read":
                EnsureArgumentCount(arguments, 0);
                EmitCallByKey("io.Read");
                return ValueType.I32;
            case "readinteger":
                EnsureArgumentCount(arguments, 0);
                EmitCallByKey("io.ReadInteger");
                return ValueType.I32;
            case "readreal":
                EnsureArgumentCount(arguments, 0);
                EmitCallByKey("io.ReadReal");
                return ValueType.F64;
            case "readbool":
                EnsureArgumentCount(arguments, 0);
                EmitCallByKey("io.ReadBool");
                return ValueType.Bool;
            case "readline":
                EnsureArgumentCount(arguments, 0);
                EmitCallByKey("io.ReadLine");
                return ValueType.String;
            default:
                throw new NotSupportedException($"IO.{member} is not supported in wasm.");
        }
    }

    private void EmitPrint(Expression argument)
    {
        var valueKindOpt = EmitExpression(argument);
        if (!valueKindOpt.HasValue)
            throw new NotSupportedException("print argument must produce a value.");

        var valueType = valueKindOpt.Value;
        if (valueType.Kind == ValueKind.String)
        {
            EmitPrintStringFromStack();
            return;
        }

        var key = valueType.Kind switch
        {
            ValueKind.Bool => "io.PrintBool",
            ValueKind.F64 => "io.PrintReal",
            ValueKind.Array => "io.PrintArray",
            ValueKind.List => "io.PrintList",
            ValueKind.Map => "io.PrintMap",
            _ => "io.PrintInteger",
        };
        EmitCallByKey(key);
    }
    private void EmitPrintStringFromStack()
    {
        var temp = GetTemporary(ValueType.String);
        EmitLocalSet(temp.Index);
        EmitLocalGet(temp.Index);
        EmitI32Const(4);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalGet(temp.Index);
        EmitI32Load();
        EmitCallByKey("io.PrintString");
    }

    private LocalInfo GetTemporary(ValueType type)
    {
        if (_tempLocals.TryGetValue(type, out var existing))
            return existing;

        var index = _parameterCount + (uint)_localOrder.Count;
        var info = new LocalInfo($"$tmp{_tempLocals.Count}", index, type);
        _localOrder.Add(info);
        _tempLocals[type] = info;
        return info;
    }
}
