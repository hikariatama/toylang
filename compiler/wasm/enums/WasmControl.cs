namespace ToyLang.Wasm;

public enum WasmControl : byte
{
    Void = 0x40,
    I32 = 0x7F,
    Function = 0x60,
}

public static class WasmConstants
{
    public static readonly byte[] WasmMagic = [0x00, 0x61, 0x73, 0x6D];
    public static readonly byte[] WasmVersion = [0x01, 0x00, 0x00, 0x00];
}
