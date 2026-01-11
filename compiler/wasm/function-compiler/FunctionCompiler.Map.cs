using ToyLang.Syntax;

namespace ToyLang.Wasm;

internal sealed partial class FunctionCompiler
{
    private ValueType EmitMapConstructor(IReadOnlyList<TypeRef> typeArguments, IReadOnlyList<Expression> arguments)
    {
        if (typeArguments.Count != 2)
            throw new NotSupportedException("Map constructor requires key and value type arguments.");

        if (arguments.Count != 0)
            throw new NotSupportedException("Map constructor does not accept arguments.");

        var keyType = ValueType.MapValueType(typeArguments[0]);
        var valueType = ValueType.MapValueType(typeArguments[1]);

        EmitI32Const(0);
        return ValueType.ForMap(keyType, valueType);
    }

    private ValueType? EmitMapMemberCall(Expression targetExpr, ValueType mapType, string member, IReadOnlyList<Expression> arguments)
    {
        var mapInfo = mapType.Map ?? throw new NotSupportedException("Map metadata missing in wasm backend.");

        var mapPtrLocal = GetScratchI32(WasmScratchSlot.MapPointer);
        EmitLocalSet(mapPtrLocal.Index);

        var assignmentTarget = ResolveMapAssignmentTarget(targetExpr);
        var keyTag = GetValueTag(mapInfo.KeyType);
        var valueTag = GetValueTag(mapInfo.ValueType);
        var combinedTagValue = (valueTag << 16) | (keyTag & 0xFFFF);

        switch (member)
        {
            case "set":
                {
                    EnsureArgumentCount(arguments, 2);
                    var lookup = EmitMapLookup(mapPtrLocal, mapInfo, arguments[0]);
                    var nodeLocal = lookup.NodeLocal;
                    var keyLocal = lookup.KeyLocal;
                    var keyIsFloat = lookup.KeyIsFloat;

                    var valueType = RequireValue(arguments[1]);
                    Coerce(valueType, mapInfo.ValueType);

                    LocalInfo valueLocal;
                    var valueIsFloat = mapInfo.ValueType.Kind == ValueKind.F64;
                    if (valueIsFloat)
                    {
                        valueLocal = GetScratchF64(WasmScratchSlot.MapValueF64);
                        EmitLocalSet(valueLocal.Index);
                    }
                    else
                    {
                        valueLocal = GetScratchI32(WasmScratchSlot.MapValue);
                        EmitLocalSet(valueLocal.Index);
                    }

                    EmitLocalGet(nodeLocal.Index);
                    EmitI32Const(0);
                    EmitOpcode(WasmOpcode.EqualInt32);
                    EmitOpcode(WasmOpcode.If);
                    _body.WriteByte((byte)WasmControl.Void);

                    EmitHeapAlloc((int)WasmScratchSlot.MapNodeSize, 8);
                    var newNodeLocal = GetScratchI32(WasmScratchSlot.MapNewNode);
                    EmitLocalSet(newNodeLocal.Index);

                    EmitLocalGet(newNodeLocal.Index);
                    if (keyIsFloat)
                    {
                        EmitLocalGet(keyLocal.Index);
                        EmitF64Store(offset: (int)WasmMapOffset.Key);
                    }
                    else
                    {
                        EmitLocalGet(keyLocal.Index);
                        EmitI32Store(offset: (int)WasmMapOffset.Key);
                    }

                    EmitLocalGet(newNodeLocal.Index);
                    if (valueIsFloat)
                    {
                        EmitLocalGet(valueLocal.Index);
                        EmitF64Store(offset: (int)WasmMapOffset.Value);
                    }
                    else
                    {
                        EmitLocalGet(valueLocal.Index);
                        EmitI32Store(offset: (int)WasmMapOffset.Value);
                    }

                    EmitLocalGet(newNodeLocal.Index);
                    EmitLocalGet(mapPtrLocal.Index);
                    EmitI32Store(offset: (int)WasmMapOffset.Next);

                    EmitLocalGet(newNodeLocal.Index);
                    EmitI32Const(combinedTagValue);
                    EmitI32Store(offset: (int)WasmMapOffset.Tag);

                    EmitLocalGet(newNodeLocal.Index);
                    EmitLocalSet(mapPtrLocal.Index);

                    EmitOpcode(WasmOpcode.Else);

                    EmitLocalGet(nodeLocal.Index);
                    if (valueIsFloat)
                    {
                        EmitLocalGet(valueLocal.Index);
                        EmitF64Store(offset: (int)WasmMapOffset.Value);
                    }
                    else
                    {
                        EmitLocalGet(valueLocal.Index);
                        EmitI32Store(offset: (int)WasmMapOffset.Value);
                    }

                    EmitLocalGet(nodeLocal.Index);
                    EmitI32Const(combinedTagValue);
                    EmitI32Store(offset: (int)WasmMapOffset.Tag);

                    EmitOpcode(WasmOpcode.End);

                    StoreMapAssignmentTarget(assignmentTarget, mapPtrLocal);

                    return null;
                }

            case "get":
                {
                    EnsureArgumentCount(arguments, 1);
                    var lookup = EmitMapLookup(mapPtrLocal, mapInfo, arguments[0]);
                    var nodeLocal = lookup.NodeLocal;

                    EmitLocalGet(nodeLocal.Index);
                    EmitI32Const(0);
                    EmitOpcode(WasmOpcode.EqualInt32);
                    EmitOpcode(WasmOpcode.If);
                    WriteBlockType(mapInfo.ValueType);
                    EmitDefaultValue(mapInfo.ValueType);
                    EmitOpcode(WasmOpcode.Else);
                    if (mapInfo.ValueType.Kind == ValueKind.List)
                    {
                        var listValueLocal = GetScratchI32(WasmScratchSlot.MapValue);
                        EmitLocalGet(nodeLocal.Index);
                        EmitI32Load(offset: (int)WasmMapOffset.Value);
                        EmitLocalSet(listValueLocal.Index);
                        RegisterPendingListAlias(mapPtrLocal, lookup.KeyLocal, lookup.KeyIsFloat, mapInfo);
                        EmitLocalGet(listValueLocal.Index);
                    }
                    else
                    {
                        EmitLocalGet(nodeLocal.Index);
                        LoadMapValue(mapInfo.ValueType);
                    }
                    EmitOpcode(WasmOpcode.End);
                    return mapInfo.ValueType;
                }

            case "Contains":
                {
                    EnsureArgumentCount(arguments, 1);
                    var lookup = EmitMapLookup(mapPtrLocal, mapInfo, arguments[0]);
                    EmitLocalGet(lookup.NodeLocal.Index);
                    EmitI32Const(0);
                    EmitOpcode(WasmOpcode.NotEqualInt32);
                    return ValueType.Bool;
                }

            case "Remove":
                {
                    EnsureArgumentCount(arguments, 1);
                    var lookup = EmitMapLookup(mapPtrLocal, mapInfo, arguments[0]);
                    var nodeLocal = lookup.NodeLocal;
                    var previousLocal = lookup.PreviousLocal;

                    var resultLocal = GetScratchI32(WasmScratchSlot.MapRemovalResult);
                    EmitI32Const(0);
                    EmitLocalSet(resultLocal.Index);

                    EmitBlock();
                    EmitLocalGet(nodeLocal.Index);
                    EmitI32Const(0);
                    EmitOpcode(WasmOpcode.EqualInt32);
                    EmitOpcode(WasmOpcode.If);
                    _body.WriteByte((byte)WasmControl.Void);
                    EmitBr(1);
                    EmitOpcode(WasmOpcode.End);

                    var nextLocal = GetScratchI32(WasmScratchSlot.MapRemovalNext);
                    EmitLocalGet(nodeLocal.Index);
                    EmitI32Load(offset: (int)WasmMapOffset.Next);
                    EmitLocalSet(nextLocal.Index);

                    EmitLocalGet(previousLocal.Index);
                    EmitI32Const(0);
                    EmitOpcode(WasmOpcode.EqualInt32);
                    EmitOpcode(WasmOpcode.If);
                    _body.WriteByte((byte)WasmControl.Void);
                    EmitLocalGet(nextLocal.Index);
                    EmitLocalSet(mapPtrLocal.Index);
                    EmitOpcode(WasmOpcode.Else);
                    EmitLocalGet(previousLocal.Index);
                    EmitLocalGet(nextLocal.Index);
                    EmitI32Store(offset: (int)WasmMapOffset.Next);
                    EmitOpcode(WasmOpcode.End);

                    StoreMapAssignmentTarget(assignmentTarget, mapPtrLocal);

                    EmitI32Const(1);
                    EmitLocalSet(resultLocal.Index);

                    EmitOpcode(WasmOpcode.End);

                    EmitLocalGet(resultLocal.Index);
                    return ValueType.Bool;
                }

            case "Keys":
                {
                    EnsureArgumentCount(arguments, 0);
                    var headLocal = GetScratchI32(WasmScratchSlot.MapKeysHead);
                    EmitI32Const(0);
                    EmitLocalSet(headLocal.Index);

                    var currentLocal = GetScratchI32(WasmScratchSlot.MapLookupCurrent);
                    EmitLocalGet(mapPtrLocal.Index);
                    EmitLocalSet(currentLocal.Index);

                    var keyTagRuntime = GetValueTag(mapInfo.KeyType);

                    EmitBlock();
                    EmitLoop();
                    EmitLocalGet(currentLocal.Index);
                    EmitI32Const(0);
                    EmitOpcode(WasmOpcode.EqualInt32);
                    EmitBrIf(1);

                    LocalInfo keyValueLocal;
                    if (mapInfo.KeyType.Kind == ValueKind.F64)
                    {
                        keyValueLocal = GetScratchF64(WasmScratchSlot.MapKeyF64);
                        EmitLocalGet(currentLocal.Index);
                        EmitF64Load(offset: (int)WasmMapOffset.Key);
                        EmitLocalSet(keyValueLocal.Index);
                    }
                    else
                    {
                        keyValueLocal = GetScratchI32(WasmScratchSlot.MapKey);
                        EmitLocalGet(currentLocal.Index);
                        EmitI32Load(offset: (int)WasmMapOffset.Key);
                        EmitLocalSet(keyValueLocal.Index);
                    }

                    EmitHeapAlloc(16, 4);
                    var newNodeLocal = GetScratchI32(WasmScratchSlot.MapKeysNode);
                    EmitLocalSet(newNodeLocal.Index);

                    EmitLocalGet(newNodeLocal.Index);
                    if (mapInfo.KeyType.Kind == ValueKind.F64)
                    {
                        EmitLocalGet(keyValueLocal.Index);
                        EmitF64Store(offset: 0);
                    }
                    else
                    {
                        EmitLocalGet(keyValueLocal.Index);
                        EmitI32Store(offset: 0);
                    }

                    EmitLocalGet(newNodeLocal.Index);
                    EmitLocalGet(headLocal.Index);
                    EmitI32Store(offset: 8);

                    EmitLocalGet(newNodeLocal.Index);
                    EmitI32Const(keyTagRuntime);
                    EmitI32Store(offset: 12);

                    EmitLocalGet(newNodeLocal.Index);
                    EmitLocalSet(headLocal.Index);

                    EmitLocalGet(currentLocal.Index);
                    EmitI32Load(offset: (int)WasmMapOffset.Next);
                    EmitLocalSet(currentLocal.Index);

                    EmitBr(0);
                    EmitEnd();
                    EmitEnd();

                    EmitLocalGet(headLocal.Index);
                    return new ValueType(ValueKind.List, null, null, new ListInfo(mapInfo.KeyType));
                }

            case "Values":
                {
                    EnsureArgumentCount(arguments, 0);
                    var headLocal = GetScratchI32(WasmScratchSlot.MapValuesHead);
                    EmitI32Const(0);
                    EmitLocalSet(headLocal.Index);

                    var currentLocal = GetScratchI32(WasmScratchSlot.MapLookupCurrent);
                    EmitLocalGet(mapPtrLocal.Index);
                    EmitLocalSet(currentLocal.Index);

                    var valueTagRuntime = GetValueTag(mapInfo.ValueType);

                    EmitBlock();
                    EmitLoop();
                    EmitLocalGet(currentLocal.Index);
                    EmitI32Const(0);
                    EmitOpcode(WasmOpcode.EqualInt32);
                    EmitBrIf(1);

                    LocalInfo valueLocal;
                    if (mapInfo.ValueType.Kind == ValueKind.F64)
                    {
                        valueLocal = GetScratchF64(WasmScratchSlot.MapValueF64);
                        EmitLocalGet(currentLocal.Index);
                        EmitF64Load(offset: (int)WasmMapOffset.Value);
                        EmitLocalSet(valueLocal.Index);
                    }
                    else
                    {
                        valueLocal = GetScratchI32(WasmScratchSlot.MapValue);
                        EmitLocalGet(currentLocal.Index);
                        EmitI32Load(offset: (int)WasmMapOffset.Value);
                        EmitLocalSet(valueLocal.Index);
                    }

                    EmitHeapAlloc(16, 4);
                    var newNodeLocal = GetScratchI32(WasmScratchSlot.MapValuesNode);
                    EmitLocalSet(newNodeLocal.Index);

                    EmitLocalGet(newNodeLocal.Index);
                    if (mapInfo.ValueType.Kind == ValueKind.F64)
                    {
                        EmitLocalGet(valueLocal.Index);
                        EmitF64Store(offset: 0);
                    }
                    else
                    {
                        EmitLocalGet(valueLocal.Index);
                        EmitI32Store(offset: 0);
                    }

                    EmitLocalGet(newNodeLocal.Index);
                    EmitLocalGet(headLocal.Index);
                    EmitI32Store(offset: 8);

                    EmitLocalGet(newNodeLocal.Index);
                    EmitI32Const(valueTagRuntime);
                    EmitI32Store(offset: 12);

                    EmitLocalGet(newNodeLocal.Index);
                    EmitLocalSet(headLocal.Index);

                    EmitLocalGet(currentLocal.Index);
                    EmitI32Load(offset: (int)WasmMapOffset.Next);
                    EmitLocalSet(currentLocal.Index);

                    EmitBr(0);
                    EmitEnd();
                    EmitEnd();

                    EmitLocalGet(headLocal.Index);
                    return new ValueType(ValueKind.List, null, null, new ListInfo(mapInfo.ValueType));
                }

            default:
                throw new NotSupportedException($"Map member '{member}' is not supported in wasm backend.");
        }
    }

    private (LocalInfo NodeLocal, LocalInfo KeyLocal, bool KeyIsFloat, LocalInfo PreviousLocal) EmitMapLookup(LocalInfo mapPtrLocal, MapInfo mapInfo, Expression keyExpr)
    {
        var keyType = mapInfo.KeyType;
        var keyValueType = RequireValue(keyExpr);
        Coerce(keyValueType, keyType);

        LocalInfo keyLocal;
        var keyIsFloat = keyType.Kind == ValueKind.F64;
        if (keyIsFloat)
        {
            keyLocal = GetScratchF64(WasmScratchSlot.MapKeyF64);
            EmitLocalSet(keyLocal.Index);
        }
        else
        {
            keyLocal = GetScratchI32(WasmScratchSlot.MapKey);
            EmitLocalSet(keyLocal.Index);
        }

        var currentLocal = GetScratchI32(WasmScratchSlot.MapLookupCurrent);
        EmitLocalGet(mapPtrLocal.Index);
        EmitLocalSet(currentLocal.Index);

        var foundLocal = GetScratchI32(WasmScratchSlot.MapLookupFound);
        EmitI32Const(0);
        EmitLocalSet(foundLocal.Index);

        var previousLocal = GetScratchI32(WasmScratchSlot.MapLookupPrevious);
        EmitI32Const(0);
        EmitLocalSet(previousLocal.Index);

        EmitBlock();
        EmitLoop();
        EmitLocalGet(currentLocal.Index);
        EmitI32Const(0);
        EmitOpcode(WasmOpcode.EqualInt32);
        EmitBrIf(1);

        EmitCompareMapKey(currentLocal, keyLocal, keyType);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitLocalGet(currentLocal.Index);
        EmitLocalSet(foundLocal.Index);
        EmitBr(2);
        EmitOpcode(WasmOpcode.End);

        EmitLocalGet(currentLocal.Index);
        EmitLocalSet(previousLocal.Index);

        EmitLocalGet(currentLocal.Index);
        EmitI32Load(offset: (int)WasmMapOffset.Next);
        EmitLocalSet(currentLocal.Index);
        EmitBr(0);
        EmitEnd();
        EmitEnd();

        EmitLocalGet(foundLocal.Index);
        EmitLocalSet(currentLocal.Index);

        return (currentLocal, keyLocal, keyIsFloat, previousLocal);
    }

    private void EmitCompareMapKey(LocalInfo nodeLocal, LocalInfo keyLocal, ValueType keyType)
    {
        EmitLocalGet(nodeLocal.Index);
        if (keyType.Kind == ValueKind.F64)
        {
            EmitF64Load(offset: (int)WasmMapOffset.Key);
            EmitLocalGet(keyLocal.Index);
            EmitOpcode(WasmOpcode.EqualFloat64);
        }
        else if (keyType.Kind == ValueKind.String)
        {
            var storedKeyLocal = GetScratchI32(WasmScratchSlot.MapStoredStringKey);
            EmitI32Load(offset: (int)WasmMapOffset.Key);
            EmitLocalSet(storedKeyLocal.Index);
            EmitCompareStringMapKey(storedKeyLocal, keyLocal);
        }
        else
        {
            EmitI32Load(offset: (int)WasmMapOffset.Key);
            EmitLocalGet(keyLocal.Index);
            EmitOpcode(WasmOpcode.EqualInt32);
        }
    }

    private void EmitCompareStringMapKey(LocalInfo storedKeyLocal, LocalInfo keyLocal)
    {
        var leftLengthLocal = GetScratchI32(WasmScratchSlot.StringCompareLeftLength);
        var rightLengthLocal = GetScratchI32(WasmScratchSlot.StringCompareRightLength);
        var resultLocal = GetScratchI32(WasmScratchSlot.StringCompareResult);

        EmitI32Const(0);
        EmitLocalSet(resultLocal.Index);

        EmitBlock();

        EmitLocalGet(storedKeyLocal.Index);
        EmitLocalGet(keyLocal.Index);
        EmitOpcode(WasmOpcode.EqualInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitI32Const(1);
        EmitLocalSet(resultLocal.Index);
        EmitBr(0);
        EmitOpcode(WasmOpcode.End);

        EmitLocalGet(storedKeyLocal.Index);
        EmitOpcode(WasmOpcode.EqualZeroInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitBr(0);
        EmitOpcode(WasmOpcode.End);

        EmitLocalGet(keyLocal.Index);
        EmitOpcode(WasmOpcode.EqualZeroInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitBr(0);
        EmitOpcode(WasmOpcode.End);

        EmitLocalGet(storedKeyLocal.Index);
        EmitI32Load();
        EmitLocalSet(leftLengthLocal.Index);

        EmitLocalGet(keyLocal.Index);
        EmitI32Load();
        EmitLocalSet(rightLengthLocal.Index);

        EmitLocalGet(leftLengthLocal.Index);
        EmitLocalGet(rightLengthLocal.Index);
        EmitOpcode(WasmOpcode.EqualInt32);
        EmitLocalSet(resultLocal.Index);

        EmitBlock();
        EmitLocalGet(resultLocal.Index);
        EmitOpcode(WasmOpcode.EqualZeroInt32);
        EmitBrIf(0);
        CompareStringContent(storedKeyLocal, keyLocal, leftLengthLocal, resultLocal);
        EmitEnd();

        EmitEnd();

        EmitLocalGet(resultLocal.Index);
    }
    private ValueType LoadMapValue(ValueType valueType)
    {
        switch (valueType.Kind)
        {
            case ValueKind.F64:
                EmitF64Load(offset: (int)WasmMapOffset.Value);
                return valueType;
            default:
                EmitI32Load(offset: (int)WasmMapOffset.Value);
                return valueType;
        }
    }

    private (LocalInfo NodeLocal, LocalInfo PreviousLocal) EmitMapLookupForAlias(MapValueAlias alias)
    {
        var currentLocal = GetScratchI32(WasmScratchSlot.MapLookupCurrent);
        EmitLocalGet(alias.MapLocal.Index);
        EmitLocalSet(currentLocal.Index);

        var foundLocal = GetScratchI32(WasmScratchSlot.MapLookupFound);
        EmitI32Const(0);
        EmitLocalSet(foundLocal.Index);

        var previousLocal = GetScratchI32(WasmScratchSlot.MapLookupPrevious);
        EmitI32Const(0);
        EmitLocalSet(previousLocal.Index);

        EmitBlock();
        EmitLoop();
        EmitLocalGet(currentLocal.Index);
        EmitI32Const(0);
        EmitOpcode(WasmOpcode.EqualInt32);
        EmitBrIf(1);

        EmitCompareMapKey(currentLocal, alias.KeyLocal, alias.KeyType);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitLocalGet(currentLocal.Index);
        EmitLocalSet(foundLocal.Index);
        EmitBr(2);
        EmitOpcode(WasmOpcode.End);

        EmitLocalGet(currentLocal.Index);
        EmitLocalSet(previousLocal.Index);

        EmitLocalGet(currentLocal.Index);
        EmitI32Load(offset: (int)WasmMapOffset.Next);
        EmitLocalSet(currentLocal.Index);
        EmitBr(0);
        EmitEnd();
        EmitEnd();

        EmitLocalGet(foundLocal.Index);
        EmitLocalSet(currentLocal.Index);

        return (currentLocal, previousLocal);
    }

    private void StoreMapValueAtNode(LocalInfo nodeLocal, ValueType valueType, LocalInfo valueLocal)
    {
        EmitLocalGet(nodeLocal.Index);
        if (valueType.Kind == ValueKind.F64)
        {
            EmitLocalGet(valueLocal.Index);
            EmitF64Store(offset: (int)WasmMapOffset.Value);
        }
        else
        {
            EmitLocalGet(valueLocal.Index);
            EmitI32Store(offset: (int)WasmMapOffset.Value);
        }
    }

    private void RegisterPendingListAlias(LocalInfo mapPtrLocal, LocalInfo keyLocal, bool keyIsFloat, MapInfo mapInfo)
    {
        if (!_captureNextListAlias)
            return;

        var mapAliasLocal = AllocateAnonymousLocal(ValueType.I32);
        EmitLocalGet(mapPtrLocal.Index);
        EmitLocalSet(mapAliasLocal.Index);

        LocalInfo aliasKeyLocal;
        if (keyIsFloat)
        {
            aliasKeyLocal = AllocateAnonymousLocal(ValueType.F64);
            EmitLocalGet(keyLocal.Index);
            EmitLocalSet(aliasKeyLocal.Index);
        }
        else
        {
            aliasKeyLocal = AllocateAnonymousLocal(ValueType.I32);
            EmitLocalGet(keyLocal.Index);
            EmitLocalSet(aliasKeyLocal.Index);
        }

        _nextListAlias = new MapValueAlias(mapAliasLocal, aliasKeyLocal, mapInfo.KeyType, mapInfo);
        _captureNextListAlias = false;
    }
    private readonly record struct MapAssignmentTarget(LocalInfo? Local, (LocalInfo Instance, ClassFieldEntry Field)? InstanceField)
    {
        public bool IsValid => Local is not null || InstanceField.HasValue;
    }
    private MapAssignmentTarget ResolveMapAssignmentTarget(Expression targetExpr)
    {
        if (targetExpr is IdentifierExpr id && _locals.TryGetValue(id.Name, out var local))
            return new MapAssignmentTarget(local, null);

        if (targetExpr is MemberAccessExpr memberAccess && TryResolveInstanceFieldAssignment(memberAccess, out var instanceLocal, out var fieldEntry))
            return new MapAssignmentTarget(null, (instanceLocal, fieldEntry));

        return default;
    }
    private void StoreMapAssignmentTarget(MapAssignmentTarget target, LocalInfo pointerLocal)
    {
        if (!target.IsValid)
            return;

        if (target.Local is not null)
        {
            EmitLocalGet(pointerLocal.Index);
            EmitLocalSet(target.Local.Index);
            return;
        }

        var (instance, field) = target.InstanceField!.Value;
        EmitInstanceFieldAddress(instance, field.Offset);
        EmitLocalGet(pointerLocal.Index);
        EmitI32Store();
    }
}
