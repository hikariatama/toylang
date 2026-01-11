using ToyLang.Syntax;

namespace ToyLang.Wasm;

internal sealed partial class FunctionCompiler
{
    private ValueType? EmitStringMemberCall(string member, IReadOnlyList<Expression> arguments)
    {
        var receiverLocal = AllocateAnonymousLocal(ValueType.String);
        EmitLocalSet(receiverLocal.Index);
        switch (member)
        {
            case "Concat":
                return EmitStringConcat(receiverLocal, arguments);
            case "Equal":
                return EmitStringEquality(receiverLocal, arguments, true);
            case "NotEqual":
                return EmitStringEquality(receiverLocal, arguments, false);
            case "Split":
                return EmitStringSplit(receiverLocal, arguments);
            case "Slice":
                return EmitStringSlice(receiverLocal, arguments);
            case "LastIndexOf":
                return EmitStringLastIndexOf(receiverLocal, arguments);
            case "StartsWith":
                return EmitStringStartsWith(receiverLocal, arguments);
            case "EndsWith":
                return EmitStringEndsWith(receiverLocal, arguments);
            case "Join":
                return EmitStringJoin(receiverLocal, arguments);
            case "Length":
                EnsureArgumentCount(arguments, 0);
                EmitLocalGet(receiverLocal.Index);
                EmitI32Load();
                return ValueType.I32;
            default:
                throw new NotSupportedException($"String member '{member}' is not supported in wasm backend.");
        }
    }

    private ValueType EmitStringConcat(LocalInfo leftLocal, IReadOnlyList<Expression> arguments)
    {
        EnsureArgumentCount(arguments, 1);
        var rightType = RequireValue(arguments[0]);
        _ = Coerce(rightType, ValueType.String);
        var rightLocal = GetScratchI32(WasmScratchSlot.StringConcatRight);
        EmitLocalSet(rightLocal.Index);

        var leftLengthLocal = GetScratchI32(WasmScratchSlot.StringConcatLeftLength);
        EmitLocalGet(leftLocal.Index);
        EmitI32Load();
        EmitLocalSet(leftLengthLocal.Index);

        var rightLengthLocal = GetScratchI32(WasmScratchSlot.StringConcatRightLength);
        EmitLocalGet(rightLocal.Index);
        EmitI32Load();
        EmitLocalSet(rightLengthLocal.Index);

        var totalLengthLocal = GetScratchI32(WasmScratchSlot.StringConcatTotalLength);
        EmitLocalGet(leftLengthLocal.Index);
        EmitLocalGet(rightLengthLocal.Index);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(totalLengthLocal.Index);

        var dataSizeLocal = GetScratchI32(WasmScratchSlot.StringConcatDataSize);
        EmitLocalGet(totalLengthLocal.Index);
        EmitI32Const(4);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(dataSizeLocal.Index);

        EmitLocalGet(dataSizeLocal.Index);
        EmitI32Const(3);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitI32Const(unchecked((int)~3));
        EmitOpcode(WasmOpcode.AndInt32);
        EmitLocalSet(dataSizeLocal.Index);

        var allocSizeLocal = GetScratchI32(WasmScratchSlot.StringConcatAllocSize);
        EmitLocalGet(dataSizeLocal.Index);
        EmitI32Const(4);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(allocSizeLocal.Index);

        EmitHeapAllocDynamic(allocSizeLocal, 4);

        var resultLocal = GetScratchI32(WasmScratchSlot.StringConcatResult);
        EmitLocalSet(resultLocal.Index);

        EmitLocalGet(resultLocal.Index);
        EmitLocalGet(totalLengthLocal.Index);
        EmitI32Store();

        var destCursorLocal = GetScratchI32(WasmScratchSlot.StringConcatDest);
        EmitLocalGet(resultLocal.Index);
        EmitI32Const(4);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(destCursorLocal.Index);

        CopyStringSegment(leftLocal, leftLengthLocal, destCursorLocal);
        CopyStringSegment(rightLocal, rightLengthLocal, destCursorLocal);

        EmitLocalGet(destCursorLocal.Index);
        EmitI32Const(0);
        EmitI32Store8();

        EmitLocalGet(resultLocal.Index);
        return ValueType.String;
    }

    private ValueType EmitStringEquality(LocalInfo leftLocal, IReadOnlyList<Expression> arguments, bool equals)
    {
        EnsureArgumentCount(arguments, 1);
        var rightType = RequireValue(arguments[0]);
        _ = Coerce(rightType, ValueType.String);
        var rightLocal = GetScratchI32(WasmScratchSlot.StringCompareRight);
        EmitLocalSet(rightLocal.Index);

        var leftLengthLocal = GetScratchI32(WasmScratchSlot.StringCompareLeftLength);
        EmitLocalGet(leftLocal.Index);
        EmitI32Load();
        EmitLocalSet(leftLengthLocal.Index);

        var rightLengthLocal = GetScratchI32(WasmScratchSlot.StringCompareRightLength);
        EmitLocalGet(rightLocal.Index);
        EmitI32Load();
        EmitLocalSet(rightLengthLocal.Index);

        var resultLocal = GetScratchI32(WasmScratchSlot.StringCompareResult);
        EmitLocalGet(leftLengthLocal.Index);
        EmitLocalGet(rightLengthLocal.Index);
        EmitOpcode(WasmOpcode.EqualInt32);
        EmitLocalSet(resultLocal.Index);

        EmitBlock();
        EmitLocalGet(resultLocal.Index);
        EmitOpcode(WasmOpcode.EqualZeroInt32);
        EmitBrIf(0);
        CompareStringContent(leftLocal, rightLocal, leftLengthLocal, resultLocal);
        EmitEnd();

        EmitLocalGet(resultLocal.Index);
        if (!equals) EmitOpcode(WasmOpcode.EqualZeroInt32);
        return ValueType.Bool;
    }

    private ValueType EmitStringSplit(LocalInfo receiverLocal, IReadOnlyList<Expression> arguments)
    {
        if (arguments.Count > 2) throw new NotSupportedException("String.Split accepts at most two arguments in wasm backend.");

        var delimiterLocal = GetScratchI32(WasmScratchSlot.StringSplitDelimiter);
        var maxSplitLocal = GetScratchI32(WasmScratchSlot.StringSplitMax);

        if (arguments.Count >= 1)
        {
            var delimiterType = RequireValue(arguments[0]);
            _ = Coerce(delimiterType, ValueType.String);
            EmitLocalSet(delimiterLocal.Index);
        }
        else
        {
            EmitStringLiteral(" ");
            EmitLocalSet(delimiterLocal.Index);
        }

        if (arguments.Count == 2)
        {
            var maxSplitType = RequireValue(arguments[1]);
            _ = Coerce(maxSplitType, ValueType.I32);
            EmitLocalSet(maxSplitLocal.Index);
        }
        else
        {
            EmitI32Const(0);
            EmitLocalSet(maxSplitLocal.Index);
        }

        var receiverLengthLocal = GetScratchI32(WasmScratchSlot.StringSplitReceiverLength);
        EmitLocalGet(receiverLocal.Index);
        EmitI32Load();
        EmitLocalSet(receiverLengthLocal.Index);

        var receiverDataLocal = GetScratchI32(WasmScratchSlot.StringSplitReceiverData);
        EmitLocalGet(receiverLocal.Index);
        EmitI32Const(4);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(receiverDataLocal.Index);

        var delimiterLengthLocal = GetScratchI32(WasmScratchSlot.StringSplitDelimiterLength);
        EmitLocalGet(delimiterLocal.Index);
        EmitI32Load();
        EmitLocalSet(delimiterLengthLocal.Index);

        var delimiterDataLocal = GetScratchI32(WasmScratchSlot.StringSplitDelimiterData);
        EmitLocalGet(delimiterLocal.Index);
        EmitI32Const(4);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(delimiterDataLocal.Index);

        EmitList(ValueType.String, 4);
        var resultListLocal = GetScratchI32(WasmScratchSlot.List);
        EmitLocalSet(resultListLocal.Index);

        EmitBlock();

        EmitLocalGet(delimiterLengthLocal.Index);
        EmitI32Const(0);
        EmitOpcode(WasmOpcode.EqualInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitLocalGet(resultListLocal.Index);
        EmitLocalGet(receiverLocal.Index);
        EmitListAppend(ValueType.String);
        EmitLocalSet(resultListLocal.Index);
        EmitBr(1);
        EmitOpcode(WasmOpcode.End);

        var segmentStartLocal = GetScratchI32(WasmScratchSlot.StringSplitSegmentStart);
        EmitI32Const(0);
        EmitLocalSet(segmentStartLocal.Index);

        var indexLocal = GetScratchI32(WasmScratchSlot.StringSplitIndex);
        EmitI32Const(0);
        EmitLocalSet(indexLocal.Index);

        var splitsDoneLocal = GetScratchI32(WasmScratchSlot.StringSplitSplitsDone);
        EmitI32Const(0);
        EmitLocalSet(splitsDoneLocal.Index);

        var matchEndLocal = GetScratchI32(WasmScratchSlot.StringSplitMatchEnd);
        var matchResultLocal = GetScratchI32(WasmScratchSlot.StringSplitMatchResult);
        var compareIndexLocal = GetScratchI32(WasmScratchSlot.StringSplitCompareIndex);

        EmitBlock();
        EmitLoop();

        EmitLocalGet(indexLocal.Index);
        EmitLocalGet(receiverLengthLocal.Index);
        EmitOpcode(WasmOpcode.LessThanUInt32);
        EmitOpcode(WasmOpcode.EqualZeroInt32);
        EmitBrIf(1);

        EmitLocalGet(indexLocal.Index);
        EmitLocalGet(delimiterLengthLocal.Index);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(matchEndLocal.Index);

        EmitLocalGet(matchEndLocal.Index);
        EmitLocalGet(receiverLengthLocal.Index);
        EmitOpcode(WasmOpcode.GreaterThanUInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitBr(1);
        EmitOpcode(WasmOpcode.End);

        EmitI32Const(1);
        EmitLocalSet(matchResultLocal.Index);

        EmitI32Const(0);
        EmitLocalSet(compareIndexLocal.Index);

        EmitBlock();
        EmitLoop();

        EmitLocalGet(compareIndexLocal.Index);
        EmitLocalGet(delimiterLengthLocal.Index);
        EmitOpcode(WasmOpcode.LessThanUInt32);
        EmitOpcode(WasmOpcode.EqualZeroInt32);
        EmitBrIf(1);

        EmitLocalGet(receiverDataLocal.Index);
        EmitLocalGet(indexLocal.Index);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalGet(compareIndexLocal.Index);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitI32Load8Unsigned();

        EmitLocalGet(delimiterDataLocal.Index);
        EmitLocalGet(compareIndexLocal.Index);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitI32Load8Unsigned();

        EmitOpcode(WasmOpcode.NotEqualInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitI32Const(0);
        EmitLocalSet(matchResultLocal.Index);
        EmitBr(2);
        EmitOpcode(WasmOpcode.End);

        EmitLocalGet(compareIndexLocal.Index);
        EmitI32Const(1);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(compareIndexLocal.Index);

        EmitBr(0);
        EmitEnd();
        EmitEnd();

        EmitLocalGet(matchResultLocal.Index);
        EmitOpcode(WasmOpcode.EqualZeroInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitLocalGet(indexLocal.Index);
        EmitI32Const(1);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(indexLocal.Index);
        EmitBr(1);
        EmitOpcode(WasmOpcode.End);

        EmitLocalGet(resultListLocal.Index);
        AppendStringSegment(receiverLocal, segmentStartLocal, indexLocal, receiverLengthLocal);
        EmitListAppend(ValueType.String);
        EmitLocalSet(resultListLocal.Index);

        EmitLocalGet(matchEndLocal.Index);
        EmitLocalSet(segmentStartLocal.Index);

        EmitLocalGet(splitsDoneLocal.Index);
        EmitI32Const(1);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(splitsDoneLocal.Index);

        EmitLocalGet(maxSplitLocal.Index);
        EmitI32Const(0);
        EmitOpcode(WasmOpcode.EqualInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitOpcode(WasmOpcode.Else);
        EmitLocalGet(splitsDoneLocal.Index);
        EmitLocalGet(maxSplitLocal.Index);
        EmitOpcode(WasmOpcode.LessThanUInt32);
        EmitOpcode(WasmOpcode.EqualZeroInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitBr(3);
        EmitOpcode(WasmOpcode.End);
        EmitOpcode(WasmOpcode.End);

        EmitLocalGet(matchEndLocal.Index);
        EmitLocalSet(indexLocal.Index);

        EmitBr(0);
        EmitEnd();
        EmitEnd();

        EmitLocalGet(resultListLocal.Index);
        AppendStringSegment(receiverLocal, segmentStartLocal, receiverLengthLocal, receiverLengthLocal);
        EmitListAppend(ValueType.String);
        EmitLocalSet(resultListLocal.Index);

        EmitEnd();

        EmitLocalGet(resultListLocal.Index);

        return new ValueType(ValueKind.List, null, null, new ListInfo(ValueType.String));
    }

    private ValueType EmitStringSlice(LocalInfo receiverLocal, IReadOnlyList<Expression> arguments)
    {
        if (arguments.Count is < 1 or > 2)
            throw new NotSupportedException("String.Slice expects one or two arguments in wasm backend.");

        var startType = RequireValue(arguments[0]);
        _ = Coerce(startType, ValueType.I32);
        var startLocal = GetScratchI32(WasmScratchSlot.StringSliceStart);
        EmitLocalSet(startLocal.Index);

        var totalLengthLocal = GetScratchI32(WasmScratchSlot.StringSliceTotalLength);
        EmitLocalGet(receiverLocal.Index);
        EmitI32Load();
        EmitLocalSet(totalLengthLocal.Index);

        var dataPtrLocal = GetScratchI32(WasmScratchSlot.StringSliceDataPtr);
        EmitLocalGet(receiverLocal.Index);
        EmitI32Const(4);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(dataPtrLocal.Index);

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

        ClampUtf8IndexToByteOffset(dataPtrLocal, totalLengthLocal, startLocal, startLocal);

        var endLocal = GetScratchI32(WasmScratchSlot.StringSliceEnd);
        var lengthLocal = GetScratchI32(WasmScratchSlot.StringSliceLength);

        if (arguments.Count == 2)
        {
            var lengthType = RequireValue(arguments[1]);
            _ = Coerce(lengthType, ValueType.I32);
            EmitLocalSet(lengthLocal.Index);

            EmitLocalGet(lengthLocal.Index);
            EmitI32Const(0);
            EmitOpcode(WasmOpcode.LessThanInt32);
            EmitOpcode(WasmOpcode.If);
            _body.WriteByte((byte)WasmControl.Void);
            EmitI32Const(0);
            EmitLocalSet(lengthLocal.Index);
            EmitOpcode(WasmOpcode.End);

            var availableBytesLocal = GetScratchI32(WasmScratchSlot.StringSliceAvailableBytes);
            EmitLocalGet(totalLengthLocal.Index);
            EmitLocalGet(startLocal.Index);
            EmitOpcode(WasmOpcode.SubtractInt32);
            EmitLocalSet(availableBytesLocal.Index);

            EmitLocalGet(dataPtrLocal.Index);
            EmitLocalGet(startLocal.Index);
            EmitOpcode(WasmOpcode.AddInt32);
            EmitLocalSet(dataPtrLocal.Index);

            ClampUtf8IndexToByteOffset(dataPtrLocal, availableBytesLocal, lengthLocal, lengthLocal);

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

        AppendStringSegment(receiverLocal, startLocal, endLocal, totalLengthLocal);
        return ValueType.String;
    }

    private ValueType EmitStringLastIndexOf(LocalInfo receiverLocal, IReadOnlyList<Expression> arguments)
    {
        EnsureArgumentCount(arguments, 1);
        var needleType = RequireValue(arguments[0]);
        _ = Coerce(needleType, ValueType.String);
        var needleLocal = GetScratchI32(WasmScratchSlot.StringSearchNeedle);
        EmitLocalSet(needleLocal.Index);

        var receiverLengthLocal = GetScratchI32(WasmScratchSlot.StringSplitReceiverLength);
        EmitLocalGet(receiverLocal.Index);
        EmitI32Load();
        EmitLocalSet(receiverLengthLocal.Index);

        var needleLengthLocal = GetScratchI32(WasmScratchSlot.StringSplitDelimiterLength);
        EmitLocalGet(needleLocal.Index);
        EmitI32Load();
        EmitLocalSet(needleLengthLocal.Index);

        var receiverDataLocal = GetScratchI32(WasmScratchSlot.StringSplitReceiverData);
        EmitLocalGet(receiverLocal.Index);
        EmitI32Const(4);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(receiverDataLocal.Index);

        var needleDataLocal = GetScratchI32(WasmScratchSlot.StringSplitDelimiterData);
        EmitLocalGet(needleLocal.Index);
        EmitI32Const(4);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(needleDataLocal.Index);

        var indexLocal = GetScratchI32(WasmScratchSlot.StringSearchIndex);
        EmitLocalGet(receiverLengthLocal.Index);
        EmitLocalGet(needleLengthLocal.Index);
        EmitOpcode(WasmOpcode.SubtractInt32);
        EmitLocalSet(indexLocal.Index);

        var matchResultLocal = GetScratchI32(WasmScratchSlot.StringSearchMatchResult);
        var compareIndexLocal = GetScratchI32(WasmScratchSlot.StringSearchCompareIndex);

        EmitBlock(ValueType.I32);

        EmitLocalGet(needleLengthLocal.Index);
        EmitI32Const(0);
        EmitOpcode(WasmOpcode.EqualInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitLocalGet(receiverLengthLocal.Index);
        EmitBr(1);
        EmitOpcode(WasmOpcode.End);

        EmitLocalGet(needleLengthLocal.Index);
        EmitLocalGet(receiverLengthLocal.Index);
        EmitOpcode(WasmOpcode.GreaterThanUInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitI32Const(-1);
        EmitBr(1);
        EmitOpcode(WasmOpcode.End);

        EmitLocalGet(indexLocal.Index);
        EmitI32Const(0);
        EmitOpcode(WasmOpcode.LessThanInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitI32Const(-1);
        EmitBr(1);
        EmitOpcode(WasmOpcode.End);

        EmitBlock();
        EmitLoop();

        EmitI32Const(1);
        EmitLocalSet(matchResultLocal.Index);

        EmitI32Const(0);
        EmitLocalSet(compareIndexLocal.Index);

        EmitBlock();
        EmitLoop();
        EmitLocalGet(compareIndexLocal.Index);
        EmitLocalGet(needleLengthLocal.Index);
        EmitOpcode(WasmOpcode.LessThanUInt32);
        EmitOpcode(WasmOpcode.EqualZeroInt32);
        EmitBrIf(1);

        EmitLocalGet(receiverDataLocal.Index);
        EmitLocalGet(indexLocal.Index);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalGet(compareIndexLocal.Index);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitI32Load8Unsigned();

        EmitLocalGet(needleDataLocal.Index);
        EmitLocalGet(compareIndexLocal.Index);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitI32Load8Unsigned();

        EmitOpcode(WasmOpcode.NotEqualInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitI32Const(0);
        EmitLocalSet(matchResultLocal.Index);
        EmitBr(2);
        EmitOpcode(WasmOpcode.End);

        EmitLocalGet(compareIndexLocal.Index);
        EmitI32Const(1);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(compareIndexLocal.Index);

        EmitBr(0);
        EmitEnd();
        EmitEnd();

        EmitLocalGet(matchResultLocal.Index);
        EmitOpcode(WasmOpcode.EqualZeroInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);

        EmitLocalGet(indexLocal.Index);
        EmitI32Const(1);
        EmitOpcode(WasmOpcode.SubtractInt32);
        EmitLocalSet(indexLocal.Index);

        EmitI32Const(-1);
        EmitLocalGet(indexLocal.Index);
        EmitI32Const(0);
        EmitOpcode(WasmOpcode.LessThanInt32);
        EmitOpcode(WasmOpcode.BranchIf);
        _body.WriteVarUInt32(2);
        EmitDrop();

        EmitBr(1);
        EmitOpcode(WasmOpcode.End);

        EmitLocalGet(indexLocal.Index);
        EmitBr(2);

        EmitEnd();
        EmitEnd();

        EmitI32Const(-1);
        EmitEnd();

        return ValueType.I32;
    }

    private ValueType EmitStringStartsWith(LocalInfo receiverLocal, IReadOnlyList<Expression> arguments)
    {
        EnsureArgumentCount(arguments, 1);
        var needleType = RequireValue(arguments[0]);
        _ = Coerce(needleType, ValueType.String);
        var needleLocal = GetScratchI32(WasmScratchSlot.StringSearchNeedle);
        EmitLocalSet(needleLocal.Index);

        var receiverLengthLocal = GetScratchI32(WasmScratchSlot.StringSplitReceiverLength);
        EmitLocalGet(receiverLocal.Index);
        EmitI32Load();
        EmitLocalSet(receiverLengthLocal.Index);

        var needleLengthLocal = GetScratchI32(WasmScratchSlot.StringSplitDelimiterLength);
        EmitLocalGet(needleLocal.Index);
        EmitI32Load();
        EmitLocalSet(needleLengthLocal.Index);

        var receiverDataLocal = GetScratchI32(WasmScratchSlot.StringSplitReceiverData);
        EmitLocalGet(receiverLocal.Index);
        EmitI32Const(4);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(receiverDataLocal.Index);

        var needleDataLocal = GetScratchI32(WasmScratchSlot.StringSplitDelimiterData);
        EmitLocalGet(needleLocal.Index);
        EmitI32Const(4);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(needleDataLocal.Index);

        var compareIndexLocal = GetScratchI32(WasmScratchSlot.StringSearchCompareIndex);

        EmitBlock(ValueType.Bool);

        EmitLocalGet(needleLengthLocal.Index);
        EmitI32Const(0);
        EmitOpcode(WasmOpcode.EqualInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitI32Const(1);
        EmitBr(1);
        EmitOpcode(WasmOpcode.End);

        EmitLocalGet(needleLengthLocal.Index);
        EmitLocalGet(receiverLengthLocal.Index);
        EmitOpcode(WasmOpcode.GreaterThanUInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitI32Const(0);
        EmitBr(1);
        EmitOpcode(WasmOpcode.End);

        EmitI32Const(0);
        EmitLocalSet(compareIndexLocal.Index);

        EmitBlock();
        EmitLoop();
        EmitLocalGet(compareIndexLocal.Index);
        EmitLocalGet(needleLengthLocal.Index);
        EmitOpcode(WasmOpcode.LessThanUInt32);
        EmitOpcode(WasmOpcode.EqualZeroInt32);
        EmitBrIf(1);

        EmitLocalGet(receiverDataLocal.Index);
        EmitLocalGet(compareIndexLocal.Index);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitI32Load8Unsigned();

        EmitLocalGet(needleDataLocal.Index);
        EmitLocalGet(compareIndexLocal.Index);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitI32Load8Unsigned();

        EmitOpcode(WasmOpcode.NotEqualInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitI32Const(0);
        EmitBr(3);
        EmitOpcode(WasmOpcode.End);

        EmitLocalGet(compareIndexLocal.Index);
        EmitI32Const(1);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(compareIndexLocal.Index);

        EmitBr(0);
        EmitEnd();
        EmitEnd();

        EmitI32Const(1);
        EmitEnd();

        return ValueType.Bool;
    }

    private ValueType EmitStringEndsWith(LocalInfo receiverLocal, IReadOnlyList<Expression> arguments)
    {
        EnsureArgumentCount(arguments, 1);
        var needleType = RequireValue(arguments[0]);
        _ = Coerce(needleType, ValueType.String);
        var needleLocal = GetScratchI32(WasmScratchSlot.StringSearchNeedle);
        EmitLocalSet(needleLocal.Index);

        var receiverLengthLocal = GetScratchI32(WasmScratchSlot.StringSplitReceiverLength);
        EmitLocalGet(receiverLocal.Index);
        EmitI32Load();
        EmitLocalSet(receiverLengthLocal.Index);

        var needleLengthLocal = GetScratchI32(WasmScratchSlot.StringSplitDelimiterLength);
        EmitLocalGet(needleLocal.Index);
        EmitI32Load();
        EmitLocalSet(needleLengthLocal.Index);

        var receiverDataLocal = GetScratchI32(WasmScratchSlot.StringSplitReceiverData);
        EmitLocalGet(receiverLocal.Index);
        EmitI32Const(4);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(receiverDataLocal.Index);

        var needleDataLocal = GetScratchI32(WasmScratchSlot.StringSplitDelimiterData);
        EmitLocalGet(needleLocal.Index);
        EmitI32Const(4);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(needleDataLocal.Index);

        var startLocal = GetScratchI32(WasmScratchSlot.StringSearchIndex);
        EmitLocalGet(receiverLengthLocal.Index);
        EmitLocalGet(needleLengthLocal.Index);
        EmitOpcode(WasmOpcode.SubtractInt32);
        EmitLocalSet(startLocal.Index);

        var compareIndexLocal = GetScratchI32(WasmScratchSlot.StringSearchCompareIndex);

        EmitBlock(ValueType.Bool);

        EmitLocalGet(needleLengthLocal.Index);
        EmitI32Const(0);
        EmitOpcode(WasmOpcode.EqualInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitI32Const(1);
        EmitBr(1);
        EmitOpcode(WasmOpcode.End);

        EmitLocalGet(needleLengthLocal.Index);
        EmitLocalGet(receiverLengthLocal.Index);
        EmitOpcode(WasmOpcode.GreaterThanUInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitI32Const(0);
        EmitBr(1);
        EmitOpcode(WasmOpcode.End);

        EmitLocalGet(startLocal.Index);
        EmitI32Const(0);
        EmitOpcode(WasmOpcode.LessThanInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitI32Const(0);
        EmitBr(1);
        EmitOpcode(WasmOpcode.End);

        EmitI32Const(0);
        EmitLocalSet(compareIndexLocal.Index);

        EmitBlock();
        EmitLoop();
        EmitLocalGet(compareIndexLocal.Index);
        EmitLocalGet(needleLengthLocal.Index);
        EmitOpcode(WasmOpcode.LessThanUInt32);
        EmitOpcode(WasmOpcode.EqualZeroInt32);
        EmitBrIf(1);

        EmitLocalGet(receiverDataLocal.Index);
        EmitLocalGet(startLocal.Index);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalGet(compareIndexLocal.Index);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitI32Load8Unsigned();

        EmitLocalGet(needleDataLocal.Index);
        EmitLocalGet(compareIndexLocal.Index);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitI32Load8Unsigned();

        EmitOpcode(WasmOpcode.NotEqualInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitI32Const(0);
        EmitBr(3);
        EmitOpcode(WasmOpcode.End);

        EmitLocalGet(compareIndexLocal.Index);
        EmitI32Const(1);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(compareIndexLocal.Index);

        EmitBr(0);
        EmitEnd();
        EmitEnd();

        EmitI32Const(1);
        EmitEnd();

        return ValueType.Bool;
    }

    private ValueType EmitStringJoin(LocalInfo receiverLocal, IReadOnlyList<Expression> arguments)
    {
        EnsureArgumentCount(arguments, 1);
        var partsType = RequireValue(arguments[0]);
        var listValueType = new ValueType(ValueKind.List, null, null, new ListInfo(ValueType.String));
        _ = Coerce(partsType, listValueType);

        var partsLocal = GetScratchI32(WasmScratchSlot.StringJoinParts);
        EmitLocalSet(partsLocal.Index);

        var countLocal = GetScratchI32(WasmScratchSlot.StringJoinCount);
        EmitLocalGet(partsLocal.Index);
        EmitI32Load(offset: 4);
        EmitLocalSet(countLocal.Index);

        var delimiterLengthLocal = GetScratchI32(WasmScratchSlot.StringSplitDelimiterLength);
        EmitLocalGet(receiverLocal.Index);
        EmitI32Load();
        EmitLocalSet(delimiterLengthLocal.Index);

        var delimiterDataLocal = GetScratchI32(WasmScratchSlot.StringSplitDelimiterData);
        EmitLocalGet(receiverLocal.Index);
        EmitI32Const(4);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(delimiterDataLocal.Index);

        EmitBlock(ValueType.I32);
        EmitLocalGet(countLocal.Index);
        EmitI32Const(0);
        EmitOpcode(WasmOpcode.EqualInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitStringLiteral(string.Empty);
        EmitBr(1);
        EmitOpcode(WasmOpcode.End);

        var totalLengthLocal = GetScratchI32(WasmScratchSlot.StringConcatTotalLength);
        EmitI32Const(0);
        EmitLocalSet(totalLengthLocal.Index);

        var baseLocal = GetScratchI32(WasmScratchSlot.StringJoinBasePtr);
        EmitLocalGet(partsLocal.Index);
        EmitI32Const(ListHeaderSize);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(baseLocal.Index);

        var indexLocal = GetScratchI32(WasmScratchSlot.StringJoinIndex);
        EmitI32Const(0);
        EmitLocalSet(indexLocal.Index);

        var elementPtrLocal = GetScratchI32(WasmScratchSlot.StringJoinElementPtr);
        var elementLengthLocal = GetScratchI32(WasmScratchSlot.StringSplitSegmentLength);
        var lastIndexLocal = GetScratchI32(WasmScratchSlot.StringJoinLastIndex);
        EmitLocalGet(countLocal.Index);
        EmitI32Const(1);
        EmitOpcode(WasmOpcode.SubtractInt32);
        EmitLocalSet(lastIndexLocal.Index);

        var cellAddrLocal = GetScratchI32(WasmScratchSlot.StringJoinCellAddr);

        EmitBlock();
        EmitLoop();
        EmitLocalGet(indexLocal.Index);
        EmitLocalGet(countLocal.Index);
        EmitOpcode(WasmOpcode.LessThanUInt32);
        EmitOpcode(WasmOpcode.EqualZeroInt32);
        EmitBrIf(1);

        EmitLocalGet(baseLocal.Index);
        EmitLocalGet(indexLocal.Index);
        EmitI32Const(ListCellSize);
        EmitOpcode(WasmOpcode.MultiplyInt32);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(cellAddrLocal.Index);

        EmitLocalGet(cellAddrLocal.Index);
        EmitI32Load();
        EmitLocalSet(elementPtrLocal.Index);

        EmitLocalGet(elementPtrLocal.Index);
        EmitI32Load();
        EmitLocalSet(elementLengthLocal.Index);

        EmitLocalGet(totalLengthLocal.Index);
        EmitLocalGet(elementLengthLocal.Index);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(totalLengthLocal.Index);

        EmitLocalGet(indexLocal.Index);
        EmitLocalGet(lastIndexLocal.Index);
        EmitOpcode(WasmOpcode.LessThanUInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitLocalGet(totalLengthLocal.Index);
        EmitLocalGet(delimiterLengthLocal.Index);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(totalLengthLocal.Index);
        EmitOpcode(WasmOpcode.End);

        EmitLocalGet(indexLocal.Index);
        EmitI32Const(1);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(indexLocal.Index);

        EmitBr(0);
        EmitEnd();
        EmitEnd();

        var dataSizeLocal = GetScratchI32(WasmScratchSlot.StringConcatDataSize);
        EmitLocalGet(totalLengthLocal.Index);
        EmitI32Const(4);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(dataSizeLocal.Index);

        EmitLocalGet(dataSizeLocal.Index);
        EmitI32Const(3);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitI32Const(unchecked((int)~3));
        EmitOpcode(WasmOpcode.AndInt32);
        EmitLocalSet(dataSizeLocal.Index);

        var allocSizeLocal = GetScratchI32(WasmScratchSlot.StringConcatAllocSize);
        EmitLocalGet(dataSizeLocal.Index);
        EmitI32Const(4);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(allocSizeLocal.Index);

        EmitHeapAllocDynamic(allocSizeLocal, 4);

        var resultLocal = GetScratchI32(WasmScratchSlot.StringConcatResult);
        EmitLocalSet(resultLocal.Index);

        EmitLocalGet(resultLocal.Index);
        EmitLocalGet(totalLengthLocal.Index);
        EmitI32Store();

        var destCursorLocal = GetScratchI32(WasmScratchSlot.StringConcatDest);
        EmitLocalGet(resultLocal.Index);
        EmitI32Const(4);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(destCursorLocal.Index);

        EmitI32Const(0);
        EmitLocalSet(indexLocal.Index);

        EmitBlock();
        EmitLoop();
        EmitLocalGet(indexLocal.Index);
        EmitLocalGet(countLocal.Index);
        EmitOpcode(WasmOpcode.LessThanUInt32);
        EmitOpcode(WasmOpcode.EqualZeroInt32);
        EmitBrIf(1);

        EmitLocalGet(baseLocal.Index);
        EmitLocalGet(indexLocal.Index);
        EmitI32Const(ListCellSize);
        EmitOpcode(WasmOpcode.MultiplyInt32);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(cellAddrLocal.Index);

        EmitLocalGet(cellAddrLocal.Index);
        EmitI32Load();
        EmitLocalSet(elementPtrLocal.Index);

        EmitLocalGet(elementPtrLocal.Index);
        EmitI32Load();
        EmitLocalSet(elementLengthLocal.Index);

        EmitLocalGet(elementLengthLocal.Index);
        EmitI32Const(0);
        EmitOpcode(WasmOpcode.EqualInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitOpcode(WasmOpcode.Else);
        CopyStringSegment(elementPtrLocal, elementLengthLocal, destCursorLocal);
        EmitOpcode(WasmOpcode.End);

        EmitLocalGet(indexLocal.Index);
        EmitLocalGet(lastIndexLocal.Index);
        EmitOpcode(WasmOpcode.LessThanUInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitLocalGet(delimiterLengthLocal.Index);
        EmitI32Const(0);
        EmitOpcode(WasmOpcode.EqualInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitOpcode(WasmOpcode.Else);
        CopyStringSegment(receiverLocal, delimiterLengthLocal, destCursorLocal);
        EmitOpcode(WasmOpcode.End);
        EmitOpcode(WasmOpcode.End);

        EmitLocalGet(indexLocal.Index);
        EmitI32Const(1);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(indexLocal.Index);

        EmitBr(0);
        EmitEnd();
        EmitEnd();

        EmitLocalGet(destCursorLocal.Index);
        EmitI32Const(0);
        EmitI32Store8();

        EmitLocalGet(resultLocal.Index);
        EmitEnd();

        return ValueType.String;
    }

    private void AppendStringSegment(LocalInfo sourcePtrLocal, LocalInfo startIndexLocal, LocalInfo endIndexLocal, LocalInfo totalLengthLocal)
    {
        var segmentLengthLocal = GetScratchI32(WasmScratchSlot.StringSplitSegmentLength);
        EmitLocalGet(endIndexLocal.Index);
        EmitLocalGet(startIndexLocal.Index);
        EmitOpcode(WasmOpcode.SubtractInt32);
        EmitLocalSet(segmentLengthLocal.Index);

        EmitLocalGet(segmentLengthLocal.Index);
        EmitI32Const(0);
        EmitOpcode(WasmOpcode.LessThanUInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitI32Const(0);
        EmitLocalSet(segmentLengthLocal.Index);
        EmitOpcode(WasmOpcode.End);

        var segmentOffsetLocal = GetScratchI32(WasmScratchSlot.StringSplitSegmentOffset);
        EmitLocalGet(startIndexLocal.Index);
        EmitLocalSet(segmentOffsetLocal.Index);

        EmitLocalGet(endIndexLocal.Index);
        EmitLocalGet(segmentOffsetLocal.Index);
        EmitOpcode(WasmOpcode.LessThanUInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitLocalGet(endIndexLocal.Index);
        EmitLocalSet(segmentOffsetLocal.Index);
        EmitOpcode(WasmOpcode.End);

        EmitLocalGet(segmentOffsetLocal.Index);
        EmitLocalGet(totalLengthLocal.Index);
        EmitOpcode(WasmOpcode.GreaterThanUInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitLocalGet(totalLengthLocal.Index);
        EmitLocalSet(segmentOffsetLocal.Index);
        EmitOpcode(WasmOpcode.End);

        var maxSliceLengthLocal = GetScratchI32(WasmScratchSlot.StringSplitSliceCapacity);
        EmitLocalGet(totalLengthLocal.Index);
        EmitLocalGet(segmentOffsetLocal.Index);
        EmitOpcode(WasmOpcode.SubtractInt32);
        EmitLocalSet(maxSliceLengthLocal.Index);

        EmitLocalGet(maxSliceLengthLocal.Index);
        EmitI32Const(0);
        EmitOpcode(WasmOpcode.LessThanUInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitI32Const(0);
        EmitLocalSet(maxSliceLengthLocal.Index);
        EmitOpcode(WasmOpcode.End);

        EmitLocalGet(segmentLengthLocal.Index);
        EmitLocalGet(maxSliceLengthLocal.Index);
        EmitOpcode(WasmOpcode.GreaterThanUInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitLocalGet(maxSliceLengthLocal.Index);
        EmitLocalSet(segmentLengthLocal.Index);
        EmitOpcode(WasmOpcode.End);

        var dataSizeLocal = GetScratchI32(WasmScratchSlot.StringSplitDataSize);
        EmitLocalGet(segmentLengthLocal.Index);
        EmitI32Const(4);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(dataSizeLocal.Index);

        EmitLocalGet(dataSizeLocal.Index);
        EmitI32Const(3);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitI32Const(unchecked((int)~3));
        EmitOpcode(WasmOpcode.AndInt32);
        EmitLocalSet(dataSizeLocal.Index);

        var allocSizeLocal = GetScratchI32(WasmScratchSlot.StringSplitAllocSize);
        EmitLocalGet(dataSizeLocal.Index);
        EmitI32Const(4);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(allocSizeLocal.Index);

        EmitHeapAllocDynamic(allocSizeLocal, 4);
        var newStringLocal = GetScratchI32(WasmScratchSlot.StringSplitNewString);
        EmitLocalSet(newStringLocal.Index);

        EmitLocalGet(newStringLocal.Index);
        EmitLocalGet(segmentLengthLocal.Index);
        EmitI32Store();

        var destCursorLocal = GetScratchI32(WasmScratchSlot.StringSplitDestCursor);
        EmitLocalGet(newStringLocal.Index);
        EmitI32Const(4);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(destCursorLocal.Index);

        CopyStringSegment(sourcePtrLocal, segmentLengthLocal, destCursorLocal, segmentOffsetLocal);

        EmitLocalGet(destCursorLocal.Index);
        EmitI32Const(0);
        EmitI32Store8();

        EmitLocalGet(newStringLocal.Index);
    }

    private void ClampUtf8IndexToByteOffset(LocalInfo dataPtrLocal, LocalInfo totalBytesLocal, LocalInfo charIndexLocal, LocalInfo resultLocal)
    {
        var remainingCharsLocal = GetScratchI32(WasmScratchSlot.StringSliceUtf8RemainingChars);
        EmitLocalGet(charIndexLocal.Index);
        EmitLocalSet(remainingCharsLocal.Index);

        EmitI32Const(0);
        EmitLocalSet(resultLocal.Index);

        EmitBlock();
        EmitLoop();
        EmitLocalGet(remainingCharsLocal.Index);
        EmitI32Const(0);
        EmitOpcode(WasmOpcode.LessEqualInt32);
        EmitBrIf(1);

        EmitLocalGet(resultLocal.Index);
        EmitLocalGet(totalBytesLocal.Index);
        EmitOpcode(WasmOpcode.GreaterEqualUInt32);
        EmitBrIf(1);

        var cursorLocal = GetScratchI32(WasmScratchSlot.StringSliceUtf8Cursor);
        EmitLocalGet(dataPtrLocal.Index);
        EmitLocalGet(resultLocal.Index);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(cursorLocal.Index);

        var leadLocal = GetScratchI32(WasmScratchSlot.StringSliceUtf8Lead);
        EmitLocalGet(cursorLocal.Index);
        EmitI32Load8Unsigned();
        EmitLocalSet(leadLocal.Index);

        var advanceLocal = GetScratchI32(WasmScratchSlot.StringSliceUtf8Advance);

        EmitBlock();
        EmitLocalGet(leadLocal.Index);
        EmitI32Const(0x80);
        EmitOpcode(WasmOpcode.AndInt32);
        EmitOpcode(WasmOpcode.EqualZeroInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitI32Const(1);
        EmitLocalSet(advanceLocal.Index);
        EmitBr(1);
        EmitOpcode(WasmOpcode.End);

        EmitLocalGet(leadLocal.Index);
        EmitI32Const(0xE0);
        EmitOpcode(WasmOpcode.AndInt32);
        EmitI32Const(0xC0);
        EmitOpcode(WasmOpcode.EqualInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitI32Const(2);
        EmitLocalSet(advanceLocal.Index);
        EmitBr(1);
        EmitOpcode(WasmOpcode.End);

        EmitLocalGet(leadLocal.Index);
        EmitI32Const(0xF0);
        EmitOpcode(WasmOpcode.AndInt32);
        EmitI32Const(0xE0);
        EmitOpcode(WasmOpcode.EqualInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitI32Const(3);
        EmitLocalSet(advanceLocal.Index);
        EmitBr(1);
        EmitOpcode(WasmOpcode.End);

        EmitLocalGet(leadLocal.Index);
        EmitI32Const(0xF8);
        EmitOpcode(WasmOpcode.AndInt32);
        EmitI32Const(0xF0);
        EmitOpcode(WasmOpcode.EqualInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitI32Const(4);
        EmitLocalSet(advanceLocal.Index);
        EmitBr(1);
        EmitOpcode(WasmOpcode.End);

        EmitI32Const(1);
        EmitLocalSet(advanceLocal.Index);
        EmitBr(0);
        EmitEnd();

        EmitLocalGet(resultLocal.Index);
        EmitLocalGet(advanceLocal.Index);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(resultLocal.Index);

        EmitLocalGet(resultLocal.Index);
        EmitLocalGet(totalBytesLocal.Index);
        EmitOpcode(WasmOpcode.GreaterThanUInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitLocalGet(totalBytesLocal.Index);
        EmitLocalSet(resultLocal.Index);
        EmitI32Const(0);
        EmitLocalSet(remainingCharsLocal.Index);
        EmitBr(1);
        EmitOpcode(WasmOpcode.End);

        EmitLocalGet(remainingCharsLocal.Index);
        EmitI32Const(1);
        EmitOpcode(WasmOpcode.SubtractInt32);
        EmitLocalSet(remainingCharsLocal.Index);

        EmitBr(0);
        EmitEnd();
        EmitEnd();
    }

    private void CopyStringSegment(LocalInfo sourcePtrLocal, LocalInfo lengthLocal, LocalInfo destCursorLocal, LocalInfo? offsetLocal = null)
    {
        var remainingLocal = GetScratchI32(WasmScratchSlot.StringCopyRemaining);
        EmitLocalGet(lengthLocal.Index);
        EmitLocalSet(remainingLocal.Index);

        EmitLocalGet(remainingLocal.Index);
        EmitI32Const(0);
        EmitOpcode(WasmOpcode.LessThanInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitI32Const(0);
        EmitLocalSet(remainingLocal.Index);
        EmitOpcode(WasmOpcode.End);

        var sourceCursorLocal = GetScratchI32(WasmScratchSlot.StringCopySource);
        EmitLocalGet(sourcePtrLocal.Index);
        EmitI32Const(4);
        EmitOpcode(WasmOpcode.AddInt32);
        if (offsetLocal is not null)
        {
            EmitLocalGet(offsetLocal.Index);
            EmitOpcode(WasmOpcode.AddInt32);
        }
        EmitLocalSet(sourceCursorLocal.Index);

        var chunkLocal = GetScratchI32(WasmScratchSlot.StringCopyChunk);

        EmitBlock();
        EmitLoop();
        EmitLocalGet(remainingLocal.Index);
        EmitI32Const(4);
        EmitOpcode(WasmOpcode.LessThanUInt32);
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

        EmitLocalGet(remainingLocal.Index);
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
        EmitLocalGet(remainingLocal.Index);
        EmitOpcode(WasmOpcode.EqualInt32);
        EmitBrIf(1);

        EmitLocalGet(destCursorLocal.Index);
        EmitLocalGet(chunkLocal.Index);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalGet(sourceCursorLocal.Index);
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

        EmitLocalGet(sourceCursorLocal.Index);
        EmitLocalGet(remainingLocal.Index);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(sourceCursorLocal.Index);

        EmitLocalGet(destCursorLocal.Index);
        EmitLocalGet(remainingLocal.Index);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(destCursorLocal.Index);

        EmitOpcode(WasmOpcode.End);
    }

    private void CompareStringContent(LocalInfo leftPtrLocal, LocalInfo rightPtrLocal, LocalInfo lengthLocal, LocalInfo resultLocal)
    {
        var remainingLocal = GetScratchI32(WasmScratchSlot.StringCompareRemaining);
        EmitLocalGet(lengthLocal.Index);
        EmitLocalSet(remainingLocal.Index);

        var leftCursorLocal = GetScratchI32(WasmScratchSlot.StringCompareLeftData);
        EmitLocalGet(leftPtrLocal.Index);
        EmitI32Const(4);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(leftCursorLocal.Index);

        var rightCursorLocal = GetScratchI32(WasmScratchSlot.StringCompareRightData);
        EmitLocalGet(rightPtrLocal.Index);
        EmitI32Const(4);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(rightCursorLocal.Index);

        var leftChunkLocal = GetScratchI32(WasmScratchSlot.StringCompareLeftChunk);
        var rightChunkLocal = GetScratchI32(WasmScratchSlot.StringCompareRightChunk);

        EmitBlock();

        EmitBlock();
        EmitLoop();
        EmitLocalGet(remainingLocal.Index);
        EmitI32Const(4);
        EmitOpcode(WasmOpcode.LessThanUInt32);
        EmitBrIf(1);

        EmitLocalGet(leftCursorLocal.Index);
        EmitI32Load();
        EmitLocalSet(leftChunkLocal.Index);

        EmitLocalGet(rightCursorLocal.Index);
        EmitI32Load();
        EmitLocalSet(rightChunkLocal.Index);

        EmitLocalGet(leftChunkLocal.Index);
        EmitLocalGet(rightChunkLocal.Index);
        EmitOpcode(WasmOpcode.NotEqualInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitI32Const(0);
        EmitLocalSet(resultLocal.Index);
        EmitBr(2);
        EmitOpcode(WasmOpcode.End);

        EmitLocalGet(leftCursorLocal.Index);
        EmitI32Const(4);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(leftCursorLocal.Index);

        EmitLocalGet(rightCursorLocal.Index);
        EmitI32Const(4);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(rightCursorLocal.Index);

        EmitLocalGet(remainingLocal.Index);
        EmitI32Const(4);
        EmitOpcode(WasmOpcode.SubtractInt32);
        EmitLocalSet(remainingLocal.Index);

        EmitBr(0);
        EmitEnd();
        EmitEnd();

        EmitLocalGet(resultLocal.Index);
        EmitOpcode(WasmOpcode.EqualZeroInt32);
        EmitBrIf(0);

        EmitLocalGet(remainingLocal.Index);
        EmitI32Const(0);
        EmitOpcode(WasmOpcode.NotEqualInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);

        EmitBlock();
        EmitLoop();
        EmitLocalGet(remainingLocal.Index);
        EmitI32Const(0);
        EmitOpcode(WasmOpcode.EqualInt32);
        EmitBrIf(1);

        EmitLocalGet(leftCursorLocal.Index);
        EmitI32Load8Unsigned();
        EmitLocalSet(leftChunkLocal.Index);

        EmitLocalGet(rightCursorLocal.Index);
        EmitI32Load8Unsigned();
        EmitLocalSet(rightChunkLocal.Index);

        EmitLocalGet(leftChunkLocal.Index);
        EmitLocalGet(rightChunkLocal.Index);
        EmitOpcode(WasmOpcode.NotEqualInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitI32Const(0);
        EmitLocalSet(resultLocal.Index);
        EmitBr(3);
        EmitOpcode(WasmOpcode.End);

        EmitLocalGet(leftCursorLocal.Index);
        EmitI32Const(1);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(leftCursorLocal.Index);

        EmitLocalGet(rightCursorLocal.Index);
        EmitI32Const(1);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(rightCursorLocal.Index);

        EmitLocalGet(remainingLocal.Index);
        EmitI32Const(1);
        EmitOpcode(WasmOpcode.SubtractInt32);
        EmitLocalSet(remainingLocal.Index);

        EmitBr(0);
        EmitEnd();
        EmitEnd();

        EmitOpcode(WasmOpcode.End);

        EmitEnd();
    }

    private ValueType EmitStringLiteral(string value)
    {
        var offset = _dataSegments.AddString(value, _memory);
        EmitI32Const((int)offset);
        return ValueType.String;
    }

    private ValueType ConvertToString(ValueType valueType)
    {
        switch (valueType.Kind)
        {
            case ValueKind.String:
                return ValueType.String;
            case ValueKind.I32:
                EmitCallByKey("io.FormatInteger");
                return ValueType.String;
            case ValueKind.Bool:
                EmitCallByKey("io.FormatBool");
                return ValueType.String;
            case ValueKind.F64:
                EmitCallByKey("io.FormatReal");
                return ValueType.String;
            default:
                throw new NotSupportedException($"Cannot convert {valueType.Kind} to String in wasm backend.");
        }
    }
}
