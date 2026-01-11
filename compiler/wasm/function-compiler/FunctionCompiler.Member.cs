using ToyLang.Syntax;

namespace ToyLang.Wasm;

internal sealed partial class FunctionCompiler
{
    private ValueType? EmitMemberCall(MemberAccessExpr memberAccess, IReadOnlyList<Expression> arguments)
    {
        if (memberAccess.Target is IdentifierExpr identifier && string.Equals(identifier.Name, "Screen", StringComparison.Ordinal))
        {
            if (arguments.Count != 0)
                throw new NotSupportedException("Screen members do not take arguments in wasm backend.");
            return EmitScreenConstant(memberAccess.Member);
        }

        var receiverTypeOpt = EmitExpression(memberAccess.Target);
        if (!receiverTypeOpt.HasValue)
        {
            foreach (var argument in arguments)
            {
                var argumentType = EmitExpression(argument);
                if (argumentType.HasValue)
                    EmitDrop();
            }

            return null;
        }

        var receiverType = receiverTypeOpt.Value;

        if (receiverType.Kind == ValueKind.Array)
        {
            return EmitArrayMemberCall(receiverType, memberAccess.Member, arguments);
        }

        if (receiverType.Kind == ValueKind.List)
        {
            return EmitListMemberCall(memberAccess.Target, receiverType, memberAccess.Member, arguments);
        }

        if (receiverType.Kind == ValueKind.Map)
        {
            return EmitMapMemberCall(memberAccess.Target, receiverType, memberAccess.Member, arguments);
        }

        if (receiverType.Kind == ValueKind.String)
        {
            return EmitStringMemberCall(memberAccess.Member, arguments);
        }

        if (receiverType.Kind == ValueKind.Instance)
        {
            var instanceInfo = receiverType.Instance;
            if (instanceInfo is null)
                throw new NotSupportedException("Instance metadata missing for member call in the wasm backend.");

            var receiverLocal = AllocateAnonymousLocal(receiverType);
            EmitLocalSet(receiverLocal.Index);

            var staticEntry = RequireInstanceMethod(instanceInfo.ClassName, memberAccess.Member, arguments.Count);
            var staticFunction = staticEntry.Function;
            var parameterTypes = staticFunction.ParameterTypes;
            if (parameterTypes.Length != arguments.Count + 1)
                throw new NotSupportedException($"Method '{memberAccess.Member}' on '{instanceInfo.ClassName}' has an unexpected parameter signature in the wasm backend.");

            var argLocals = new LocalInfo[arguments.Count];
            for (var i = 0; i < arguments.Count; i++)
            {
                var expected = parameterTypes[i + 1];
                var argType = RequireValue(arguments[i]);
                Coerce(argType, expected);

                LocalInfo storage;
                if (GetStorageKind(expected) == ValueKind.F64)
                    storage = GetScratchF64(WasmScratchSlot.ConstructorArgF64ScratchBase + i);
                else
                    storage = GetScratchI32(WasmScratchSlot.ConstructorArgScratchBase + i);

                EmitLocalSet(storage.Index);
                argLocals[i] = storage;
            }

            var typeLocal = GetScratchI32(WasmScratchSlot.Temporary);
            EmitLocalGet(receiverLocal.Index);
            EmitI32Load();
            EmitLocalSet(typeLocal.Index);

            var cases = BuildVirtualDispatchCases(instanceInfo.ClassName, memberAccess.Member, arguments.Count);
            if (cases.Count == 0)
                throw new NotSupportedException($"No dispatch targets found for method '{memberAccess.Member}' on '{instanceInfo.ClassName}' in the wasm backend.");

            var hasResult = staticFunction.ReturnType.HasValue;
            LocalInfo? resultLocal = null;
            if (hasResult)
                resultLocal = AllocateAnonymousLocal(staticFunction.ReturnType!.Value);

            for (var i = cases.Count - 1; i >= 0; i--)
            {
                var dispatchCase = cases[i];

                EmitLocalGet(typeLocal.Index);
                EmitI32Const(dispatchCase.TypeId);
                EmitOpcode(WasmOpcode.EqualInt32);
                EmitOpcode(WasmOpcode.If);
                _body.WriteByte((byte)WasmControl.Void);

                EmitLocalGet(receiverLocal.Index);
                for (var j = 0; j < argLocals.Length; j++)
                    EmitLocalGet(argLocals[j].Index);

                EmitCall(dispatchCase.Function.FunctionIndex);

                if (dispatchCase.Function.ReturnType.HasValue)
                {
                    if (hasResult)
                        EmitLocalSet(resultLocal!.Index);
                    else
                        EmitDrop();
                }

                EmitOpcode(WasmOpcode.Else);
            }

            EmitLocalGet(receiverLocal.Index);
            for (var j = 0; j < argLocals.Length; j++)
                EmitLocalGet(argLocals[j].Index);
            EmitCall(staticFunction.FunctionIndex);

            if (staticFunction.ReturnType.HasValue)
            {
                if (hasResult)
                    EmitLocalSet(resultLocal!.Index);
                else
                    EmitDrop();
            }

            for (var i = 0; i < cases.Count; i++)
                EmitOpcode(WasmOpcode.End);

            if (hasResult)
            {
                EmitLocalGet(resultLocal!.Index);
                return staticFunction.ReturnType;
            }

            return staticFunction.ReturnType;
        }

        switch (memberAccess.Member)
        {
            case "Plus":
                EnsureArgumentCount(arguments, 1);
                return EmitNumericBinary(receiverType, arguments[0], WasmOpcode.AddInt32, WasmOpcode.AddFloat64);
            case "Minus":
                EnsureArgumentCount(arguments, 1);
                return EmitNumericBinary(receiverType, arguments[0], WasmOpcode.SubtractInt32, WasmOpcode.SubtractFloat64);
            case "Mult":
                EnsureArgumentCount(arguments, 1);
                return EmitNumericBinary(receiverType, arguments[0], WasmOpcode.MultiplyInt32, WasmOpcode.MultiplyFloat64);
            case "Div":
                {
                    EnsureArgumentCount(arguments, 1);

                    if (receiverType.Kind == ValueKind.I32)
                    {
                        var leftLocal = GetScratchI32(WasmScratchSlot.Temporary);
                        EmitLocalSet(leftLocal.Index);

                        var rightType = RequireValue(arguments[0]);
                        var rightRealLocal = GetScratchF64(WasmScratchSlot.NumericRightF64);
                        _ = Coerce(rightType, ValueType.F64);
                        EmitLocalSet(rightRealLocal.Index);

                        var leftRealLocal = GetScratchF64(WasmScratchSlot.NumericLeftF64);
                        EmitLocalGet(leftLocal.Index);
                        EmitOpcode(WasmOpcode.ConvertInt32ToFloat64);
                        EmitLocalSet(leftRealLocal.Index);

                        EmitLocalGet(leftRealLocal.Index);
                        EmitLocalGet(rightRealLocal.Index);
                        EmitOpcode(WasmOpcode.DivideFloat64);
                        return ValueType.F64;
                    }

                    if (receiverType.Kind == ValueKind.F64)
                    {
                        var leftRealLocal = GetScratchF64(WasmScratchSlot.NumericLeftF64);
                        EmitLocalSet(leftRealLocal.Index);

                        var rightType = RequireValue(arguments[0]);
                        var rightRealLocal = GetScratchF64(WasmScratchSlot.NumericRightF64);
                        _ = Coerce(rightType, ValueType.F64);
                        EmitLocalSet(rightRealLocal.Index);

                        EmitLocalGet(leftRealLocal.Index);
                        EmitLocalGet(rightRealLocal.Index);
                        EmitOpcode(WasmOpcode.DivideFloat64);
                        return ValueType.F64;
                    }

                    _ = Coerce(receiverType, ValueType.I32);
                    var divRight = RequireValue(arguments[0]);
                    Coerce(divRight, ValueType.I32);
                    EmitOpcode(WasmOpcode.DivideInt32);
                    return ValueType.I32;
                }
            case "Mod":
                {
                    EnsureArgumentCount(arguments, 1);

                    if (receiverType.Kind == ValueKind.I32)
                    {
                        var leftLocal = GetScratchI32(WasmScratchSlot.Temporary);
                        EmitLocalSet(leftLocal.Index);

                        var rightType = RequireValue(arguments[0]);
                        if (rightType.Kind == ValueKind.F64)
                        {
                            var rightRealLocal = GetScratchF64(WasmScratchSlot.NumericRightF64);
                            _ = Coerce(rightType, ValueType.F64);
                            EmitLocalSet(rightRealLocal.Index);

                            var leftRealLocal = GetScratchF64(WasmScratchSlot.NumericLeftF64);
                            EmitLocalGet(leftLocal.Index);
                            EmitOpcode(WasmOpcode.ConvertInt32ToFloat64);
                            EmitLocalSet(leftRealLocal.Index);

                            var quotientLocal = GetScratchF64(WasmScratchSlot.NumericQuotientF64);
                            EmitLocalGet(leftRealLocal.Index);
                            EmitLocalGet(rightRealLocal.Index);
                            EmitOpcode(WasmOpcode.DivideFloat64);
                            EmitOpcode(WasmOpcode.TruncateFloat64);
                            EmitLocalSet(quotientLocal.Index);

                            EmitLocalGet(leftRealLocal.Index);
                            EmitLocalGet(quotientLocal.Index);
                            EmitLocalGet(rightRealLocal.Index);
                            EmitOpcode(WasmOpcode.MultiplyFloat64);
                            EmitOpcode(WasmOpcode.SubtractFloat64);
                            return ValueType.F64;
                        }

                        var rightIntLocal = GetScratchI32(WasmScratchSlot.MapKey);
                        Coerce(rightType, ValueType.I32);
                        EmitLocalSet(rightIntLocal.Index);

                        EmitLocalGet(leftLocal.Index);
                        EmitLocalGet(rightIntLocal.Index);
                        EmitOpcode(WasmOpcode.RemainderInt32);
                        return ValueType.I32;
                    }

                    if (receiverType.Kind == ValueKind.F64)
                    {
                        var leftRealLocal = GetScratchF64(WasmScratchSlot.NumericLeftF64);
                        EmitLocalSet(leftRealLocal.Index);

                        var rightType = RequireValue(arguments[0]);
                        var rightRealLocal = GetScratchF64(WasmScratchSlot.NumericRightF64);
                        _ = Coerce(rightType, ValueType.F64);
                        EmitLocalSet(rightRealLocal.Index);

                        var quotientLocal = GetScratchF64(WasmScratchSlot.NumericQuotientF64);
                        EmitLocalGet(leftRealLocal.Index);
                        EmitLocalGet(rightRealLocal.Index);
                        EmitOpcode(WasmOpcode.DivideFloat64);
                        EmitOpcode(WasmOpcode.TruncateFloat64);
                        EmitLocalSet(quotientLocal.Index);

                        EmitLocalGet(leftRealLocal.Index);
                        EmitLocalGet(quotientLocal.Index);
                        EmitLocalGet(rightRealLocal.Index);
                        EmitOpcode(WasmOpcode.MultiplyFloat64);
                        EmitOpcode(WasmOpcode.SubtractFloat64);
                        return ValueType.F64;
                    }

                    _ = Coerce(receiverType, ValueType.I32);
                    var modRight = RequireValue(arguments[0]);
                    Coerce(modRight, ValueType.I32);
                    EmitOpcode(WasmOpcode.RemainderInt32);
                    return ValueType.I32;
                }
            case "Rem":
                EnsureArgumentCount(arguments, 1);
                _ = Coerce(receiverType, ValueType.I32);
                var remRight = RequireValue(arguments[0]);
                Coerce(remRight, ValueType.I32);
                EmitOpcode(WasmOpcode.RemainderInt32);
                return ValueType.I32;
            case "Equal":
                EnsureArgumentCount(arguments, 1);
                _ = Coerce(receiverType, ValueType.I32);
                var eqRight = RequireValue(arguments[0]);
                Coerce(eqRight, ValueType.I32);
                EmitOpcode(WasmOpcode.EqualInt32);
                return ValueType.Bool;
            case "NotEqual":
                EnsureArgumentCount(arguments, 1);
                if (TryEmitNumericComparison(receiverType, arguments[0], intOpcode: WasmOpcode.NotEqualInt32, realOpcode: WasmOpcode.NotEqualFloat64))
                    return ValueType.Bool;
                _ = Coerce(receiverType, ValueType.Bool);
                var neBoolRight = RequireValue(arguments[0]);
                Coerce(neBoolRight, ValueType.Bool);
                EmitOpcode(WasmOpcode.XorInt32);
                return ValueType.Bool;
            case "LessThan":
                EnsureArgumentCount(arguments, 1);
                if (TryEmitNumericComparison(receiverType, arguments[0], intOpcode: WasmOpcode.LessThanInt32, realOpcode: WasmOpcode.LessThanFloat64))
                    return ValueType.Bool;
                throw new NotSupportedException("Less is only supported for numeric types in wasm backend.");
            case "LessEqual":
                EnsureArgumentCount(arguments, 1);
                if (TryEmitNumericComparison(receiverType, arguments[0], intOpcode: WasmOpcode.LessEqualInt32, realOpcode: WasmOpcode.LessEqualFloat64))
                    return ValueType.Bool;
                throw new NotSupportedException("LessEqual is only supported for numeric types in wasm backend.");
            case "GreaterThan":
                EnsureArgumentCount(arguments, 1);
                if (TryEmitNumericComparison(receiverType, arguments[0], intOpcode: WasmOpcode.GreaterThanInt32, realOpcode: WasmOpcode.GreaterThanFloat64))
                    return ValueType.Bool;
                throw new NotSupportedException("Greater is only supported for numeric types in wasm backend.");
            case "GreaterEqual":
                EnsureArgumentCount(arguments, 1);
                if (TryEmitNumericComparison(receiverType, arguments[0], intOpcode: WasmOpcode.GreaterEqualInt32, realOpcode: WasmOpcode.GreaterEqualFloat64))
                    return ValueType.Bool;
                throw new NotSupportedException("GreaterEqual is only supported for numeric types in wasm backend.");
            case "And":
                EnsureArgumentCount(arguments, 1);
                _ = Coerce(receiverType, ValueType.Bool);
                var andRight = RequireValue(arguments[0]);
                Coerce(andRight, ValueType.Bool);
                EmitOpcode(WasmOpcode.AndInt32);
                return ValueType.Bool;
            case "Or":
                EnsureArgumentCount(arguments, 1);
                _ = Coerce(receiverType, ValueType.Bool);
                var orRight = RequireValue(arguments[0]);
                Coerce(orRight, ValueType.Bool);
                EmitOpcode(WasmOpcode.OrInt32);
                return ValueType.Bool;
            case "Xor":
                EnsureArgumentCount(arguments, 1);
                _ = Coerce(receiverType, ValueType.Bool);
                var xorRight = RequireValue(arguments[0]);
                Coerce(xorRight, ValueType.Bool);
                EmitOpcode(WasmOpcode.XorInt32);
                return ValueType.Bool;
            case "Not":
                EnsureArgumentCount(arguments, 0);
                Coerce(receiverType, ValueType.Bool);
                EmitOpcode(WasmOpcode.EqualZeroInt32);
                return ValueType.Bool;
            case "UnaryMinus":
                EnsureArgumentCount(arguments, 0);
                if (receiverType.Kind == ValueKind.F64)
                {
                    _ = Coerce(receiverType, ValueType.F64);
                    EmitF64Const(-1.0);
                    EmitOpcode(WasmOpcode.MultiplyFloat64);
                    return ValueType.F64;
                }

                Coerce(receiverType, ValueType.I32);
                EmitI32Const(-1);
                EmitOpcode(WasmOpcode.MultiplyInt32);
                return ValueType.I32;
            case "ToInteger":
                EnsureArgumentCount(arguments, 0);
                Coerce(receiverType, ValueType.I32);
                return ValueType.I32;
            case "ToBoolean":
                EnsureArgumentCount(arguments, 0);
                Coerce(receiverType, ValueType.Bool);
                return ValueType.Bool;
            case "ToReal":
                EnsureArgumentCount(arguments, 0);
                Coerce(receiverType, ValueType.F64);
                return ValueType.F64;
            default:
                throw new NotSupportedException($"Member call '{memberAccess.Member}' is not supported in wasm.");
        }
    }

    private ValueType EmitScreenConstant(string member)
    {
        switch (member)
        {
            case "Width":
                EmitCallByKey("screen.Width");
                return ValueType.F64;
            case "Height":
                EmitCallByKey("screen.Height");
                return ValueType.F64;
            default:
                throw new NotSupportedException($"Member '{member}' is not supported on 'Screen' in wasm backend.");
        }
    }
    private ValueType? EmitInstanceMethodCall(ValueType receiverType, string methodName, IReadOnlyList<Expression> arguments)
    {
        var instanceInfo = receiverType.Instance ?? throw new NotSupportedException("Instance metadata missing in wasm backend.");

        if (!TryLookupInstanceMethod(instanceInfo.ClassName, methodName, arguments.Count, out var entry))
            throw new NotSupportedException($"Method '{methodName}' is not supported on '{instanceInfo.ClassName}' for {arguments.Count} argument(s).");

        var receiverLocal = GetScratchI32(WasmScratchSlot.InstanceReceiver);
        EmitLocalSet(receiverLocal.Index);

        EmitLocalGet(receiverLocal.Index);

        var parameters = entry.Function.ParameterTypes;
        if (parameters.Length != arguments.Count + 1)
            throw new NotSupportedException($"Arity mismatch when invoking '{instanceInfo.ClassName}.{methodName}'.");

        for (var i = 0; i < arguments.Count; i++)
        {
            var argumentType = RequireValue(arguments[i]);
            var expected = parameters[i + 1];
            Coerce(argumentType, expected);
        }

        EmitCall(entry.Function.FunctionIndex);
        return entry.Function.ReturnType;
    }

    private bool TryLookupInstanceMethod(string className, string methodName, int arity, out ClassMethodEntry entry)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = className;
        while (current != null && visited.Add(current))
        {
            if (_classMetadata.TryGetValue(current, out var metadata) && metadata.TryGetMethod(methodName, arity, out entry))
                return true;

            current = metadata?.BaseName;
        }

        entry = null!;
        return false;
    }

    private ValueType? EmitNumericBinary(ValueType receiverType, Expression rightExpr, WasmOpcode intOpcode, WasmOpcode floatOpcode)
    {
        if (receiverType.Kind == ValueKind.I32)
        {
            var rightType = RequireValue(rightExpr);
            Coerce(rightType, ValueType.I32);
            EmitOpcode(intOpcode);
            return ValueType.I32;
        }

        if (receiverType.Kind == ValueKind.F64)
        {
            var rightType = RequireValue(rightExpr);
            Coerce(rightType, ValueType.F64);
            EmitOpcode(floatOpcode);
            return ValueType.F64;
        }

        return null;
    }
}
