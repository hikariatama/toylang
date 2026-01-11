using ToyLang.Syntax;

namespace ToyLang.Wasm;

internal sealed partial class FunctionCompiler
{
    private ValueType EmitArrayConstructor(IReadOnlyList<TypeRef> typeArguments, IReadOnlyList<Expression> arguments)
    {
        if (typeArguments.Count != 1)
            throw new NotSupportedException("Array constructor requires a single element type argument.");

        EnsureArgumentCount(arguments, 1);

        var elementType = ValueType.MapValueType(typeArguments[0]);
        var arrayInfo = new ArrayInfo(elementType);
        var lengthType = RequireValue(arguments[0]);
        Coerce(lengthType, ValueType.I32);

        var lengthLocal = GetScratchI32(2);
        EmitLocalSet(lengthLocal.Index);

        const uint headerSize = 16;
        EmitHeapAlloc(headerSize, 4);
        var headerLocal = GetScratchI32(3);
        EmitLocalSet(headerLocal.Index);

        EmitLocalGet(lengthLocal.Index);
        var elementSize = GetElementSize(elementType);
        if (elementSize != 1)
        {
            EmitI32Const((int)elementSize);
            EmitOpcode(WasmOpcode.MultiplyInt32);
        }

        var dataSizeLocal = GetScratchI32(4);
        EmitLocalSet(dataSizeLocal.Index);

        var alignment = GetElementAlignment(elementType);
        EmitHeapAllocDynamic(dataSizeLocal, alignment);

        var dataPtrLocal = GetScratchI32(5);
        EmitLocalSet(dataPtrLocal.Index);

        EmitLocalGet(headerLocal.Index);
        EmitLocalGet(lengthLocal.Index);
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

        EmitLocalGet(headerLocal.Index);
        return new ValueType(ValueKind.Array, arrayInfo);
    }

    private ValueType? EmitArrayMemberCall(ValueType arrayType, string member, IReadOnlyList<Expression> arguments)
    {
        var arrayInfo = arrayType.Array;
        if (arrayInfo is null)
            throw new NotSupportedException("Array metadata missing in wasm backend.");

        var elementType = arrayInfo.ElementType;
        var elementSize = GetElementSize(elementType);

        var arrayPtrLocal = GetScratchI32(member == "set" ? WasmScratchSlot.ArraySetPointer : WasmScratchSlot.Temporary);
        EmitLocalSet(arrayPtrLocal.Index);

        switch (member)
        {
            case "get":
                {
                    EnsureArgumentCount(arguments, 1);
                    var indexType = RequireValue(arguments[0]);
                    Coerce(indexType, ValueType.I32);
                    var indexLocal = GetScratchI32(3);
                    EmitLocalSet(indexLocal.Index);

                    EmitLocalGet(arrayPtrLocal.Index);
                    EmitI32Load(8);
                    EmitLocalGet(indexLocal.Index);
                    EmitI32Const((int)elementSize);
                    EmitOpcode(WasmOpcode.MultiplyInt32);
                    EmitOpcode(WasmOpcode.AddInt32);

                    return LoadArrayElement(elementType);
                }
            case "set":
                {
                    EnsureArgumentCount(arguments, 2);
                    var indexType = RequireValue(arguments[0]);
                    Coerce(indexType, ValueType.I32);
                    var indexLocal = GetScratchI32(WasmScratchSlot.ArraySetIndex);
                    EmitLocalSet(indexLocal.Index);

                    var valueType = RequireValue(arguments[1]);
                    _ = Coerce(valueType, elementType);

                    if (elementType.Kind == ValueKind.F64)
                    {
                        var valueLocal = GetScratchF64(0);
                        EmitLocalSet(valueLocal.Index);

                        EmitLocalGet(arrayPtrLocal.Index);
                        EmitI32Load(8);
                        EmitLocalGet(indexLocal.Index);
                        EmitI32Const((int)elementSize);
                        EmitOpcode(WasmOpcode.MultiplyInt32);
                        EmitOpcode(WasmOpcode.AddInt32);
                        EmitLocalGet(valueLocal.Index);
                        EmitF64Store();
                    }
                    else
                    {
                        var valueLocal = GetScratchI32(WasmScratchSlot.ArraySetValue);
                        EmitLocalSet(valueLocal.Index);

                        EmitLocalGet(arrayPtrLocal.Index);
                        EmitI32Load(8);
                        EmitLocalGet(indexLocal.Index);
                        EmitI32Const((int)elementSize);
                        EmitOpcode(WasmOpcode.MultiplyInt32);
                        EmitOpcode(WasmOpcode.AddInt32);
                        EmitLocalGet(valueLocal.Index);
                        EmitI32Store();
                    }

                    return null;
                }
            case "Length":
                {
                    EnsureArgumentCount(arguments, 0);
                    EmitLocalGet(arrayPtrLocal.Index);
                    EmitI32Load(0);
                    return ValueType.I32;
                }
            case "Slice":
                {
                    if (arguments.Count is < 1 or > 2)
                        throw new NotSupportedException("Array.Slice expects one or two arguments in wasm backend.");

                    var startType = RequireValue(arguments[0]);
                    Coerce(startType, ValueType.I32);
                    var startLocal = GetScratchI32(WasmScratchSlot.ArraySliceStart);
                    EmitLocalSet(startLocal.Index);

                    var totalLengthLocal = GetScratchI32(WasmScratchSlot.ArraySliceTotalLength);
                    EmitLocalGet(arrayPtrLocal.Index);
                    EmitI32Load(0);
                    EmitLocalSet(totalLengthLocal.Index);

                    EmitLocalGet(startLocal.Index);
                    EmitI32Const(0);
                    EmitOpcode(WasmOpcode.LessThanInt32);
                    EmitOpcode(WasmOpcode.If);
                    _body.WriteByte((byte)WasmControl.Void);
                    EmitI32Const(0);
                    EmitLocalSet(startLocal.Index);
                    EmitOpcode(WasmOpcode.End);

                    EmitLocalGet(startLocal.Index);
                    EmitLocalGet(totalLengthLocal.Index);
                    EmitOpcode(WasmOpcode.GreaterThanUInt32);
                    EmitOpcode(WasmOpcode.If);
                    _body.WriteByte((byte)WasmControl.Void);
                    EmitLocalGet(totalLengthLocal.Index);
                    EmitLocalSet(startLocal.Index);
                    EmitOpcode(WasmOpcode.End);

                    var endLocal = GetScratchI32(WasmScratchSlot.ArraySliceEnd);
                    var lengthLocal = GetScratchI32(WasmScratchSlot.ArraySliceLength);

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
                        EmitLocalGet(totalLengthLocal.Index);
                        EmitOpcode(WasmOpcode.GreaterThanUInt32);
                        EmitOpcode(WasmOpcode.If);
                        _body.WriteByte((byte)WasmControl.Void);
                        EmitLocalGet(totalLengthLocal.Index);
                        EmitLocalSet(endLocal.Index);
                        EmitOpcode(WasmOpcode.End);

                        EmitLocalGet(endLocal.Index);
                        EmitLocalGet(startLocal.Index);
                        EmitOpcode(WasmOpcode.SubtractInt32);
                        EmitLocalSet(lengthLocal.Index);
                    }
                    else
                    {
                        EmitLocalGet(totalLengthLocal.Index);
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

                    const uint headerSize = 16;
                    EmitHeapAlloc(headerSize, 4);
                    var newHeaderLocal = GetScratchI32(WasmScratchSlot.ArraySliceHeader);
                    EmitLocalSet(newHeaderLocal.Index);

                    var sliceBytesLocal = GetScratchI32(WasmScratchSlot.ArraySliceDataSize);
                    EmitLocalGet(lengthLocal.Index);
                    EmitI32Const((int)elementSize);
                    EmitOpcode(WasmOpcode.MultiplyInt32);
                    EmitLocalSet(sliceBytesLocal.Index);

                    var alignment = GetElementAlignment(elementType);
                    EmitHeapAllocDynamic(sliceBytesLocal, alignment);
                    var newDataPtrLocal = GetScratchI32(WasmScratchSlot.ArraySliceDataPtr);
                    EmitLocalSet(newDataPtrLocal.Index);

                    EmitLocalGet(newHeaderLocal.Index);
                    EmitLocalGet(lengthLocal.Index);
                    EmitI32Store(offset: 0);

                    EmitLocalGet(newHeaderLocal.Index);
                    EmitI32Const(arrayInfo.ElementTag);
                    EmitI32Store(offset: 4);

                    EmitLocalGet(newHeaderLocal.Index);
                    EmitLocalGet(newDataPtrLocal.Index);
                    EmitI32Store(offset: 8);

                    EmitLocalGet(newHeaderLocal.Index);
                    EmitI32Const((int)elementSize);
                    EmitI32Store(offset: 12);

                    var srcBaseLocal = GetScratchI32(WasmScratchSlot.ArraySliceSourceBase);
                    EmitLocalGet(arrayPtrLocal.Index);
                    EmitI32Load(8);
                    EmitLocalGet(startLocal.Index);
                    EmitI32Const((int)elementSize);
                    EmitOpcode(WasmOpcode.MultiplyInt32);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalSet(srcBaseLocal.Index);

                    var destBaseLocal = GetScratchI32(WasmScratchSlot.ArraySliceDestBase);
                    EmitLocalGet(newDataPtrLocal.Index);
                    EmitLocalSet(destBaseLocal.Index);

                    var remainingBytesLocal = GetScratchI32(WasmScratchSlot.StringCopyRemaining);
                    EmitLocalGet(sliceBytesLocal.Index);
                    EmitLocalSet(remainingBytesLocal.Index);

                    var srcCursorLocal = GetScratchI32(WasmScratchSlot.StringCopySource);
                    EmitLocalGet(srcBaseLocal.Index);
                    EmitLocalSet(srcCursorLocal.Index);

                    var destCursorLocal = GetScratchI32(WasmScratchSlot.InstanceReceiver);
                    EmitLocalGet(destBaseLocal.Index);
                    EmitLocalSet(destCursorLocal.Index);

                    var chunkLocal = GetScratchI32(WasmScratchSlot.StringCopyChunk);

                    EmitBlock();
                    EmitLoop();
                    EmitLocalGet(remainingBytesLocal.Index);
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

                    EmitLocalGet(remainingBytesLocal.Index);
                    EmitI32Const(4);
                    EmitOpcode(WasmOpcode.SubtractInt32);
                    EmitLocalSet(remainingBytesLocal.Index);

                    EmitBr(0);
                    EmitEnd();
                    EmitEnd();

                    EmitLocalGet(remainingBytesLocal.Index);
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
                    EmitLocalGet(remainingBytesLocal.Index);
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
                    EmitLocalGet(remainingBytesLocal.Index);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalSet(srcCursorLocal.Index);

                    EmitLocalGet(destCursorLocal.Index);
                    EmitLocalGet(remainingBytesLocal.Index);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalSet(destCursorLocal.Index);

                    EmitOpcode(WasmOpcode.End);

                    EmitLocalGet(newHeaderLocal.Index);
                    return new ValueType(ValueKind.Array, new ArrayInfo(elementType));
                }
            case "toList":
                {
                    EnsureArgumentCount(arguments, 0);

                    var lengthLocal = GetScratchI32(WasmScratchSlot.ArraySliceTotalLength);
                    EmitLocalGet(arrayPtrLocal.Index);
                    EmitI32Load(0);
                    EmitLocalSet(lengthLocal.Index);

                    var dataPtrLocal = GetScratchI32(WasmScratchSlot.ArraySliceDataPtr);
                    EmitLocalGet(arrayPtrLocal.Index);
                    EmitI32Load(8);
                    EmitLocalSet(dataPtrLocal.Index);

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

                    var destBaseLocal = GetScratchI32(WasmScratchSlot.ListSliceDestBase);
                    EmitLocalGet(newListLocal.Index);
                    EmitI32Const(ListHeaderSize);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalSet(destBaseLocal.Index);

                    var indexLocal = GetScratchI32(WasmScratchSlot.ListSliceIndex);
                    EmitI32Const(0);
                    EmitLocalSet(indexLocal.Index);

                    var cellAddrLocal = GetScratchI32(WasmScratchSlot.ListSliceSourceBase);
                    var elementAddrLocal = GetScratchI32(WasmScratchSlot.ArraySliceIndex);

                    EmitBlock();
                    EmitLoop();
                    EmitLocalGet(indexLocal.Index);
                    EmitLocalGet(lengthLocal.Index);
                    EmitOpcode(WasmOpcode.LessThanUInt32);
                    EmitOpcode(WasmOpcode.EqualZeroInt32);
                    EmitBrIf(1);

                    EmitLocalGet(destBaseLocal.Index);
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
                    EmitLocalSet(elementAddrLocal.Index);

                    EmitLocalGet(cellAddrLocal.Index);
                    EmitLocalGet(elementAddrLocal.Index);
                    if (elementType.Kind == ValueKind.F64)
                    {
                        EmitF64Load();
                        EmitF64Store(offset: 0);
                    }
                    else
                    {
                        EmitI32Load();
                        EmitI32Store(offset: 0);
                    }

                    EmitLocalGet(cellAddrLocal.Index);
                    EmitI32Const(GetValueTag(elementType));
                    EmitI32Store(offset: 12);

                    EmitLocalGet(indexLocal.Index);
                    EmitI32Const(1);
                    EmitOpcode(WasmOpcode.AddInt32);
                    EmitLocalSet(indexLocal.Index);

                    EmitBr(0);
                    EmitEnd();
                    EmitEnd();

                    EmitLocalGet(newListLocal.Index);
                    EmitLocalGet(lengthLocal.Index);
                    EmitI32Store(offset: 4);

                    EmitLocalGet(newListLocal.Index);
                    return new ValueType(ValueKind.List, null, null, new ListInfo(elementType));
                }
            default:
                throw new NotSupportedException($"Array member '{member}' is not supported in wasm backend.");
        }
    }
    private ValueType LoadArrayElement(ValueType elementType)
    {
        switch (elementType.Kind)
        {
            case ValueKind.F64:
                EmitF64Load();
                return elementType;
            case ValueKind.Bool:
                EmitI32Load();
                return elementType;
            case ValueKind.String:
                EmitI32Load();
                return elementType;
            case ValueKind.Array:
                EmitI32Load();
                return elementType;
            default:
                EmitI32Load();
                return elementType;
        }
    }

    private static uint GetElementSize(ValueType type)
    => type.Kind switch
    {
        ValueKind.F64 => 8u,
        _ => 4u,
    };

    private static uint GetElementAlignment(ValueType type)
        => type.Kind == ValueKind.F64 ? 8u : 4u;
}
