namespace ToyLang.Wasm;

internal sealed partial class FunctionCompiler
{
    private const int WasmMemoryPageSizeBytes = 65_536;

    private void EmitHeapAlloc(uint size, uint alignment)
    {
        if (alignment != 0 && (alignment & (alignment - 1)) != 0)
            throw new NotSupportedException("Alignment must be a power of two.");

        var alignedPtr = GetScratchI32(0);
        var nextPtr = GetScratchI32(1);

        EmitI32Const(0);
        EmitI32Load();
        EmitLocalSet(alignedPtr.Index);

        if (alignment > 1)
        {
            EmitLocalGet(alignedPtr.Index);
            EmitI32Const((int)(alignment - 1));
            EmitOpcode(WasmOpcode.AddInt32);
            EmitI32Const(unchecked((int)~(alignment - 1)));
            EmitOpcode(WasmOpcode.AndInt32);
            EmitLocalSet(alignedPtr.Index);
        }

        EmitLocalGet(alignedPtr.Index);
        EmitI32Const((int)size);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(nextPtr.Index);

        EmitEnsureHeapCapacity(nextPtr);

        EmitI32Const(0);
        EmitLocalGet(nextPtr.Index);
        EmitI32Store();

        EmitLocalGet(alignedPtr.Index);
    }

    private void EmitHeapAllocDynamic(LocalInfo sizeLocal, uint alignment)
    {
        if (alignment != 0 && (alignment & (alignment - 1)) != 0)
            throw new NotSupportedException("Alignment must be a power of two.");

        var alignedPtr = GetScratchI32(0);
        var nextPtr = GetScratchI32(1);

        EmitI32Const(0);
        EmitI32Load();
        EmitLocalSet(alignedPtr.Index);

        if (alignment > 1)
        {
            EmitLocalGet(alignedPtr.Index);
            EmitI32Const((int)(alignment - 1));
            EmitOpcode(WasmOpcode.AddInt32);
            EmitI32Const(unchecked((int)~(alignment - 1)));
            EmitOpcode(WasmOpcode.AndInt32);
            EmitLocalSet(alignedPtr.Index);
        }

        EmitLocalGet(alignedPtr.Index);
        EmitLocalGet(sizeLocal.Index);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitLocalSet(nextPtr.Index);

        EmitEnsureHeapCapacity(nextPtr);

        EmitI32Const(0);
        EmitLocalGet(nextPtr.Index);
        EmitI32Store();

        EmitLocalGet(alignedPtr.Index);
    }

    private void EmitEnsureHeapCapacity(LocalInfo requiredEndPtr)
    {
        var requiredLocal = GetScratchI32(WasmScratchSlot.HeapRequiredBytes);
        EmitLocalGet(requiredEndPtr.Index);
        EmitLocalSet(requiredLocal.Index);

        var currentPagesLocal = GetScratchI32(WasmScratchSlot.HeapCurrentPages);
        var currentBytesLocal = GetScratchI32(WasmScratchSlot.HeapCurrentBytes);
        var deficitLocal = GetScratchI32(WasmScratchSlot.HeapDeficitBytes);
        var growPagesLocal = GetScratchI32(WasmScratchSlot.HeapGrowPages);

        EmitMemorySize();
        EmitLocalSet(currentPagesLocal.Index);

        EmitLocalGet(currentPagesLocal.Index);
        EmitI32Const(WasmMemoryPageSizeBytes);
        EmitOpcode(WasmOpcode.MultiplyInt32);
        EmitLocalSet(currentBytesLocal.Index);

        EmitLocalGet(requiredLocal.Index);
        EmitLocalGet(currentBytesLocal.Index);
        EmitOpcode(WasmOpcode.LessEqualUInt32);
        EmitOpcode(WasmOpcode.If);
        _body.WriteByte((byte)WasmControl.Void);
        EmitOpcode(WasmOpcode.Else);

        EmitLocalGet(requiredLocal.Index);
        EmitLocalGet(currentBytesLocal.Index);
        EmitOpcode(WasmOpcode.SubtractInt32);
        EmitLocalSet(deficitLocal.Index);

        EmitLocalGet(deficitLocal.Index);
        EmitI32Const(WasmMemoryPageSizeBytes - 1);
        EmitOpcode(WasmOpcode.AddInt32);
        EmitI32Const(WasmMemoryPageSizeBytes);
        EmitOpcode(WasmOpcode.DivideInt32);
        EmitLocalSet(growPagesLocal.Index);

        EmitLocalGet(growPagesLocal.Index);
        EmitMemoryGrow();
        EmitDrop();

        EmitOpcode(WasmOpcode.End);
    }
}
