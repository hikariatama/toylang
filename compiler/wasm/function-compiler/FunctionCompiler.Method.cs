using ToyLang.Syntax;

namespace ToyLang.Wasm;

internal sealed partial class FunctionCompiler
{
    public void Compile(MethodDecl method)
    {
        switch (method.Body)
        {
            case ExprBody exprBody:
                CompileExprBody(exprBody);
                break;
            case BlockBody blockBody:
                foreach (var statement in blockBody.Statements)
                {
                    EmitStatement(statement);
                }
                break;
            case null:
                if (_expectedReturn.HasValue)
                {
                    EmitDefaultValue(_expectedReturn.Value);
                    EmitReturn();
                    _hasReturn = true;
                }
                break;
        }
    }

    private void CompileExprBody(ExprBody body)
    {
        var result = EmitExpression(body.Expr);
        if (_expectedReturn.HasValue)
        {
            if (!result.HasValue)
                throw new NotSupportedException("Expression body must produce a value for non-void methods.");

            Coerce(result.Value, _expectedReturn.Value);
        }
        else if (result.HasValue)
        {
            EmitDrop();
        }

        EmitReturn();
        _hasReturn = true;
    }

    private void EmitReturn() => EmitOpcode(WasmOpcode.Return);

    private void EmitStatement(Statement statement)
    {
        switch (statement)
        {
            case VarDeclStmt varDecl:
                EmitVarDecl(varDecl);
                break;
            case AssignStmt assignStmt:
                EmitAssign(assignStmt);
                break;
            case ExprStmt exprStmt:
                {
                    var value = EmitExpression(exprStmt.Expr);
                    if (value.HasValue)
                        EmitDrop();
                    break;
                }
            case BlockStmt blockStmt:
                foreach (var inner in blockStmt.Statements)
                    EmitStatement(inner);
                break;
            case ReturnStmt returnStmt:
                EmitReturnStatement(returnStmt);
                break;
            case IfStmt ifStmt:
                EmitIf(ifStmt);
                break;
            case WhileStmt whileStmt:
                EmitWhile(whileStmt);
                break;
            case BreakStmt breakStmt:
                EmitBreak(breakStmt);
                break;
            default:
                throw new NotSupportedException($"Statement '{statement.GetType().Name}' is not supported in the wasm backend yet.");
        }
    }
    private void EmitReturnStatement(ReturnStmt returnStmt)
    {
        if (returnStmt.Expr != null)
        {
            var result = EmitExpression(returnStmt.Expr);
            if (!result.HasValue)
                throw new NotSupportedException("Return expression must produce a value.");

            if (_expectedReturn.HasValue)
            {
                Coerce(result.Value, _expectedReturn.Value);
            }
            else
            {
                EmitDrop();
            }
        }
        else if (_expectedReturn.HasValue)
        {
            EmitDefaultValue(_expectedReturn.Value);
        }

        EmitReturn();
        _hasReturn = true;
    }
}
