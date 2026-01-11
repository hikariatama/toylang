using System.Globalization;
using ToyLang.Syntax;

namespace ToyLang.Wasm;

internal sealed partial class FunctionCompiler
{
    private ValueType? EmitExpression(Expression expression)
    {
        return expression switch
        {
            LiteralExpr literal => EmitLiteral(literal),
            IdentifierExpr identifier => EmitIdentifier(identifier, allowTypeDefaults: true),
            ThisExpr => EmitThis(),
            MemberAccessExpr memberAccess => EmitMemberAccess(memberAccess),
            ParenExpr paren => EmitExpression(paren.Inner),
            CallExpr call => EmitCall(call),
            GenericRefExpr genericRef => EmitGenericReferenceValue(genericRef),
            IndexExpr index => EmitIndexExpression(index),
            _ => throw new NotSupportedException($"Expression '{expression.GetType().Name}' is not supported in the wasm backend yet."),
        };
    }

    private ValueType? EmitIndexExpression(IndexExpr index)
    {
        var targetTypeOpt = EmitExpression(index.Target);
        if (!targetTypeOpt.HasValue)
            throw new NotSupportedException("Index target must produce a value.");

        var targetType = targetTypeOpt.Value;
        switch (targetType.Kind)
        {
            case ValueKind.Array:
                {
                    var result = EmitArrayMemberCall(targetType, "get", new[] { index.Index });
                    if (!result.HasValue)
                        throw new NotSupportedException("Array index must produce a value.");
                    return result.Value;
                }
            case ValueKind.List:
                {
                    var result = EmitListMemberCall(index.Target, targetType, "get", new[] { index.Index });
                    if (!result.HasValue)
                        throw new NotSupportedException("List index must produce a value.");
                    return result.Value;
                }
            case ValueKind.Map:
                {
                    var result = EmitMapMemberCall(index.Target, targetType, "get", new[] { index.Index });
                    if (!result.HasValue)
                        throw new NotSupportedException("Map index must produce a value.");
                    return result.Value;
                }
            default:
                throw new NotSupportedException("Unsupported index expression in wasm backend.");
        }
    }

    private ValueType EmitIdentifier(IdentifierExpr identifier, bool allowTypeDefaults)
    {
        if (_locals.TryGetValue(identifier.Name, out var local))
        {
            EmitLocalGet(local.Index);
            return local.Type;
        }

        if (allowTypeDefaults && TryEmitTypeDefault(identifier.Name, out var kind))
        {
            return kind;
        }

        throw new NotSupportedException($"Unknown local '{identifier.Name}'.");
    }

    private ValueType EmitThis()
    {
        if (!_hasInstanceContext || _thisLocal is null)
            throw new NotSupportedException("'this' is not available in this context.");

        EmitLocalGet(_thisLocal.Index);
        return _thisLocal.Type;
    }

    private ValueType EmitMemberAccess(MemberAccessExpr memberAccess)
    {
        if (memberAccess.Target is IdentifierExpr identifier && string.Equals(identifier.Name, "Screen", StringComparison.Ordinal))
            return EmitScreenConstant(memberAccess.Member);

        var receiverTypeOpt = EmitExpression(memberAccess.Target);
        if (!receiverTypeOpt.HasValue)
            throw new NotSupportedException("Member access target must produce a value.");

        var receiverType = receiverTypeOpt.Value;
        if (receiverType.Kind == ValueKind.String)
        {
            var receiverLocal = AllocateAnonymousLocal(ValueType.String);
            EmitLocalSet(receiverLocal.Index);

            if (string.Equals(memberAccess.Member, "Length", StringComparison.Ordinal))
            {
                EmitLocalGet(receiverLocal.Index);
                EmitI32Load();
                return ValueType.I32;
            }

            EmitLocalGet(receiverLocal.Index);
        }

        if (receiverType.Kind == ValueKind.Array)
        {
            var result = EmitArrayMemberCall(receiverType, memberAccess.Member, Array.Empty<Expression>());
            if (!result.HasValue)
                throw new NotSupportedException($"Array member '{memberAccess.Member}' does not produce a value.");
            return result.Value;
        }

        if (receiverType.Kind == ValueKind.Instance)
        {
            var instanceInfo = receiverType.Instance
                ?? throw new NotSupportedException("Instance metadata missing for member access in the wasm backend.");

            var receiverLocal = AllocateAnonymousLocal(receiverType);
            EmitLocalSet(receiverLocal.Index);

            if (TryEmitInstanceFieldAccess(receiverLocal, instanceInfo.ClassName, memberAccess.Member, out var fieldType))
                return fieldType;

            EmitLocalGet(receiverLocal.Index);
        }

        throw new NotSupportedException($"Member access '{memberAccess.Member}' is not supported in wasm backend.");
    }

    private bool TryEmitTypeDefault(string name, out ValueType kind)
    {
        switch (name)
        {
            case "Integer":
                EmitI32Const(0);
                kind = ValueType.I32;
                return true;
            case "Boolean":
                EmitI32Const(0);
                kind = ValueType.Bool;
                return true;
            case "Real":
                EmitF64Const(0.0);
                kind = ValueType.F64;
                return true;
            case "String":
                kind = EmitStringLiteral(string.Empty);
                return true;
            default:
                break;
        }

        if (_classMetadata.ContainsKey(name))
        {
            EmitI32Const(0);
            kind = ValueType.ForInstance(name);
            return true;
        }

        if (_knownTypes.Contains(name))
        {
            EmitI32Const(0);
            kind = ValueType.I32;
            return true;
        }

        kind = default;
        return false;
    }

    private ValueType? EmitCall(CallExpr call)
    {
        switch (call.Target)
        {
            case IdentifierExpr identifier:
                if (_functions.TryGetValue(identifier.Name, out var function))
                    return EmitUserFunctionCall(function, call.Arguments);
                return EmitConstructorCall(identifier, call.Arguments);
            case MemberAccessExpr memberAccess:
                if (memberAccess.Target is IdentifierExpr id)
                {
                    if (string.Equals(id.Name, "IO", StringComparison.OrdinalIgnoreCase))
                        return EmitIoCall(memberAccess.Member, call.Arguments);
                    if (string.Equals(id.Name, "Math", StringComparison.OrdinalIgnoreCase))
                        return EmitMathCall(memberAccess.Member, call.Arguments);
                    if (string.Equals(id.Name, "Time", StringComparison.OrdinalIgnoreCase))
                        return EmitTimeCall(memberAccess.Member, call.Arguments);
                }

                return EmitMemberCall(memberAccess, call.Arguments);
            case GenericRefExpr genericRef:
                return EmitGenericCall(genericRef, call.Arguments);
            default:
                throw new NotSupportedException("Unsupported call target in wasm backend.");
        }
    }

    private ValueType? EmitConstructorCall(IdentifierExpr identifier, IReadOnlyList<Expression> arguments)
    {
        switch (identifier.Name)
        {
            case "Integer":
                EnsureArgumentCount(arguments, 1);
                var intArg = RequireValue(arguments[0]);
                Coerce(intArg, ValueType.I32);
                return ValueType.I32;
            case "Boolean":
                EnsureArgumentCount(arguments, 1);
                var boolArg = RequireValue(arguments[0]);
                Coerce(boolArg, ValueType.Bool);
                return ValueType.Bool;
            case "Real":
                EnsureArgumentCount(arguments, 1);
                var realArg = RequireValue(arguments[0]);
                Coerce(realArg, ValueType.F64);
                return ValueType.F64;
            case "String":
                EnsureArgumentCount(arguments, 1);
                var strArg = RequireValue(arguments[0]);
                return ConvertToString(strArg);
            default:
                if (_classMetadata.TryGetValue(identifier.Name, out var metadata))
                    return EmitClassConstructorCall(metadata, arguments);

                if (_knownTypes.Contains(identifier.Name))
                {
                    EnsureArgumentCount(arguments, 0);
                    EmitI32Const(0);
                    return ValueType.I32;
                }

                throw new NotSupportedException($"Constructor '{identifier.Name}' is not supported in wasm.");
        }
    }

    private ValueType? EmitGenericCall(GenericRefExpr genericRef, IReadOnlyList<Expression> arguments)
    {
        if (genericRef.Target is IdentifierExpr id)
        {
            if (string.Equals(id.Name, "Array", StringComparison.Ordinal))
                return EmitArrayConstructor(genericRef.TypeArguments, arguments);

            if (string.Equals(id.Name, "List", StringComparison.Ordinal))
                return EmitListConstructor(genericRef.TypeArguments, arguments);

            if (string.Equals(id.Name, "Map", StringComparison.Ordinal))
                return EmitMapConstructor(genericRef.TypeArguments, arguments);
        }

        throw new NotSupportedException("Generic calls are only supported for Array, List, and Map in the wasm backend.");
    }

    private ValueType EmitGenericReferenceValue(GenericRefExpr genericRef)
    {
        var typeRef = BuildTypeRefFromGeneric(genericRef);
        if (typeRef == null)
            throw new NotSupportedException("Generic reference target must be a named type in the wasm backend.");
        var valueType = ValueType.MapValueType(typeRef);
        EmitDefaultValue(valueType);
        return valueType;
    }

    private ValueType? EmitUserFunctionCall(FunctionInfo function, IReadOnlyList<Expression> arguments)
    {
        if (function.Parameters.Length != arguments.Count)
            throw new NotSupportedException($"Function '{function.Name}' expects {function.Parameters.Length} argument(s) but received {arguments.Count}.");

        for (var i = 0; i < arguments.Count; i++)
        {
            var argumentType = RequireValue(arguments[i]);
            Coerce(argumentType, function.Parameters[i]);
        }

        EmitCall(function.Index);
        return function.ReturnType;
    }

    private ValueType EmitLiteral(LiteralExpr literal)
    {
        switch (literal.Kind)
        {
            case TokenType.Integer when int.TryParse(literal.Lexeme, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue):
                EmitI32Const(intValue);
                return ValueType.I32;
            case TokenType.Integer:
                EmitI32Const(0);
                return ValueType.I32;
            case TokenType.Boolean:
                var boolValue = literal.Lexeme.Equals("true", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                EmitI32Const(boolValue);
                return ValueType.Bool;
            case TokenType.Real when double.TryParse(literal.Lexeme, NumberStyles.Float, CultureInfo.InvariantCulture, out var realValue):
                EmitF64Const(realValue);
                return ValueType.F64;
            case TokenType.Real:
                EmitF64Const(0.0);
                return ValueType.F64;
            case TokenType.String:
                return EmitStringLiteral(literal.Lexeme);
            default:
                throw new NotSupportedException($"Literal '{literal.Kind}' is not supported in wasm backend.");
        }
    }
}
