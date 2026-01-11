using ToyLang.Syntax;

namespace ToyLang.Wasm;

internal sealed partial class FunctionCompiler
{
    private void EmitVarDecl(VarDeclStmt varDecl)
    {
        BeginListAliasCapture();
        var value = EmitExpression(varDecl.Init);
        if (!value.HasValue)
            throw new NotSupportedException("Variable initializer must produce a value.");

        var local = DeclareLocal(varDecl.Name, value.Value);
        EmitLocalSet(local.Index);
        ConsumePendingListAlias(varDecl.Name, local);
    }

    private void EmitAssign(AssignStmt assignStmt)
    {
        if (assignStmt.Target is IdentifierExpr identifier)
        {
            var target = GetLocal(identifier.Name);
            BeginListAliasCapture();
            var value = EmitExpression(assignStmt.Value);
            if (!value.HasValue)
                throw new NotSupportedException("Assignment requires a value-producing expression.");

            if (value.Value.Kind == ValueKind.Instance)
            {
                TryPromoteLocalToInstance(target, value.Value);
                target = GetLocal(identifier.Name);
            }

            Coerce(value.Value, target.Type);
            EmitLocalSet(target.Index);
            ConsumePendingListAlias(identifier.Name, target);
            return;
        }
        else
        {
            CancelListAliasCapture();
        }

        if (assignStmt.Target is IndexExpr indexTarget)
        {
            EmitIndexedAssignment(indexTarget, assignStmt.Value);
            return;
        }

        if (assignStmt.Target is MemberAccessExpr memberTarget)
        {
            EmitMemberAssignment(memberTarget, assignStmt.Value);
            return;
        }

        throw new NotSupportedException("Assignments to the given target are not supported in the wasm backend yet.");
    }

    private void EmitIndexedAssignment(IndexExpr target, Expression valueExpr)
    {
        var targetTypeOpt = EmitExpression(target.Target);
        if (!targetTypeOpt.HasValue)
            throw new NotSupportedException("Index assignment target must produce a value.");

        var targetType = targetTypeOpt.Value;

        switch (targetType.Kind)
        {
            case ValueKind.Array:
                EmitArrayMemberCall(targetType, "set", [target.Index, valueExpr]);
                break;
            case ValueKind.List:
                EmitListMemberCall(target.Target, targetType, "set", [target.Index, valueExpr]);
                break;
            case ValueKind.I32:
            case ValueKind.Bool:
            default:
                throw new NotSupportedException("Unsupported indexed assignment in the wasm backend.");
            case ValueKind.String:
                EmitStringLiteral(string.Empty);
                break;
        }
    }

    private void EmitMemberAssignment(MemberAccessExpr memberAccess, Expression valueExpr)
    {
        LocalInfo instanceLocal;
        ClassFieldEntry fieldEntry;

        if (TryResolveInstanceFieldAssignment(memberAccess, out var resolvedInstance, out var resolvedField))
        {
            instanceLocal = resolvedInstance;
            fieldEntry = resolvedField;
        }
        else
        {
            var receiverTypeOpt = EmitExpression(memberAccess.Target);
            if (!receiverTypeOpt.HasValue || receiverTypeOpt.Value.Kind != ValueKind.Instance)
                throw new NotSupportedException("Assignment target must be an instance field in the wasm backend.");

            var receiverType = receiverTypeOpt.Value;
            var instanceInfo = receiverType.Instance
                ?? throw new NotSupportedException("Instance metadata missing for field assignment in the wasm backend.");

            instanceLocal = AllocateAnonymousLocal(receiverType);
            EmitLocalSet(instanceLocal.Index);

            var metadata = RequireClassMetadata(instanceInfo.ClassName);
            if (!metadata.TryGetField(memberAccess.Member, out fieldEntry))
                throw new NotSupportedException($"Field '{memberAccess.Member}' is not defined on '{metadata.Name}' in the wasm backend.");
        }

        var valueTypeOpt = EmitExpression(valueExpr);
        if (!valueTypeOpt.HasValue)
            throw new NotSupportedException("Assignment requires a value-producing expression.");

        Coerce(valueTypeOpt.Value, fieldEntry.Type);

        if (fieldEntry.Type.Kind == ValueKind.F64)
        {
            var valueLocal = GetScratchF64(WasmScratchSlot.InstanceFieldValueF64);
            EmitLocalSet(valueLocal.Index);
            EmitInstanceFieldAddress(instanceLocal, fieldEntry.Offset);
            EmitLocalGet(valueLocal.Index);
            EmitF64Store();
        }
        else
        {
            var valueLocal = GetScratchI32(WasmScratchSlot.InstanceFieldValue);
            EmitLocalSet(valueLocal.Index);
            EmitInstanceFieldAddress(instanceLocal, fieldEntry.Offset);
            EmitLocalGet(valueLocal.Index);
            EmitI32Store();
        }
    }

    private LocalInfo DeclareLocal(string name, ValueType type)
    {
        if (_locals.ContainsKey(name))
            throw new InvalidOperationException($"Local '{name}' already declared.");

        var index = _parameterCount + (uint)_localOrder.Count;
        var info = new LocalInfo(name, index, type);
        _locals[name] = info;
        _localOrder.Add(info);
        return info;
    }

    private LocalInfo GetLocal(string name)
    {
        if (_locals.TryGetValue(name, out var info))
            return info;
        throw new NotSupportedException($"Unknown local '{name}'.");
    }

    private void TryPromoteLocalToInstance(LocalInfo local, ValueType newValue)
    {
        if (newValue.Kind != ValueKind.Instance)
            return;

        var newInfo = newValue.Instance;
        if (newInfo is null)
            return;

        if (local.Type.Kind == ValueKind.Instance && local.Type.Instance is { } existing && string.Equals(existing.ClassName, newInfo.ClassName, StringComparison.Ordinal))
            return;

        if (!_classMetadata.TryGetValue(newInfo.ClassName, out var metadata) || metadata.IsGenericDefinition)
            return;

        var updated = new LocalInfo(local.Name, local.Index, newValue);
        _locals[local.Name] = updated;
        for (var i = 0; i < _localOrder.Count; i++)
        {
            if (string.Equals(_localOrder[i].Name, local.Name, StringComparison.Ordinal))
            {
                _localOrder[i] = updated;
                break;
            }
        }
    }
}
