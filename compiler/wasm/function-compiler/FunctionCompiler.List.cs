using ToyLang.Syntax;

namespace ToyLang.Wasm;

internal sealed partial class FunctionCompiler
{
    private const int ListHeaderSize = 12;
    private const int ListCellSize = 16;

    private ValueType EmitListConstructor(IReadOnlyList<TypeRef> typeArguments, IReadOnlyList<Expression> arguments)
    {
        if (typeArguments.Count != 1)
            throw new NotSupportedException("List constructor requires a single element type argument.");

        var elementType = ValueType.MapValueType(typeArguments[0]);

        if (arguments.Count == 0)
        {
            EmitList(elementType, 0);
            return new ValueType(ValueKind.List, null, null, new ListInfo(elementType));
        }

        EmitList(elementType, arguments.Count);
        var listLocal = GetScratchI32(WasmScratchSlot.List);
        EmitLocalSet(listLocal.Index);

        var baseLocal = GetScratchI32(WasmScratchSlot.InstanceReceiver);
        EmitLocalGet(listLocal.Index);
        EmitI32Const(ListHeaderSize);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(baseLocal.Index);

        var indexLocal = GetScratchI32(WasmScratchSlot.ListIndex);
        EmitI32Const(0);
        EmitLocalSet(indexLocal.Index);

        LocalInfo valueLocalI32 = GetScratchI32(17);
        LocalInfo valueLocalF64 = GetScratchF64(2);

        for (var i = 0; i < arguments.Count; i++)
        {
            var vt = RequireValue(arguments[i]);
            Coerce(vt, elementType);

            if (elementType.Kind == ValueKind.F64)
            {
                EmitLocalSet(valueLocalF64.Index);
            }
            else
            {
                EmitLocalSet(valueLocalI32.Index);
            }

            EmitLocalGet(baseLocal.Index);
            EmitLocalGet(indexLocal.Index);
            EmitI32Const(ListCellSize);
            EmitOpcode(WasmOpcode.MultiplyInt32);
            EmitOpcode(WasmOpcode.AddInt32);

            if (elementType.Kind == ValueKind.F64)
            {
                EmitLocalGet(valueLocalF64.Index);
                EmitF64Store(offset: 0);
            }
            else
            {
                EmitLocalGet(valueLocalI32.Index);
                EmitI32Store(offset: 0);
            }

            EmitLocalGet(baseLocal.Index);
            EmitLocalGet(indexLocal.Index);
            EmitI32Const(ListCellSize);
            EmitOpcode(WasmOpcode.MultiplyInt32);
            EmitOpcode(WasmOpcode.AddInt32);
            EmitI32Const(GetValueTag(elementType));
            EmitI32Store(offset: 12);

            EmitLocalGet(indexLocal.Index);
            EmitI32Const(1);
            EmitOpcode(WasmOpcode.AddInt32);
            EmitLocalSet(indexLocal.Index);
        }

        EmitLocalGet(listLocal.Index);
        EmitI32Const(arguments.Count);
        EmitI32Store(offset: 4);

        EmitLocalGet(listLocal.Index);
        return new ValueType(ValueKind.List, null, null, new ListInfo(elementType));
    }

    private void EmitList(ValueType elementType, int initialCapacity)
    {
        var allocSize = ListHeaderSize + initialCapacity * ListCellSize;
        EmitHeapAlloc((uint)allocSize, 4);
        var listLocal = GetScratchI32(WasmScratchSlot.List);
        EmitLocalSet(listLocal.Index);

        EmitLocalGet(listLocal.Index);
        EmitI32Const(GetValueTag(elementType));
        EmitI32Store(offset: 0);

        EmitLocalGet(listLocal.Index);
        EmitI32Const(0);
        EmitI32Store(offset: 4);

        EmitLocalGet(listLocal.Index);
        EmitI32Const(initialCapacity);
        EmitI32Store(offset: 8);

        EmitLocalGet(listLocal.Index);
    }

    private void EmitListAppend(ValueType elementType)
    {
        var valueLocalI32 = GetScratchI32(WasmScratchSlot.Temporary);
        var valueLocalF64 = GetScratchF64(2);

        if (elementType.Kind == ValueKind.F64)
            EmitLocalSet(valueLocalF64.Index);
        else
            EmitLocalSet(valueLocalI32.Index);

        var listLocal = GetScratchI32(WasmScratchSlot.List);
        EmitLocalSet(listLocal.Index);

        var countLocal = GetScratchI32(WasmScratchSlot.ListLengthCount);
        EmitLocalGet(listLocal.Index);
        EmitI32Load(offset: 4);
        EmitLocalSet(countLocal.Index);

        var capacityLocal = GetScratchI32(WasmScratchSlot.ListIndex);
        EmitLocalGet(listLocal.Index);
        EmitI32Load(offset: 8);
        EmitLocalSet(capacityLocal.Index);

        EmitLocalGet(countLocal.Index);
        EmitLocalGet(capacityLocal.Index);
        EmitOpcode(WasmOpcode.EqualInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);

        var newCapacityLocal = GetScratchI32(WasmScratchSlot.MapKey);
        EmitLocalGet(capacityLocal.Index);
        EmitI32Const(0);
        EmitOpcode(WasmOpcode.EqualInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmType.I32);
        EmitI32Const(4);
        EmitOpcode(WasmOpcode.Else);
        EmitLocalGet(capacityLocal.Index);
        EmitI32Const(2);
        EmitOpcode(WasmOpcode.MultiplyInt32);
        EmitOpcode(WasmOpcode.End);
        EmitLocalSet(newCapacityLocal.Index);

        var cellSizeLocal = GetScratchI32(WasmScratchSlot.MapValue);
        EmitI32Const(ListCellSize);
        EmitLocalSet(cellSizeLocal.Index);

        var newAllocSize = GetScratchI32(WasmScratchSlot.StringCopyRemaining);
        EmitLocalGet(newCapacityLocal.Index);
        EmitLocalGet(cellSizeLocal.Index);
        EmitOpcode(WasmOpcode.MultiplyInt32);
        EmitI32Const(ListHeaderSize);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(newAllocSize.Index);

        EmitHeapAllocDynamic(newAllocSize, 4);
        var newListLocal = GetScratchI32(WasmScratchSlot.MapNewNode);
        EmitLocalSet(newListLocal.Index);

        EmitLocalGet(newListLocal.Index);
        EmitLocalGet(listLocal.Index);
        EmitI32Load();
        EmitI32Store();

        EmitLocalGet(newListLocal.Index);
        EmitLocalGet(countLocal.Index);
        EmitI32Store(offset: 4);

        EmitLocalGet(newListLocal.Index);
        EmitLocalGet(newCapacityLocal.Index);
        EmitI32Store(offset: 8);

        var destCursorLocal = GetScratchI32(WasmScratchSlot.InstanceReceiver);
        EmitLocalGet(newListLocal.Index);
        EmitI32Const(ListHeaderSize);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(destCursorLocal.Index);

        var sourceCursorLocal = GetScratchI32(WasmScratchSlot.StringCopySource);
        EmitLocalGet(listLocal.Index);
        EmitI32Const(ListHeaderSize);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(sourceCursorLocal.Index);

        var remainingLocal = GetScratchI32(WasmScratchSlot.StringCopyRemaining);
        EmitLocalGet(countLocal.Index);
        EmitLocalGet(cellSizeLocal.Index);
        EmitOpcode(WasmOpcode.MultiplyInt32);
        EmitLocalSet(remainingLocal.Index);

        var chunkLocal = GetScratchI32(WasmScratchSlot.StringCopyChunk);

        EmitBlock();
        EmitLoop();
        EmitLocalGet(remainingLocal.Index);
        EmitI32Const(4);
        EmitOpcode(WasmOpcode.LessThanInt32);
        EmitBrIf(1);

        EmitLocalGet(sourceCursorLocal.Index);
        EmitI32Load();
        EmitLocalSet(chunkLocal.Index);

        EmitLocalGet(destCursorLocal.Index);
        EmitLocalGet(chunkLocal.Index);
        EmitI32Store();

        EmitLocalGet(sourceCursorLocal.Index);
        EmitI32Const(4);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(sourceCursorLocal.Index);

        EmitLocalGet(destCursorLocal.Index);
        EmitI32Const(4);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(destCursorLocal.Index);

        EmitLocalGet(remainingLocal.Index);
        EmitI32Const(4);
        EmitOpcode(WasmOpcode.SubtractInt32);
        EmitLocalSet(remainingLocal.Index);

        EmitBr(0);
        EmitEnd();
        EmitEnd();

        EmitLocalGet(newListLocal.Index);
        EmitLocalSet(listLocal.Index);

        EmitOpcode(WasmOpcode.End);

        var elemAddrLocal = GetScratchI32(WasmScratchSlot.MapValue);
        EmitLocalGet(listLocal.Index);
        EmitI32Const(ListHeaderSize);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalGet(countLocal.Index);
        EmitI32Const(ListCellSize);
        EmitOpcode(WasmOpcode.MultiplyInt32);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(elemAddrLocal.Index);

        if (elementType.Kind == ValueKind.F64)
        {
            EmitLocalGet(elemAddrLocal.Index);
            EmitLocalGet(valueLocalF64.Index);
            EmitF64Store(offset: 0);
        }
        else
        {
            EmitLocalGet(elemAddrLocal.Index);
            EmitLocalGet(valueLocalI32.Index);
            EmitI32Store(offset: 0);
        }

        EmitLocalGet(elemAddrLocal.Index);
        EmitI32Const(GetValueTag(elementType));
        EmitI32Store(offset: 12);

        EmitLocalGet(listLocal.Index);
        EmitLocalGet(countLocal.Index);
        EmitI32Const(1);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitI32Store(offset: 4);

        EmitLocalGet(listLocal.Index);
    }

    private ValueType? EmitListMemberCall(Expression targetExpr, ValueType listType, string member, IReadOnlyList<Expression> arguments)
    {
        var listInfo = listType.List;
        if (listInfo is null)
            throw new NotSupportedException("List metadata missing in wasm backend.");

        var elementType = listInfo.ElementType;

        var listPtrLocal = GetScratchI32(14);
        EmitLocalSet(listPtrLocal.Index);

        var assignableTarget = ResolveListAssignmentTarget(targetExpr);

        switch (member)
        {
            case "append":
                {
                    EnsureArgumentCount(arguments, 1);
                    var valueType = RequireValue(arguments[0]);
                    Coerce(valueType, elementType);

                    if (elementType.Kind == ValueKind.F64)
                    {
                        var v = GetScratchF64(1);
                        EmitLocalSet(v.Index);
                        EmitLocalGet(listPtrLocal.Index);
                        EmitLocalGet(v.Index);
                    }
                    else
                    {
                        var v = GetScratchI32(15);
                        EmitLocalSet(v.Index);
                        EmitLocalGet(listPtrLocal.Index);
                        EmitLocalGet(v.Index);
                    }

                    EmitListAppend(elementType);

                    if (assignableTarget.IsValid)
                    {
                        EmitLocalSet(listPtrLocal.Index);
                        StoreListAssignmentTarget(assignableTarget, listPtrLocal);
                        EmitLocalGet(listPtrLocal.Index);
                    }

                    if (targetExpr is IdentifierExpr aliasId && _listAliases.TryGetValue(aliasId.Name, out var alias))
                    {
                        EmitMapAliasUpdate(alias, listPtrLocal);
                    }

                    return new ValueType(ValueKind.List, null, null, listInfo);
                }

            case "get":
                {
                    EnsureArgumentCount(arguments, 1);
                    var indexType = RequireValue(arguments[0]);
                    Coerce(indexType, ValueType.I32);
                    var idxLocal = GetScratchI32(WasmScratchSlot.ListIndex);
                    EmitLocalSet(idxLocal.Index);

                    var addrLocal = GetScratchI32(WasmScratchSlot.ListCurrent);
                    EmitLocalGet(listPtrLocal.Index);
                    EmitI32Const(ListHeaderSize);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalGet(idxLocal.Index);
                    EmitI32Const(ListCellSize);
                    EmitOpcode(WasmOpcode.MultiplyInt32);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalSet(addrLocal.Index);

                    EmitLocalGet(addrLocal.Index);
                    return LoadListElement(elementType);
                }

            case "set":
                {
                    EnsureArgumentCount(arguments, 2);
                    var indexType = RequireValue(arguments[0]);
                    Coerce(indexType, ValueType.I32);
                    var idxLocal = GetScratchI32(WasmScratchSlot.ListIndex);
                    EmitLocalSet(idxLocal.Index);

                    LocalInfo storedValueLocal;
                    if (elementType.Kind == ValueKind.F64)
                    {
                        storedValueLocal = GetScratchF64(1);
                        var valType = RequireValue(arguments[1]);
                        Coerce(valType, elementType);
                        EmitLocalSet(storedValueLocal.Index);
                    }
                    else
                    {
                        storedValueLocal = GetScratchI32(15);
                        var valType = RequireValue(arguments[1]);
                        Coerce(valType, elementType);
                        EmitLocalSet(storedValueLocal.Index);
                    }

                    var addrLocal = GetScratchI32(WasmScratchSlot.ListCurrent);
                    EmitLocalGet(listPtrLocal.Index);
                    EmitI32Const(ListHeaderSize);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalGet(idxLocal.Index);
                    EmitI32Const(ListCellSize);
                    EmitOpcode(WasmOpcode.MultiplyInt32);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalSet(addrLocal.Index);

                    EmitLocalGet(addrLocal.Index);
                    if (elementType.Kind == ValueKind.F64)
                    {
                        EmitLocalGet(storedValueLocal.Index);
                        EmitF64Store(offset: 0);
                    }
                    else
                    {
                        EmitLocalGet(storedValueLocal.Index);
                        EmitI32Store(offset: 0);
                    }

                    EmitLocalGet(addrLocal.Index);
                    EmitI32Const(GetValueTag(elementType));
                    EmitI32Store(offset: 12);

                    return null;
                }

            case "Slice":
                {
                    if (arguments.Count is < 1 or > 2)
                        throw new NotSupportedException("List.Slice expects one or two arguments in wasm backend.");

                    var startType = RequireValue(arguments[0]);
                    Coerce(startType, ValueType.I32);
                    var startLocal = GetScratchI32(WasmScratchSlot.ListSliceStart);
                    EmitLocalSet(startLocal.Index);

                    var countLocal = GetScratchI32(WasmScratchSlot.ListLengthCount);
                    EmitLocalGet(listPtrLocal.Index);
                    EmitI32Load(offset: 4);
                    EmitLocalSet(countLocal.Index);

                    EmitLocalGet(startLocal.Index);
                    EmitI32Const(0);
                    EmitOpcode(WasmOpcode.LessThanInt32);
                    EmitOpcode(WasmOpcode.If);
                    _body.WriteByte((byte)WasmControl.Void);
                    EmitI32Const(0);
                    EmitLocalSet(startLocal.Index);
                    EmitOpcode(WasmOpcode.End);

                    EmitLocalGet(startLocal.Index);
                    EmitLocalGet(countLocal.Index);
                    EmitOpcode(WasmOpcode.GreaterThanUInt32);
                    EmitOpcode(WasmOpcode.If);
                    _body.WriteByte((byte)WasmControl.Void);
                    EmitLocalGet(countLocal.Index);
                    EmitLocalSet(startLocal.Index);
                    EmitOpcode(WasmOpcode.End);

                    var endLocal = GetScratchI32(WasmScratchSlot.ListSliceEnd);
                    var lengthLocal = GetScratchI32(WasmScratchSlot.ListSliceLength);

                    if (arguments.Count == 2)
                    {
                        var lengthType = RequireValue(arguments[1]);
                        Coerce(lengthType, ValueType.I32);
                        EmitLocalSet(lengthLocal.Index);

                        EmitLocalGet(lengthLocal.Index);
                        EmitI32Const(0);
                        EmitOpcode(WasmOpcode.LessThanInt32);
                        EmitOpcode(WasmOpcode.If);
                        _body.WriteByte((byte)WasmControl.Void);
                        EmitI32Const(0);
                        EmitLocalSet(lengthLocal.Index);
                        EmitOpcode(WasmOpcode.End);

                        EmitLocalGet(startLocal.Index);
                        EmitLocalGet(lengthLocal.Index);
                        EmitOpcode(WasmOpcode.AddInt32);
                        EmitLocalSet(endLocal.Index);

                        EmitLocalGet(endLocal.Index);
                        EmitLocalGet(countLocal.Index);
                        EmitOpcode(WasmOpcode.GreaterThanUInt32);
                        EmitOpcode(WasmOpcode.If);
                        _body.WriteByte((byte)WasmControl.Void);
                        EmitLocalGet(countLocal.Index);
                        EmitLocalSet(endLocal.Index);
                        EmitOpcode(WasmOpcode.End);

                        EmitLocalGet(endLocal.Index);
                        EmitLocalGet(startLocal.Index);
                        EmitOpcode(WasmOpcode.SubtractInt32);
                        EmitLocalSet(lengthLocal.Index);
                    }
                    else
                    {
                        EmitLocalGet(countLocal.Index);
                        EmitLocalSet(endLocal.Index);

                        EmitLocalGet(endLocal.Index);
                        EmitLocalGet(startLocal.Index);
                        EmitOpcode(WasmOpcode.SubtractInt32);
                        EmitLocalSet(lengthLocal.Index);
                    }

                    EmitLocalGet(endLocal.Index);
                    EmitLocalGet(startLocal.Index);
                    EmitOpcode(WasmOpcode.LessThanUInt32);
                    EmitOpcode(WasmOpcode.If);
                    _body.WriteByte((byte)WasmControl.Void);
                    EmitLocalGet(startLocal.Index);
                    EmitLocalSet(endLocal.Index);
                    EmitLocalGet(endLocal.Index);
                    EmitLocalGet(startLocal.Index);
                    EmitOpcode(WasmOpcode.SubtractInt32);
                    EmitLocalSet(lengthLocal.Index);
                    EmitOpcode(WasmOpcode.End);

                    var allocSizeLocal = GetScratchI32(WasmScratchSlot.ListSliceAllocSize);
                    EmitLocalGet(lengthLocal.Index);
                    EmitI32Const(ListCellSize);
                    EmitOpcode(WasmOpcode.MultiplyInt32);
                    EmitI32Const(ListHeaderSize);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalSet(allocSizeLocal.Index);

                    EmitHeapAllocDynamic(allocSizeLocal, 4);
                    var newListLocal = GetScratchI32(WasmScratchSlot.ListSliceResult);
                    EmitLocalSet(newListLocal.Index);

                    EmitLocalGet(newListLocal.Index);
                    EmitI32Const(GetValueTag(elementType));
                    EmitI32Store(offset: 0);

                    EmitLocalGet(newListLocal.Index);
                    EmitI32Const(0);
                    EmitI32Store(offset: 4);

                    EmitLocalGet(newListLocal.Index);
                    EmitLocalGet(lengthLocal.Index);
                    EmitI32Store(offset: 8);

                    var srcBaseLocal = GetScratchI32(WasmScratchSlot.ListSliceSourceBase);
                    EmitLocalGet(listPtrLocal.Index);
                    EmitI32Const(ListHeaderSize);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalGet(startLocal.Index);
                    EmitI32Const(ListCellSize);
                    EmitOpcode(WasmOpcode.MultiplyInt32);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalSet(srcBaseLocal.Index);

                    var destBaseLocal = GetScratchI32(WasmScratchSlot.ListSliceDestBase);
                    EmitLocalGet(newListLocal.Index);
                    EmitI32Const(ListHeaderSize);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalSet(destBaseLocal.Index);

                    var bytesLocal = GetScratchI32(WasmScratchSlot.StringCopyRemaining);
                    EmitLocalGet(lengthLocal.Index);
                    EmitI32Const(ListCellSize);
                    EmitOpcode(WasmOpcode.MultiplyInt32);
                    EmitLocalSet(bytesLocal.Index);

                    var srcCursorLocal = GetScratchI32(WasmScratchSlot.StringCopySource);
                    EmitLocalGet(srcBaseLocal.Index);
                    EmitLocalSet(srcCursorLocal.Index);

                    var destCursorLocal = GetScratchI32(WasmScratchSlot.InstanceReceiver);
                    EmitLocalGet(destBaseLocal.Index);
                    EmitLocalSet(destCursorLocal.Index);

                    var chunkLocal = GetScratchI32(WasmScratchSlot.StringCopyChunk);

                    EmitBlock();
                    EmitLoop();
                    EmitLocalGet(bytesLocal.Index);
                    EmitI32Const(4);
                    EmitOpcode(WasmOpcode.LessThanUInt32);
                    EmitBrIf(1);

                    EmitLocalGet(srcCursorLocal.Index);
                    EmitI32Load();
                    EmitLocalSet(chunkLocal.Index);

                    EmitLocalGet(destCursorLocal.Index);
                    EmitLocalGet(chunkLocal.Index);
                    EmitI32Store();

                    EmitLocalGet(srcCursorLocal.Index);
                    EmitI32Const(4);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalSet(srcCursorLocal.Index);

                    EmitLocalGet(destCursorLocal.Index);
                    EmitI32Const(4);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalSet(destCursorLocal.Index);

                    EmitLocalGet(bytesLocal.Index);
                    EmitI32Const(4);
                    EmitOpcode(WasmOpcode.SubtractInt32);
                    EmitLocalSet(bytesLocal.Index);

                    EmitBr(0);
                    EmitEnd();
                    EmitEnd();

                    EmitLocalGet(bytesLocal.Index);
                    EmitI32Const(0);
                    EmitOpcode(WasmOpcode.EqualInt32);
                    EmitOpcode(WasmOpcode.If);
                    _body.WriteByte((byte)WasmControl.Void);
                    EmitOpcode(WasmOpcode.Else);

                    EmitI32Const(0);
                    EmitLocalSet(chunkLocal.Index);

                    EmitBlock();
                    EmitLoop();
                    EmitLocalGet(chunkLocal.Index);
                    EmitLocalGet(bytesLocal.Index);
                    EmitOpcode(WasmOpcode.EqualInt32);
                    EmitBrIf(1);

                    EmitLocalGet(destCursorLocal.Index);
                    EmitLocalGet(chunkLocal.Index);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalGet(srcCursorLocal.Index);
                    EmitLocalGet(chunkLocal.Index);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitI32Load8Unsigned();
                    EmitI32Store8();

                    EmitLocalGet(chunkLocal.Index);
                    EmitI32Const(1);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalSet(chunkLocal.Index);

                    EmitBr(0);
                    EmitEnd();
                    EmitEnd();

                    EmitLocalGet(srcCursorLocal.Index);
                    EmitLocalGet(bytesLocal.Index);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalSet(srcCursorLocal.Index);

                    EmitLocalGet(destCursorLocal.Index);
                    EmitLocalGet(bytesLocal.Index);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalSet(destCursorLocal.Index);

                    EmitOpcode(WasmOpcode.End);

                    EmitLocalGet(newListLocal.Index);
                    EmitLocalGet(lengthLocal.Index);
                    EmitI32Store(offset: 4);

                    EmitLocalGet(newListLocal.Index);
                    return new ValueType(ValueKind.List, null, null, listInfo);
                }

            case "toArray":
                {
                    EnsureArgumentCount(arguments, 0);

                    var countLocal = GetScratchI32(WasmScratchSlot.ListLengthCount);
                    EmitLocalGet(listPtrLocal.Index);
                    EmitI32Load(offset: 4);
                    EmitLocalSet(countLocal.Index);

                    var arrayInfo = new ArrayInfo(elementType);
                    var elementSize = GetElementSize(elementType);
                    var alignment = GetElementAlignment(elementType);

                    const uint arrayHeaderSize = 16;
                    EmitHeapAlloc(arrayHeaderSize, 4);
                    var headerLocal = GetScratchI32(WasmScratchSlot.ArraySliceHeader);
                    EmitLocalSet(headerLocal.Index);

                    var dataSizeLocal = GetScratchI32(WasmScratchSlot.ArraySliceDataSize);
                    EmitLocalGet(countLocal.Index);
                    EmitI32Const((int)elementSize);
                    EmitOpcode(WasmOpcode.MultiplyInt32);
                    EmitLocalSet(dataSizeLocal.Index);

                    EmitHeapAllocDynamic(dataSizeLocal, alignment);
                    var dataPtrLocal = GetScratchI32(WasmScratchSlot.ArraySliceDataPtr);
                    EmitLocalSet(dataPtrLocal.Index);

                    EmitLocalGet(headerLocal.Index);
                    EmitLocalGet(countLocal.Index);
                    EmitI32Store(offset: 0);

                    EmitLocalGet(headerLocal.Index);
                    EmitI32Const(arrayInfo.ElementTag);
                    EmitI32Store(offset: 4);

                    EmitLocalGet(headerLocal.Index);
                    EmitLocalGet(dataPtrLocal.Index);
                    EmitI32Store(offset: 8);

                    EmitLocalGet(headerLocal.Index);
                    EmitI32Const((int)elementSize);
                    EmitI32Store(offset: 12);

                    var indexLocal = GetScratchI32(WasmScratchSlot.ListSliceIndex);
                    EmitI32Const(0);
                    EmitLocalSet(indexLocal.Index);

                    var srcBaseLocal = GetScratchI32(WasmScratchSlot.ListSliceSourceBase);
                    EmitLocalGet(listPtrLocal.Index);
                    EmitI32Const(ListHeaderSize);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalSet(srcBaseLocal.Index);

                    var cellAddrLocal = GetScratchI32(WasmScratchSlot.ListSliceDestBase);
                    var destAddrLocal = GetScratchI32(WasmScratchSlot.ArraySliceIndex);

                    EmitBlock();
                    EmitLoop();
                    EmitLocalGet(indexLocal.Index);
                    EmitLocalGet(countLocal.Index);
                    EmitOpcode(WasmOpcode.LessThanUInt32);
                    EmitOpcode(WasmOpcode.EqualZeroInt32);
                    EmitBrIf(1);

                    EmitLocalGet(srcBaseLocal.Index);
                    EmitLocalGet(indexLocal.Index);
                    EmitI32Const(ListCellSize);
                    EmitOpcode(WasmOpcode.MultiplyInt32);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalSet(cellAddrLocal.Index);

                    EmitLocalGet(dataPtrLocal.Index);
                    EmitLocalGet(indexLocal.Index);
                    EmitI32Const((int)elementSize);
                    EmitOpcode(WasmOpcode.MultiplyInt32);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalSet(destAddrLocal.Index);

                    EmitLocalGet(destAddrLocal.Index);
                    EmitLocalGet(cellAddrLocal.Index);
                    var storedType = LoadListElement(elementType);
                    if (storedType.Kind == ValueKind.F64)
                    {
                        EmitF64Store();
                    }
                    else
                    {
                        EmitI32Store();
                    }

                    EmitLocalGet(indexLocal.Index);
                    EmitI32Const(1);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalSet(indexLocal.Index);

                    EmitBr(0);
                    EmitEnd();
                    EmitEnd();

                    EmitLocalGet(headerLocal.Index);
                    return new ValueType(ValueKind.Array, new ArrayInfo(elementType));
                }

            case "head":
                {
                    EnsureArgumentCount(arguments, 0);
                    var addrLocal = GetScratchI32(WasmScratchSlot.ListCurrent);
                    EmitLocalGet(listPtrLocal.Index);
                    EmitI32Const(ListHeaderSize);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalSet(addrLocal.Index);
                    EmitLocalGet(addrLocal.Index);
                    return LoadListElement(elementType);
                }

            case "tail":
                {
                    EnsureArgumentCount(arguments, 0);

                    var countLocal = GetScratchI32(WasmScratchSlot.ListLengthCount);
                    EmitLocalGet(listPtrLocal.Index);
                    EmitI32Load(offset: 4);
                    EmitLocalSet(countLocal.Index);

                    var newCountLocal = GetScratchI32(WasmScratchSlot.MapKey);
                    EmitLocalGet(countLocal.Index);
                    EmitI32Const(1);
                    EmitOpcode(WasmOpcode.SubtractInt32);
                    EmitLocalSet(newCountLocal.Index);

                    EmitLocalGet(newCountLocal.Index);
                    EmitI32Const(0);
                    EmitOpcode(WasmOpcode.LessThanInt32);
                    EmitOpcode(WasmOpcode.If);
                    _body.WriteByte((byte)WasmControl.Void);
                    EmitI32Const(0);
                    EmitLocalSet(newCountLocal.Index);
                    EmitOpcode(WasmOpcode.End);

                    EmitList(elementType, 0);
                    var newListLocal = GetScratchI32(WasmScratchSlot.MapNewNode);
                    EmitLocalSet(newListLocal.Index);

                    EmitLocalGet(newListLocal.Index);
                    EmitLocalGet(listPtrLocal.Index);
                    EmitI32Load();
                    EmitI32Store();

                    EmitLocalGet(newListLocal.Index);
                    EmitLocalGet(newCountLocal.Index);
                    EmitI32Store(offset: 4);

                    EmitLocalGet(newListLocal.Index);
                    EmitLocalGet(newCountLocal.Index);
                    EmitI32Store(offset: 8);

                    var srcBase = GetScratchI32(WasmScratchSlot.StringCopySource);
                    var dstBase = GetScratchI32(WasmScratchSlot.InstanceReceiver);

                    EmitLocalGet(listPtrLocal.Index);
                    EmitI32Const(ListHeaderSize);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitI32Const(ListCellSize);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalSet(srcBase.Index);

                    EmitLocalGet(newListLocal.Index);
                    EmitI32Const(ListHeaderSize);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalSet(dstBase.Index);

                    var bytesLocal = GetScratchI32(WasmScratchSlot.StringCopyRemaining);
                    EmitLocalGet(newCountLocal.Index);
                    EmitI32Const(ListCellSize);
                    EmitOpcode(WasmOpcode.MultiplyInt32);
                    EmitLocalSet(bytesLocal.Index);

                    var chunkLocal = GetScratchI32(WasmScratchSlot.StringCopyChunk);

                    EmitBlock();
                    EmitLoop();
                    EmitLocalGet(bytesLocal.Index);
                    EmitI32Const(4);
                    EmitOpcode(WasmOpcode.LessThanInt32);
                    EmitBrIf(1);

                    EmitLocalGet(srcBase.Index);
                    EmitI32Load();
                    EmitLocalSet(chunkLocal.Index);

                    EmitLocalGet(dstBase.Index);
                    EmitLocalGet(chunkLocal.Index);
                    EmitI32Store();

                    EmitLocalGet(srcBase.Index);
                    EmitI32Const(4);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalSet(srcBase.Index);

                    EmitLocalGet(dstBase.Index);
                    EmitI32Const(4);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalSet(dstBase.Index);

                    EmitLocalGet(bytesLocal.Index);
                    EmitI32Const(4);
                    EmitOpcode(WasmOpcode.SubtractInt32);
                    EmitLocalSet(bytesLocal.Index);

                    EmitBr(0);
                    EmitEnd();
                    EmitEnd();

                    EmitLocalGet(newListLocal.Index);
                    return new ValueType(ValueKind.List, null, null, listInfo);
                }

            case "Length":
                {
                    EnsureArgumentCount(arguments, 0);
                    EmitLocalGet(listPtrLocal.Index);
                    EmitI32Load(offset: 4);
                    return ValueType.I32;
                }

            case "NotEqual":
                {
                    EnsureArgumentCount(arguments, 1);
                    var otherType = RequireValue(arguments[0]);
                    Coerce(otherType, listType);
                    var otherLocal = GetScratchI32(WasmScratchSlot.ListCompareOther);
                    EmitLocalSet(otherLocal.Index);
                    EmitLocalGet(listPtrLocal.Index);
                    EmitLocalGet(otherLocal.Index);
                    EmitOpcode(WasmOpcode.NotEqualInt32);
                    return ValueType.Bool;
                }

            case "pop":
                {
                    if (arguments.Count > 1)
                        throw new NotSupportedException("List.pop accepts at most one argument in wasm backend.");

                    var poppedElementType = listInfo.ElementType;

                    var indexLocal = GetScratchI32(WasmScratchSlot.ListPopIndex);
                    if (arguments.Count == 1)
                    {
                        var indexType = RequireValue(arguments[0]);
                        Coerce(indexType, ValueType.I32);
                        EmitLocalSet(indexLocal.Index);
                    }

                    EmitBlock(poppedElementType);

                    var countLocal = GetScratchI32(WasmScratchSlot.ListLengthCount);
                    EmitLocalGet(listPtrLocal.Index);
                    EmitI32Load(offset: 4);
                    EmitLocalSet(countLocal.Index);

                    EmitLocalGet(countLocal.Index);
                    EmitI32Const(0);
                    EmitOpcode(WasmOpcode.EqualInt32);
                    EmitOpcode(WasmOpcode.If);
                    _body.WriteByte((byte)WasmControl.Void);
                    EmitDefaultValue(poppedElementType);
                    EmitBr(1);
                    EmitOpcode(WasmOpcode.End);

                    if (arguments.Count == 0)
                    {
                        EmitLocalGet(countLocal.Index);
                        EmitI32Const(1);
                        EmitOpcode(WasmOpcode.SubtractInt32);
                        EmitLocalSet(indexLocal.Index);
                    }
                    else
                    {
                        EmitLocalGet(indexLocal.Index);
                        EmitI32Const(0);
                        EmitOpcode(WasmOpcode.LessThanInt32);
                        EmitOpcode(WasmOpcode.If);
                        _body.WriteByte((byte)WasmControl.Void);
                        EmitI32Const(0);
                        EmitLocalSet(indexLocal.Index);
                        EmitOpcode(WasmOpcode.End);
                    }

                    EmitLocalGet(indexLocal.Index);
                    EmitLocalGet(countLocal.Index);
                    EmitOpcode(WasmOpcode.LessThanUInt32);
                    EmitOpcode(WasmOpcode.If);
                    _body.WriteByte((byte)WasmControl.Void);
                    EmitOpcode(WasmOpcode.Else);
                    EmitLocalGet(countLocal.Index);
                    EmitI32Const(1);
                    EmitOpcode(WasmOpcode.SubtractInt32);
                    EmitLocalSet(indexLocal.Index);
                    EmitOpcode(WasmOpcode.End);

                    var baseLocal = GetScratchI32(WasmScratchSlot.InstanceReceiver);
                    EmitLocalGet(listPtrLocal.Index);
                    EmitI32Const(ListHeaderSize);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalSet(baseLocal.Index);

                    var elementAddrLocal = GetScratchI32(WasmScratchSlot.ListPopElementAddr);
                    EmitLocalGet(baseLocal.Index);
                    EmitLocalGet(indexLocal.Index);
                    EmitI32Const(ListCellSize);
                    EmitOpcode(WasmOpcode.MultiplyInt32);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalSet(elementAddrLocal.Index);

                    LocalInfo? resultI32Local = null;
                    LocalInfo? resultF64Local = null;
                    if (poppedElementType.Kind == ValueKind.F64)
                    {
                        resultF64Local = GetScratchF64(WasmScratchSlot.NumericLeftF64);
                        EmitLocalGet(elementAddrLocal.Index);
                        EmitF64Load();
                        EmitLocalSet(resultF64Local.Index);
                    }
                    else
                    {
                        resultI32Local = GetScratchI32(WasmScratchSlot.ListPopResult);
                        EmitLocalGet(elementAddrLocal.Index);
                        EmitI32Load();
                        EmitLocalSet(resultI32Local.Index);
                    }

                    var remainingLocal = GetScratchI32(WasmScratchSlot.ListPopRemaining);
                    EmitLocalGet(countLocal.Index);
                    EmitLocalGet(indexLocal.Index);
                    EmitOpcode(WasmOpcode.SubtractInt32);
                    EmitI32Const(1);
                    EmitOpcode(WasmOpcode.SubtractInt32);
                    EmitLocalSet(remainingLocal.Index);

                    EmitLocalGet(remainingLocal.Index);
                    EmitI32Const(0);
                    EmitOpcode(WasmOpcode.GreaterThanUInt32);
                    EmitOpcode(WasmOpcode.If);
                    _body.WriteByte((byte)WasmControl.Void);

                    var bytesLocal = GetScratchI32(WasmScratchSlot.ListPopBytes);
                    EmitLocalGet(remainingLocal.Index);
                    EmitI32Const(ListCellSize);
                    EmitOpcode(WasmOpcode.MultiplyInt32);
                    EmitLocalSet(bytesLocal.Index);

                    var srcCursorLocal = GetScratchI32(WasmScratchSlot.ListPopSrcBase);
                    EmitLocalGet(baseLocal.Index);
                    EmitLocalGet(indexLocal.Index);
                    EmitI32Const(ListCellSize);
                    EmitOpcode(WasmOpcode.MultiplyInt32);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitI32Const(ListCellSize);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalSet(srcCursorLocal.Index);

                    var destCursorLocal = GetScratchI32(WasmScratchSlot.ListPopDstBase);
                    EmitLocalGet(baseLocal.Index);
                    EmitLocalGet(indexLocal.Index);
                    EmitI32Const(ListCellSize);
                    EmitOpcode(WasmOpcode.MultiplyInt32);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalSet(destCursorLocal.Index);

                    var chunkLocal = GetScratchI32(WasmScratchSlot.StringCopyChunk);

                    EmitBlock();
                    EmitLoop();
                    EmitLocalGet(bytesLocal.Index);
                    EmitI32Const(4);
                    EmitOpcode(WasmOpcode.LessThanUInt32);
                    EmitBrIf(1);

                    EmitLocalGet(srcCursorLocal.Index);
                    EmitI32Load();
                    EmitLocalSet(chunkLocal.Index);

                    EmitLocalGet(destCursorLocal.Index);
                    EmitLocalGet(chunkLocal.Index);
                    EmitI32Store();

                    EmitLocalGet(srcCursorLocal.Index);
                    EmitI32Const(4);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalSet(srcCursorLocal.Index);

                    EmitLocalGet(destCursorLocal.Index);
                    EmitI32Const(4);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalSet(destCursorLocal.Index);

                    EmitLocalGet(bytesLocal.Index);
                    EmitI32Const(4);
                    EmitOpcode(WasmOpcode.SubtractInt32);
                    EmitLocalSet(bytesLocal.Index);

                    EmitBr(0);
                    EmitEnd();
                    EmitEnd();

                    EmitLocalGet(bytesLocal.Index);
                    EmitI32Const(0);
                    EmitOpcode(WasmOpcode.EqualInt32);
                    EmitOpcode(WasmOpcode.If);
                    _body.WriteByte((byte)WasmControl.Void);
                    EmitOpcode(WasmOpcode.Else);

                    EmitI32Const(0);
                    EmitLocalSet(chunkLocal.Index);

                    EmitBlock();
                    EmitLoop();
                    EmitLocalGet(chunkLocal.Index);
                    EmitLocalGet(bytesLocal.Index);
                    EmitOpcode(WasmOpcode.EqualInt32);
                    EmitBrIf(1);

                    EmitLocalGet(destCursorLocal.Index);
                    EmitLocalGet(chunkLocal.Index);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalGet(srcCursorLocal.Index);
                    EmitLocalGet(chunkLocal.Index);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitI32Load8Unsigned();
                    EmitI32Store8();

                    EmitLocalGet(chunkLocal.Index);
                    EmitI32Const(1);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalSet(chunkLocal.Index);

                    EmitBr(0);
                    EmitEnd();
                    EmitEnd();

                    EmitOpcode(WasmOpcode.End);
                    EmitOpcode(WasmOpcode.End);

                    EmitLocalGet(countLocal.Index);
                    EmitI32Const(1);
                    EmitOpcode(WasmOpcode.SubtractInt32);
                    EmitLocalSet(countLocal.Index);

                    EmitLocalGet(listPtrLocal.Index);
                    EmitLocalGet(countLocal.Index);
                    EmitI32Store(offset: 4);

                    EmitLocalGet(baseLocal.Index);
                    EmitLocalGet(countLocal.Index);
                    EmitI32Const(ListCellSize);
                    EmitOpcode(WasmOpcode.MultiplyInt32);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalSet(elementAddrLocal.Index);

                    if (poppedElementType.Kind == ValueKind.F64)
                    {
                        EmitLocalGet(elementAddrLocal.Index);
                        EmitF64Const(0.0);
                        EmitF64Store();
                    }
                    else
                    {
                        EmitLocalGet(elementAddrLocal.Index);
                        EmitI32Const(0);
                        EmitI32Store();
                    }

                    EmitLocalGet(elementAddrLocal.Index);
                    EmitI32Const(0);
                    EmitI32Store(offset: 12);

                    if (poppedElementType.Kind == ValueKind.F64)
                    {
                        EmitLocalGet(resultF64Local!.Index);
                    }
                    else
                    {
                        EmitLocalGet(resultI32Local!.Index);
                    }

                    EmitEnd();

                    return poppedElementType;
                }

            default:
                throw new NotSupportedException($"List member '{member}' is not supported in wasm backend.");
        }
    }

    private ValueType LoadListElement(ValueType elementType)
    {
        switch (elementType.Kind)
        {
            case ValueKind.F64:
                EmitF64Load();
                return elementType;
            default:
                EmitI32Load();
                return elementType;
        }
    }

    private readonly record struct ListAssignmentTarget(LocalInfo? Local, (LocalInfo Instance, ClassFieldEntry Field)? InstanceField)
    {
        public bool IsValid => Local is not null || InstanceField.HasValue;
    }

    private ListAssignmentTarget ResolveListAssignmentTarget(Expression targetExpr)
    {
        if (targetExpr is IdentifierExpr id && _locals.TryGetValue(id.Name, out var local))
            return new ListAssignmentTarget(local, null);

        if (targetExpr is MemberAccessExpr memberAccess && TryResolveInstanceFieldAssignment(memberAccess, out var instanceLocal, out var fieldEntry))
            return new ListAssignmentTarget(null, (instanceLocal, fieldEntry));

        return default;
    }

    private void StoreListAssignmentTarget(ListAssignmentTarget target, LocalInfo pointerLocal)
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

    private void EmitMapAliasUpdate(MapValueAlias alias, LocalInfo pointerLocal)
    {
        var lookup = EmitMapLookupForAlias(alias);
        StoreMapValueAtNode(lookup.NodeLocal, alias.MapInfo.ValueType, pointerLocal);
    }
}
