using System.Collections.Immutable;

namespace ToyLang.Wasm;

internal readonly record struct WasmFunctionSignature(ImmutableArray<byte> ParameterTypes, byte? ResultType)
{
    public static WasmFunctionSignature Create(IEnumerable<byte> parameterTypes, byte? resultType)
        => new(parameterTypes.ToImmutableArray(), resultType);

    public int ParameterCount => ParameterTypes.Length;
}
