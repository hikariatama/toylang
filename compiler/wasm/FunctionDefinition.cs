using System.Collections.Immutable;
using ToyLang.Syntax;

namespace ToyLang.Wasm;

public sealed class FunctionDefinition
{
    public FunctionDefinition(MethodDecl method, uint typeIndex, ImmutableArray<ValueType> parameterTypes, ValueType? returnType, ValueType? instanceType, string? declaringType)
    {
        Method = method;
        TypeIndex = typeIndex;
        ParameterTypes = parameterTypes;
        ReturnType = returnType;
        InstanceType = instanceType;
        DeclaringType = declaringType;
    }

    public MethodDecl Method { get; }
    public uint TypeIndex { get; }
    public ImmutableArray<ValueType> ParameterTypes { get; }
    public ValueType? ReturnType { get; }
    public ValueType? InstanceType { get; }
    public string? DeclaringType { get; }
    public uint FunctionIndex { get; set; }
}
