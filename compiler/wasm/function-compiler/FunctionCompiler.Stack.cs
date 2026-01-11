namespace ToyLang.Wasm;

internal sealed partial class FunctionCompiler
{
    private void EmitI32Load(uint offset = 0)
    {
        EmitOpcode(WasmOpcode.LoadInt32);
        _body.WriteVarUInt32(2);
        _body.WriteVarUInt32(offset);
    }

    private void EmitI32Load8Unsigned(uint offset = 0)
    {
        EmitOpcode(WasmOpcode.LoadInt328Unsigned);
        _body.WriteVarUInt32(0);
        _body.WriteVarUInt32(offset);
    }

    private void EmitI32Store(uint offset = 0)
    {
        EmitOpcode(WasmOpcode.StoreInt32);
        _body.WriteVarUInt32(2);
        _body.WriteVarUInt32(offset);
    }

    private void EmitI32Store8(uint offset = 0)
    {
        EmitOpcode(WasmOpcode.StoreInt328);
        _body.WriteVarUInt32(0);
        _body.WriteVarUInt32(offset);
    }

    private void EmitF64Load(uint offset = 0)
    {
        EmitOpcode(WasmOpcode.LoadFloat64);
        _body.WriteVarUInt32(3);
        _body.WriteVarUInt32(offset);
    }

    private void EmitF64Store(uint offset = 0)
    {
        EmitOpcode(WasmOpcode.StoreFloat64);
        _body.WriteVarUInt32(3);
        _body.WriteVarUInt32(offset);
    }

    private void EmitI32Const(int value)
    {
        EmitOpcode(WasmOpcode.ConstInt32);
        _body.WriteVarInt32(value);
    }

    private void EmitF64Const(double value)
    {
        EmitOpcode(WasmOpcode.ConstFloat64);
        _body.WriteBytes(BitConverter.GetBytes(value));
    }

    private void EmitMemorySize()
    {
        EmitOpcode(WasmOpcode.MemorySize);
        _body.WriteByte(0x00);
    }

    private void EmitMemoryGrow()
    {
        EmitOpcode(WasmOpcode.MemoryGrow);
        _body.WriteByte(0x00);
    }
}
