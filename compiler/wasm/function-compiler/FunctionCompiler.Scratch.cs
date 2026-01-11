namespace ToyLang.Wasm;

internal sealed partial class FunctionCompiler
{
    private LocalInfo GetScratchI32(WasmScratchSlot slot) => GetScratchI32((int)slot);

    private LocalInfo GetScratchI32(int slot)
    {
        while (_scratchI32.Count <= slot)
        {
            var index = _parameterCount + (uint)_localOrder.Count;
            var info = new LocalInfo($"$scratch_i32_{_scratchI32.Count}", index, ValueType.I32);
            _localOrder.Add(info);
            _scratchI32.Add(info);
        }

        return _scratchI32[slot];
    }

    private LocalInfo GetScratchF64(WasmScratchSlot slot) => GetScratchF64((int)slot);

    private LocalInfo GetScratchF64(int slot)
    {
        while (_scratchF64.Count <= slot)
        {
            var index = _parameterCount + (uint)_localOrder.Count;
            var info = new LocalInfo($"$scratch_f64_{_scratchF64.Count}", index, ValueType.F64);
            _localOrder.Add(info);
            _scratchF64.Add(info);
        }

        return _scratchF64[slot];
    }
}
