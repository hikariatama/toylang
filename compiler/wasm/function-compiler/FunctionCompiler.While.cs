using ToyLang.Syntax;

namespace ToyLang.Wasm;

internal sealed partial class FunctionCompiler
{
    private void EmitLoop()
    {
        EmitOpcode(WasmOpcode.Loop);
        _body.WriteByte((byte)WasmControl.Void);
    }

    private void EmitWhile(WhileStmt whileStmt)
    {
        EmitBlock();
        EmitLoop();
        _loopStack.Push(new LoopContext(BreakDepth: 1));

        var conditionKind = EmitExpression(whileStmt.Condition);
        if (!conditionKind.HasValue)
            throw new NotSupportedException("While condition must produce a value.");

        Coerce(conditionKind.Value, ValueType.Bool);
        EmitOpcode(WasmOpcode.EqualZeroInt32);
        EmitBrIf(1);

        foreach (var statement in whileStmt.Body)
        {
            EmitStatement(statement);
        }

        EmitBr(0);

        EmitEnd();
        EmitEnd();

        _loopStack.Pop();
    }

    private void EmitBreak(BreakStmt _)
    {
        if (_loopStack.Count == 0)
            throw new NotSupportedException("'break' is only supported inside loops in the wasm backend.");

        var loop = _loopStack.Peek();
        EmitBr(loop.BreakDepth);
    }
}
