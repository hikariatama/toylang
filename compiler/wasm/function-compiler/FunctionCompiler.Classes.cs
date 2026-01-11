using ToyLang.Syntax;

namespace ToyLang.Wasm;

internal sealed partial class FunctionCompiler
{
    private ValueType EmitClassConstructorCall(ClassMetadata metadata, IReadOnlyList<Expression> arguments)
    {
        metadata = RequireClassMetadata(metadata.Name);

        ClassConstructorEntry? constructorEntry = null;
        var storedArguments = new List<LocalInfo>(arguments.Count);

        if (metadata.TryGetConstructor(arguments.Count, out var entry))
        {
            if (entry.ParameterTypes.Length != arguments.Count + 1)
                throw new NotSupportedException($"Constructor arity mismatch for '{metadata.Name}' in the wasm backend.");

            constructorEntry = entry;

            for (var i = 0; i < arguments.Count; i++)
            {
                var expected = entry.ParameterTypes[i + 1];
                var argumentType = EmitExpression(arguments[i]);
                if (!argumentType.HasValue)
                    throw new NotSupportedException($"Constructor argument {i + 1} for '{metadata.Name}' must produce a value.");

                Coerce(argumentType.Value, expected);

                LocalInfo storage;
                if (expected.Kind == ValueKind.F64)
                    storage = GetScratchF64(WasmScratchSlot.ConstructorArgF64ScratchBase + storedArguments.Count);
                else
                    storage = GetScratchI32(WasmScratchSlot.ConstructorArgScratchBase + storedArguments.Count);

                EmitLocalSet(storage.Index);
                storedArguments.Add(storage);
            }
        }
        else if (arguments.Count != 0)
        {
            throw new NotSupportedException($"Class '{metadata.Name}' does not declare a constructor that accepts {arguments.Count} argument(s) in the wasm backend.");
        }

        var instanceSize = metadata.InstanceSize == 0 ? 4u : metadata.InstanceSize;
        var alignment = Math.Max(4u, metadata.InstanceAlignment);

        EmitHeapAlloc(instanceSize, alignment);
        var instanceLocal = AllocateAnonymousLocal(ValueType.ForInstance(metadata.Name));
        EmitLocalSet(instanceLocal.Index);

        EmitLocalGet(instanceLocal.Index);
        EmitI32Const(metadata.TypeId);
        EmitI32Store();

        foreach (var field in metadata.Fields)
        {
            var initializerType = EmitExpression(field.Declaration.Init);
            if (!initializerType.HasValue)
                throw new NotSupportedException($"Field initializer for '{metadata.Name}.{field.Name}' must produce a value.");

            Coerce(initializerType.Value, field.Type);

            if (field.Type.Kind == ValueKind.F64)
            {
                var valueLocal = GetScratchF64(WasmScratchSlot.InstanceFieldValueF64);
                EmitLocalSet(valueLocal.Index);

                EmitInstanceFieldAddress(instanceLocal, field.Offset);
                EmitLocalGet(valueLocal.Index);
                EmitF64Store();
            }
            else
            {
                var valueLocal = GetScratchI32(WasmScratchSlot.InstanceFieldValue);
                EmitLocalSet(valueLocal.Index);

                EmitInstanceFieldAddress(instanceLocal, field.Offset);
                EmitLocalGet(valueLocal.Index);
                EmitI32Store();
            }
        }

        if (constructorEntry != null)
        {
            EmitLocalGet(instanceLocal.Index);
            foreach (var local in storedArguments)
                EmitLocalGet(local.Index);

            EmitCall(constructorEntry.Function.FunctionIndex);

            if (constructorEntry.Function.ReturnType.HasValue)
                EmitDrop();
        }

        EmitLocalGet(instanceLocal.Index);
        return ValueType.ForInstance(metadata.Name);
    }

    private ClassMetadata RequireClassMetadata(string className)
    {
        if (!_classMetadata.TryGetValue(className, out var metadata))
            throw new NotSupportedException($"Class '{className}' is not declared in the wasm backend.");

        EnsureFieldLayout(metadata, _classMetadata);

        if (metadata.IsGenericDefinition)
            throw new NotSupportedException($"Generic class '{className}' must be specialized before use in the wasm backend.");

        return metadata;
    }

    private bool TryEmitInstanceFieldAccess(LocalInfo instanceLocal, string className, string fieldName, out ValueType fieldType)
    {
        var metadata = RequireClassMetadata(className);
        if (!metadata.TryGetField(fieldName, out var field))
        {
            fieldType = default;
            return false;
        }

        EmitInstanceFieldAddress(instanceLocal, field.Offset);

        if (field.Type.Kind == ValueKind.F64)
        {
            EmitF64Load();
        }
        else
        {
            EmitI32Load();
        }

        fieldType = field.Type;
        return true;
    }

    private static uint Align(uint value, uint alignment)
            => alignment <= 1 ? value : (value + alignment - 1) & ~(alignment - 1);

    private static uint GetFieldStorageSize(ValueType type)
        => type.Kind == ValueKind.F64 ? 8u : 4u;

    private static uint GetFieldAlignment(ValueType type)
        => type.Kind == ValueKind.F64 ? 8u : 4u;
    private int GetVirtualSlot(string className, string methodName, int arity)
    {
        if (!_classMetadata.TryGetValue(className, out var metadata))
            throw new NotSupportedException($"Class '{className}' is not declared in the wasm backend.");

        return GetVirtualSlot(metadata, methodName, arity);
    }

    private int GetVirtualSlot(ClassMetadata metadata, string methodName, int arity)
    {
        if (metadata.TryGetVirtualSlot(methodName, arity, out var existing))
            return existing;

        if (metadata.BaseName != null && _classMetadata.TryGetValue(metadata.BaseName, out var baseMetadata))
        {
            try
            {
                var baseSlot = GetVirtualSlot(baseMetadata, methodName, arity);
                if (metadata.TryGetMethod(methodName, arity, out _))
                    metadata.SetVirtualSlot(methodName, arity, baseSlot);
                return baseSlot;
            }
            catch (NotSupportedException)
            {
            }
        }

        if (!metadata.TryGetMethod(methodName, arity, out _))
            throw new NotSupportedException($"Method '{methodName}' with {arity} parameter(s) is not declared in class '{metadata.Name}' or its base types in the wasm backend.");

        var newSlot = metadata.AllocateVirtualSlot();
        metadata.SetVirtualSlot(methodName, arity, newSlot);
        return newSlot;
    }
    private static void EnsureFieldLayout(ClassMetadata metadata, IReadOnlyDictionary<string, ClassMetadata> classMetadata)
    {
        if (metadata.HasFieldLayout)
            return;

        var layout = new List<ClassFieldEntry>();
        uint alignment = 4;
        uint offset;

        if (metadata.BaseName != null && classMetadata.TryGetValue(metadata.BaseName, out var baseMetadata))
        {
            EnsureFieldLayout(baseMetadata, classMetadata);

            foreach (var baseField in baseMetadata.Fields)
            {
                layout.Add(new ClassFieldEntry(baseField.Name, baseField.Offset, baseField.Type, baseField.Declaration, false));
            }

            offset = baseMetadata.InstanceSize;
            alignment = Math.Max(alignment, baseMetadata.InstanceAlignment);
        }
        else
        {
            offset = 4;
        }

        foreach (var field in metadata.FieldDecls)
        {
            var fieldType = InferFieldType(field.Init, metadata, classMetadata);
            var fieldAlignment = GetFieldAlignment(fieldType);
            offset = Align(offset, fieldAlignment);
            layout.Add(new ClassFieldEntry(field.Name, offset, fieldType, field, true));
            offset += GetFieldStorageSize(fieldType);
            alignment = Math.Max(alignment, fieldAlignment);
        }

        var size = Align(offset, alignment);
        metadata.SetFieldLayout(layout, size, alignment);
    }

    private static ValueType InferFieldType(Expression expression, ClassMetadata currentClass, IReadOnlyDictionary<string, ClassMetadata> classMetadata)
    {
        switch (expression)
        {
            case LiteralExpr literal:
                return literal.Kind switch
                {
                    TokenType.Integer => ValueType.I32,
                    TokenType.Boolean => ValueType.Bool,
                    TokenType.Real => ValueType.F64,
                    TokenType.String => ValueType.String,
                    _ => ValueType.I32,
                };
            case ThisExpr:
                return ValueType.ForInstance(currentClass.Name);
            case IdentifierExpr identifier:
                if (classMetadata.TryGetValue(identifier.Name, out var classInfo) && !classInfo.IsGenericDefinition)
                    return ValueType.ForInstance(identifier.Name);

                return identifier.Name switch
                {
                    "Integer" => ValueType.I32,
                    "Boolean" => ValueType.Bool,
                    "Real" => ValueType.F64,
                    "String" => ValueType.String,
                    _ => ValueType.I32,
                };
            case GenericRefExpr generic:
                {
                    var typeRef = BuildTypeRefFromGeneric(generic);
                    return typeRef != null ? ValueType.MapValueType(typeRef) : ValueType.I32;
                }
            case CallExpr call when call.Target is IdentifierExpr targetId:
                {
                    switch (targetId.Name)
                    {
                        case "Integer":
                            return ValueType.I32;
                        case "Boolean":
                            return ValueType.Bool;
                        case "Real":
                            return ValueType.F64;
                        case "String":
                            return ValueType.String;
                    }

                    if (classMetadata.TryGetValue(targetId.Name, out var calledClass) && !calledClass.IsGenericDefinition)
                        return ValueType.ForInstance(targetId.Name);

                    return ValueType.I32;
                }
            case CallExpr call when call.Target is GenericRefExpr genericTarget:
                {
                    var typeRef = BuildTypeRefFromGeneric(genericTarget);
                    return typeRef == null ? ValueType.I32 : ValueType.MapValueType(typeRef);
                }
            case ParenExpr paren:
                return InferFieldType(paren.Inner, currentClass, classMetadata);
            default:
                return ValueType.I32;
        }
    }
    private static TypeRef? BuildTypeRefFromGeneric(GenericRefExpr generic)
    {
        if (generic.Target is not IdentifierExpr id)
            return null;

        var args = generic.TypeArguments.ToList();
        return new TypeRef(id.Name, args, generic.Line, generic.Column);
    }
    private ClassMethodEntry RequireInstanceMethod(string className, string methodName, int arity)
    {
        var currentName = className;
        while (currentName != null)
        {
            var metadata = RequireClassMetadata(currentName);
            if (metadata.TryGetMethod(methodName, arity, out var entry))
                return entry;

            currentName = metadata.BaseName;
        }

        throw new NotSupportedException($"Method '{methodName}' with {arity} parameter(s) is not declared in class '{className}' or its base types in the wasm backend.");
    }

    private static bool IsSubtypeOf(ClassMetadata candidate, string baseName, IReadOnlyDictionary<string, ClassMetadata> classMetadata)
    {
        var current = candidate;
        while (true)
        {
            if (string.Equals(current.Name, baseName, StringComparison.Ordinal))
                return true;

            if (current.BaseName == null)
                return false;

            if (!classMetadata.TryGetValue(current.BaseName, out var next))
                return false;

            current = next;
        }
    }

    private FunctionDefinition ResolveEffectiveMethod(ClassMetadata type, string methodName, int arity)
    {
        var current = type;
        while (true)
        {
            if (current.TryGetMethod(methodName, arity, out var entry))
                return entry.Function;

            if (current.BaseName == null)
                break;

            if (!_classMetadata.TryGetValue(current.BaseName, out var next))
                break;

            current = next;
        }

        throw new NotSupportedException($"Method '{methodName}' with {arity} parameter(s) is not declared in class '{type.Name}' or its base types in the wasm backend.");
    }

    private List<(int TypeId, FunctionDefinition Function)> BuildVirtualDispatchCases(string staticClassName, string methodName, int arity)
    {
        var result = new List<(int, FunctionDefinition)>();
        foreach (var metadata in _classMetadata.Values)
        {
            if (!IsSubtypeOf(metadata, staticClassName, _classMetadata))
                continue;

            var function = ResolveEffectiveMethod(metadata, methodName, arity);
            result.Add((metadata.TypeId, function));
        }

        return result;
    }
}
