namespace ToyLang.Wasm;

public enum WasmSection : byte
{
    Type = 0x01,
    Import = 0x02,
    Function = 0x03,
    Memory = 0x05,
    Export = 0x07,
    Code = 0x0A,
    Data = 0x0B,
}
