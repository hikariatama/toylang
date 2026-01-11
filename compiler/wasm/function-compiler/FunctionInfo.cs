using System.Collections.Immutable;
namespace ToyLang.Wasm;

internal readonly record struct FunctionInfo(string Name, ImmutableArray<ValueType> Parameters, ValueType? ReturnType, uint Index);
