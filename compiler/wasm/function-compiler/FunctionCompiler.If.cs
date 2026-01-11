using ToyLang.Syntax;

namespace ToyLang.Wasm;

internal sealed partial class FunctionCompiler
{
    private void EmitIf(IfStmt ifStmt)
    {
        var conditionKind = EmitExpression(ifStmt.Condition);
        if (!conditionKind.HasValue)
            throw new NotSupportedException("If condition must produce a value.");

        Coerce(conditionKind.Value, ValueType.Bool);

        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);

        EmitStatement(ifStmt.Then);

        if (ifStmt.Else != null)
        {
            EmitOpcode(WasmOpcode.Else);
            EmitStatement(ifStmt.Else);
        }

        EmitEnd();
    }

    private void EmitBr(uint depth)
    {
        EmitOpcode(WasmOpcode.Branch);
        _body.WriteVarUInt32(depth);
    }

    private void EmitBrIf(uint depth)
    {
        EmitOpcode(WasmOpcode.BranchIf);
        _body.WriteVarUInt32(depth);
    }
}
