using ToyLang.Syntax;

namespace ToyLang.Wasm;

internal sealed partial class FunctionCompiler
{
    private readonly WasmWriter _body = new();
    private readonly Dictionary<string, LocalInfo> _locals = new(StringComparer.Ordinal);
    private readonly List<LocalInfo> _localOrder = new();
    private readonly Dictionary<string, uint> _hostFunctions;
    private readonly IReadOnlyDictionary<string, FunctionInfo> _functions;
    private readonly IReadOnlyDictionary<string, ClassMetadata> _classMetadata;
    private readonly ValueType? _expectedReturn;
    private readonly uint _parameterCount;
    private readonly LinearMemory _memory;
    private readonly DataSegmentBuilder _dataSegments;
    private readonly Dictionary<ValueType, LocalInfo> _tempLocals = new();
    private readonly List<LocalInfo> _scratchI32 = new();
    private readonly List<LocalInfo> _scratchF64 = new();
    private readonly Stack<LoopContext> _loopStack = new();
    private readonly HashSet<string> _knownTypes;
    private readonly bool _hasInstanceContext;
    private readonly LocalInfo? _thisLocal;
    private readonly Dictionary<string, MapValueAlias> _listAliases = new(StringComparer.Ordinal);
    private MapValueAlias? _nextListAlias;
    private bool _captureNextListAlias;
    private bool _hasReturn;
    private int _anonymousLocalCounter;

    public FunctionCompiler(
        ValueType? expectedReturn,
        IReadOnlyDictionary<string, uint> hostFunctions,
        IReadOnlyDictionary<string, FunctionInfo> functions,
        IReadOnlyDictionary<string, ClassMetadata> classMetadata,
        IReadOnlyList<Parameter> parameters,
        LinearMemory memory,
        DataSegmentBuilder dataSegmentBuilder,
        IEnumerable<string> knownTypes,
        ValueType? instanceType)
    {
        _expectedReturn = expectedReturn;
        _hostFunctions = new Dictionary<string, uint>(hostFunctions);
        _functions = functions;
        _classMetadata = classMetadata;
        _memory = memory;
        _dataSegments = dataSegmentBuilder;
        _knownTypes = new HashSet<string>(knownTypes, StringComparer.Ordinal);

        var hasInstance = instanceType.HasValue && instanceType.Value.Kind == ValueKind.Instance;
        _hasInstanceContext = hasInstance;

        uint index = 0;
        if (hasInstance)
        {
            var thisInfo = new LocalInfo("this", index, instanceType!.Value);
            _locals["this"] = thisInfo;
            _thisLocal = thisInfo;
            index += 1;
        }

        foreach (var parameter in parameters)
        {
            var type = ValueType.MapValueType(parameter.Type);
            _locals[parameter.Name] = new LocalInfo(parameter.Name, index, type);
            index += 1;
        }

        _parameterCount = index;
    }

    public WasmFunctionBody Build()
    {
        if (!_hasReturn && _expectedReturn.HasValue)
        {
            EmitDefaultValue(_expectedReturn.Value);
            EmitReturn();
            _hasReturn = true;
        }

        var locals = new List<WasmLocal>(_localOrder.Count);
        foreach (var local in _localOrder)
        {
            locals.Add(new WasmLocal(ToWasmType(GetStorageKind(local.Type)), 1));
        }

        return new WasmFunctionBody(locals, _body.ToArray());
    }

    private static ValueKind GetStorageKind(ValueType type)
        => type.Kind switch
        {
            ValueKind.F64 => ValueKind.F64,
            _ => ValueKind.I32,
        };

    private void EmitBlock(ValueType? resultType = null)
    {
        EmitOpcode(WasmOpcode.Block);
        WriteBlockType(resultType);
    }

    private void WriteBlockType(ValueType? resultType)
    {
        if (!resultType.HasValue)
        {
            _body.WriteByte((byte)WasmControl.Void);
            return;
        }

        var storageKind = GetStorageKind(resultType.Value);
        _body.WriteByte(ToWasmType(storageKind));
    }

    private void EmitEnd() => EmitOpcode(WasmOpcode.End);

    private void EmitInstanceFieldAddress(LocalInfo instanceLocal, uint offset)
    {
        EmitLocalGet(instanceLocal.Index);
        if (offset != 0)
        {
            EmitI32Const((int)offset);
            EmitOpcode(WasmOpcode.AddInt32);
        }
    }

    private bool TryResolveInstanceFieldAssignment(MemberAccessExpr memberAccess, out LocalInfo instanceLocal, out ClassFieldEntry fieldEntry)
    {
        instanceLocal = default!;
        fieldEntry = null!;

        LocalInfo? resolvedInstance = null;

        switch (memberAccess.Target)
        {
            case ThisExpr when _hasInstanceContext && _thisLocal is not null:
                resolvedInstance = _thisLocal;
                break;
            case IdentifierExpr id when _locals.TryGetValue(id.Name, out var local) && local.Type.Kind == ValueKind.Instance:
                resolvedInstance = local;
                break;
        }

        if (resolvedInstance is null)
            return false;

        var instanceInfo = resolvedInstance.Type.Instance;
        if (instanceInfo is null)
            return false;

        if (!_classMetadata.TryGetValue(instanceInfo.ClassName, out var metadata))
            metadata = RequireClassMetadata(instanceInfo.ClassName);

        if (!metadata.TryGetField(memberAccess.Member, out var field))
            return false;

        instanceLocal = resolvedInstance;
        fieldEntry = field;
        return true;
    }

    private static int GetValueTag(ValueType type)
    {
        return type.Kind switch
        {
            ValueKind.I32 => 0,
            ValueKind.Bool => 1,
            ValueKind.F64 => 2,
            ValueKind.String => 3,
            ValueKind.Array => 4,
            ValueKind.Instance => 5,
            ValueKind.List => 6,
            ValueKind.Map => 7,
            _ => 0
        };
    }

    private LocalInfo AllocateAnonymousLocal(ValueType type)
    {
        var index = _parameterCount + (uint)_localOrder.Count;
        var info = new LocalInfo($"$anon_{_anonymousLocalCounter++}", index, type);
        _localOrder.Add(info);
        return info;
    }

    private bool TryEmitNumericComparison(ValueType leftType, Expression rightExpr, WasmOpcode intOpcode, WasmOpcode realOpcode)
    {
        if (leftType.Kind == ValueKind.I32)
        {
            var right = RequireValue(rightExpr);
            Coerce(right, ValueType.I32);
            EmitOpcode(intOpcode);
            return true;
        }
        if (leftType.Kind == ValueKind.F64)
        {
            var right = RequireValue(rightExpr);
            Coerce(right, ValueType.F64);
            EmitOpcode(realOpcode);
            return true;
        }
        return false;
    }

    private static void EnsureArgumentCount(IReadOnlyList<Expression> arguments, int expected)
    {
        if (arguments.Count != expected)
            throw new NotSupportedException($"Expected {expected} argument(s) but received {arguments.Count}.");
    }

    private ValueType RequireValue(Expression expression)
    {
        var value = EmitExpression(expression);
        if (!value.HasValue)
            throw new NotSupportedException("Expression must produce a value.");
        return value.Value;
    }

    private void EmitLocalGet(uint index)
    {
        EmitOpcode(WasmOpcode.GetLocal);
        _body.WriteVarUInt32(index);
    }

    private void EmitLocalSet(uint index)
    {
        EmitOpcode(WasmOpcode.SetLocal);
        _body.WriteVarUInt32(index);
    }

    private void EmitOpcode(WasmOpcode opcode) => _body.WriteByte((byte)opcode);

    private void EmitDrop() => EmitOpcode(WasmOpcode.DropValue);

    private void EmitCallByKey(string key)
    {
        if (!_hostFunctions.TryGetValue(key, out var index))
            throw new NotSupportedException($"Host import '{key}' is not registered.");
        EmitCall(index);
    }

    private void EmitCall(uint index)
    {
        EmitOpcode(WasmOpcode.CallFunction);
        _body.WriteVarUInt32(index);
    }
    private void EmitCallIndirect(uint typeIndex)
    {
        EmitOpcode(WasmOpcode.CallIndirect);
        _body.WriteVarUInt32(typeIndex);
        _body.WriteVarUInt32(0);
    }

    private void EmitDefaultValue(ValueType type)
    {
        switch (type.Kind)
        {
            case ValueKind.I32:
            case ValueKind.Bool:
            case ValueKind.String:
            case ValueKind.Array:
            case ValueKind.Instance:
            case ValueKind.List:
            case ValueKind.Map:
                EmitI32Const(0);
                break;
            case ValueKind.F64:
                EmitF64Const(0.0);
                break;
            default:
                throw new NotSupportedException("Unsupported default value kind.");
        }
    }

    private ValueType Coerce(ValueType valueType, ValueType targetType)
    {
        var valueIsPointer = valueType.Kind is ValueKind.Instance or ValueKind.Array or ValueKind.List or ValueKind.Map;
        var targetIsPointer = targetType.Kind is ValueKind.Instance or ValueKind.Array or ValueKind.List or ValueKind.Map;

        if (valueIsPointer && targetIsPointer)
        {
            if (valueType.Kind == ValueKind.Array && targetType.Kind == ValueKind.Array)
            {
                if (targetType.Array != null && valueType.Array != null)
                {
                    if (valueType.Array.ElementType.Kind != targetType.Array.ElementType.Kind)
                        throw new NotSupportedException($"Cannot convert Array[{valueType.Array.ElementType.Kind}] to Array[{targetType.Array.ElementType.Kind}] in wasm backend.");
                }
            }
            else if (valueType.Kind == ValueKind.List && targetType.Kind == ValueKind.List)
            {
                if (targetType.List != null && valueType.List != null)
                {
                    if (valueType.List.ElementType.Kind != targetType.List.ElementType.Kind)
                        throw new NotSupportedException($"Cannot convert List[{valueType.List.ElementType.Kind}] to List[{targetType.List.ElementType.Kind}] in wasm backend.");
                }
            }
            else if (valueType.Kind == ValueKind.Map && targetType.Kind == ValueKind.Map)
            {
                if (targetType.Map != null && valueType.Map != null)
                {
                    if (valueType.Map.KeyType.Kind != targetType.Map.KeyType.Kind || valueType.Map.ValueType.Kind != targetType.Map.ValueType.Kind)
                        throw new NotSupportedException($"Cannot convert Map[{valueType.Map.KeyType.Kind}, {valueType.Map.ValueType.Kind}] to Map[{targetType.Map.KeyType.Kind}, {targetType.Map.ValueType.Kind}] in wasm backend.");
                }
            }
            return targetType;
        }

        if (valueType.Kind != targetType.Kind)
        {
            var valueKind = valueType.Kind;
            var targetKind = targetType.Kind;

            if (targetKind == ValueKind.String)
                return ConvertToString(valueType);

            if (targetKind == ValueKind.I32 && valueKind == ValueKind.Bool)
                return ValueType.I32;

            if (targetKind == ValueKind.I32 && valueKind == ValueKind.F64)
            {
                EmitOpcode(WasmOpcode.ConvertFloat64ToInt32);
                return ValueType.I32;
            }

            if (targetKind == ValueKind.Bool && valueKind == ValueKind.I32)
            {
                EmitI32Const(0);
                EmitOpcode(WasmOpcode.NotEqualInt32);
                return ValueType.Bool;
            }

            if (targetKind == ValueKind.F64 && valueKind == ValueKind.I32)
            {
                EmitOpcode(WasmOpcode.ConvertInt32ToFloat64);
                return ValueType.F64;
            }

            throw new NotSupportedException($"Cannot convert {valueKind} to {targetKind} in wasm backend.");
        }

        return targetType;
    }

    private static byte ToWasmType(ValueKind kind) => kind == ValueKind.F64 ? (byte)WasmType.F64 : (byte)WasmType.I32;

    private void BeginListAliasCapture()
    {
        _captureNextListAlias = true;
        _nextListAlias = null;
    }

    private void CancelListAliasCapture()
    {
        _captureNextListAlias = false;
        _nextListAlias = null;
    }

    private void ConsumePendingListAlias(string localName, LocalInfo local)
    {
        if (local.Type.Kind == ValueKind.List && _nextListAlias is MapValueAlias alias)
        {
            _listAliases[localName] = alias;
        }
        else
        {
            _listAliases.Remove(localName);
        }

        _nextListAlias = null;
        _captureNextListAlias = false;
    }

    private sealed record LocalInfo(string Name, uint Index, ValueType Type);

    private readonly record struct LoopContext(uint BreakDepth);

    private sealed record MapValueAlias(LocalInfo MapLocal, LocalInfo KeyLocal, ValueType KeyType, MapInfo MapInfo);
}
