using System.Collections.Immutable;
using ToyLang.Syntax;

namespace ToyLang.Wasm;

public sealed record ClassMethodEntry(string Name, int Arity, FunctionDefinition Function);
public sealed record ClassConstructorEntry(int Arity, ImmutableArray<ValueType> ParameterTypes, FunctionDefinition Function);
public sealed record ClassFieldEntry(string Name, uint Offset, ValueType Type, FieldDecl Declaration, bool IsDeclaredInClass);

public sealed class ClassMetadata
{
    private readonly Dictionary<(string Name, int Arity), ClassMethodEntry> _methods = new();
    private readonly Dictionary<int, ClassConstructorEntry> _constructors = new();
    private readonly List<FieldDecl> _fieldDecls = new();
    private readonly List<ClassFieldEntry> _fields = new();
    private readonly Dictionary<string, ClassFieldEntry> _fieldMap = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Name, int Arity), int> _virtualSlots = new();

    private int _virtualSlotCount;

    public ClassMetadata(string name, string? baseName, bool isGenericDefinition, ClassDecl definition)
    {
        Name = name;
        BaseName = baseName;
        IsGenericDefinition = isGenericDefinition;
        Definition = definition;
    }

    public string Name { get; }
    public string? BaseName { get; }
    public bool IsGenericDefinition { get; }
    public ClassDecl Definition { get; }
    public bool HasFieldLayout { get; private set; }
    public uint InstanceSize { get; private set; }
    public uint InstanceAlignment { get; private set; } = 4;
    public IReadOnlyList<ClassFieldEntry> Fields => _fields;
    public IReadOnlyList<FieldDecl> FieldDecls => _fieldDecls;

    public int TypeId { get; set; }
    public uint VTableAddress { get; set; }

    public void AddOrUpdateMethod(string methodName, int arity, FunctionDefinition function)
        => _methods[(methodName, arity)] = new ClassMethodEntry(methodName, arity, function);

    public bool TryGetMethod(string methodName, int arity, out ClassMethodEntry entry)
        => _methods.TryGetValue((methodName, arity), out entry!);

    public void AddOrUpdateConstructor(ImmutableArray<ValueType> parameterTypes, FunctionDefinition function)
    {
        if (parameterTypes.Length == 0 || parameterTypes[0].Kind != ValueKind.Instance)
            throw new NotSupportedException($"Constructor for '{Name}' must receive instance as the first parameter in the wasm backend.");

        var arity = parameterTypes.Length - 1;

        if (_constructors.TryGetValue(arity, out var existing))
        {
            if (!ParameterSignatureEquals(existing.ParameterTypes, parameterTypes))
                throw new NotSupportedException($"Constructor overloading with {arity} parameter(s) is not supported for '{Name}' in the wasm backend.");

            _constructors[arity] = new ClassConstructorEntry(arity, parameterTypes, function);
        }
        else
        {
            _constructors.Add(arity, new ClassConstructorEntry(arity, parameterTypes, function));
        }
    }

    public bool TryGetConstructor(int arity, out ClassConstructorEntry entry)
        => _constructors.TryGetValue(arity, out entry!);

    public void AddField(FieldDecl field)
        => _fieldDecls.Add(field);

    public bool TryGetField(string fieldName, out ClassFieldEntry entry)
        => _fieldMap.TryGetValue(fieldName, out entry!);

    public void SetFieldLayout(IReadOnlyList<ClassFieldEntry> layout, uint instanceSize, uint alignment)
    {
        _fields.Clear();
        _fieldMap.Clear();
        foreach (var entry in layout)
        {
            _fields.Add(entry);
            _fieldMap[entry.Name] = entry;
        }

        InstanceSize = instanceSize;
        InstanceAlignment = alignment;
        HasFieldLayout = true;
    }

    public bool TryGetVirtualSlot(string methodName, int arity, out int slot)
        => _virtualSlots.TryGetValue((methodName, arity), out slot);

    public int AllocateVirtualSlot()
    {
        var slot = _virtualSlotCount;
        _virtualSlotCount += 1;
        return slot;
    }

    public void SetVirtualSlot(string methodName, int arity, int slot)
        => _virtualSlots[(methodName, arity)] = slot;

    private static bool ParameterSignatureEquals(ImmutableArray<ValueType> left, ImmutableArray<ValueType> right)
    {
        if (left.Length != right.Length)
            return false;

        for (var i = 0; i < left.Length; i++)
        {
            if (!left[i].Equals(right[i]))
                return false;
        }

        return true;
    }
}
