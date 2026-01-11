using System.Buffers.Binary;
using System.Collections.Immutable;
using ToyLang.Syntax;

namespace ToyLang.Wasm;

public static class WasmCompiler
{
    private static readonly HostImport[] s_hostImports =
    [
        new("io.PrintInteger", "io", "PrintInteger", WasmFunctionSignature.Create([(byte)WasmType.I32], null)),
        new("io.PrintReal", "io", "PrintReal", WasmFunctionSignature.Create([(byte)WasmType.F64], null)),
        new("io.PrintBool", "io", "PrintBool", WasmFunctionSignature.Create([(byte)WasmType.I32], null)),
        new("io.PrintString", "io", "PrintString", WasmFunctionSignature.Create([(byte)WasmType.I32, (byte)WasmType.I32], null)),
        new("io.PrintLine", "io", "PrintLine", WasmFunctionSignature.Create(Array.Empty<byte>(), null)),
        new("io.PrintArray", "io", "PrintArray", WasmFunctionSignature.Create([(byte)WasmType.I32], null)),
        new("io.PrintList", "io", "PrintList", WasmFunctionSignature.Create([(byte)WasmType.I32], null)),
        new("io.PrintMap", "io", "PrintMap", WasmFunctionSignature.Create([(byte)WasmType.I32], null)),
        new("io.Read", "io", "Read", WasmFunctionSignature.Create(Array.Empty<byte>(), (byte)WasmType.I32)),
        new("io.ReadLine", "io", "ReadLine", WasmFunctionSignature.Create(Array.Empty<byte>(), (byte)WasmType.I32)),
        new("io.ReadInteger", "io", "ReadInteger", WasmFunctionSignature.Create(Array.Empty<byte>(), (byte)WasmType.I32)),
        new("io.ReadReal", "io", "ReadReal", WasmFunctionSignature.Create(Array.Empty<byte>(), (byte)WasmType.F64)),
        new("io.ReadBool", "io", "ReadBool", WasmFunctionSignature.Create(Array.Empty<byte>(), (byte)WasmType.I32)),
        new("io.FormatInteger", "io", "FormatInteger", WasmFunctionSignature.Create([(byte)WasmType.I32], (byte)WasmType.I32)),
        new("io.FormatReal", "io", "FormatReal", WasmFunctionSignature.Create([(byte)WasmType.F64], (byte)WasmType.I32)),
        new("io.FormatBool", "io", "FormatBool", WasmFunctionSignature.Create([(byte)WasmType.I32], (byte)WasmType.I32)),
        new("math.Cos", "math", "Cos", WasmFunctionSignature.Create([(byte)WasmType.F64], (byte)WasmType.F64)),
        new("math.Sin", "math", "Sin", WasmFunctionSignature.Create([(byte)WasmType.F64], (byte)WasmType.F64)),
        new("math.Tan", "math", "Tan", WasmFunctionSignature.Create([(byte)WasmType.F64], (byte)WasmType.F64)),
        new("math.Acos", "math", "Acos", WasmFunctionSignature.Create([(byte)WasmType.F64], (byte)WasmType.F64)),
        new("math.Asin", "math", "Asin", WasmFunctionSignature.Create([(byte)WasmType.F64], (byte)WasmType.F64)),
        new("math.Atan", "math", "Atan", WasmFunctionSignature.Create([(byte)WasmType.F64], (byte)WasmType.F64)),
        new("math.Atan2", "math", "Atan2", WasmFunctionSignature.Create([(byte)WasmType.F64, (byte)WasmType.F64], (byte)WasmType.F64)),
        new("math.Random", "math", "Random", WasmFunctionSignature.Create(Array.Empty<byte>(), (byte)WasmType.F64)),
        new("math.Exp", "math", "Exp", WasmFunctionSignature.Create([(byte)WasmType.F64], (byte)WasmType.F64)),
        new("math.Log", "math", "Log", WasmFunctionSignature.Create([(byte)WasmType.F64], (byte)WasmType.F64)),
        new("math.Sqrt", "math", "Sqrt", WasmFunctionSignature.Create([(byte)WasmType.F64], (byte)WasmType.F64)),
        new("math.Pow", "math", "Pow", WasmFunctionSignature.Create([(byte)WasmType.F64, (byte)WasmType.F64], (byte)WasmType.F64)),
        new("screen.Width", "screen", "Width", WasmFunctionSignature.Create(Array.Empty<byte>(), (byte)WasmType.F64)),
        new("screen.Height", "screen", "Height", WasmFunctionSignature.Create(Array.Empty<byte>(), (byte)WasmType.F64)),
        new("time.Sleep", "time", "Sleep", WasmFunctionSignature.Create([(byte)WasmType.F64], null)),
        new("time.PerfCounter", "time", "PerfCounter", WasmFunctionSignature.Create(Array.Empty<byte>(), (byte)WasmType.F64)),
        new("time.Unix", "time", "Unix", WasmFunctionSignature.Create(Array.Empty<byte>(), (byte)WasmType.F64)),
    ];

    private static readonly string[] s_builtinTypeNames =
    [
        "Integer",
        "Real",
        "Boolean",
        "String",
        "Array",
        "IO",
        "AnyValue",
        "AnyRef",
        "Class",
        "List",
        "Pair",
        "Map",
    ];

    public static byte[] Compile(ProgramAst optimizedAst)
    {
        if (optimizedAst is null)
            throw new ArgumentNullException(nameof(optimizedAst));

        optimizedAst = GenericMonomorphizer.Monomorphize(optimizedAst);

        var functionMethods = optimizedAst.Items
            .OfType<MethodDecl>()
            .Where(m => !m.IsConstructor && m.Body != null)
            .ToList();

        var mainMethod = functionMethods.FirstOrDefault(m => string.Equals(m.Name, "Main", StringComparison.Ordinal));

        if (mainMethod == null)
            throw new InvalidOperationException("Method 'Main' was not found in the program.");

        var signatures = new SignatureTable();
        var classMetadata = BuildClassMetadata(optimizedAst);

        var knownTypes = optimizedAst.Items
            .OfType<ClassDecl>()
            .Where(c => c.TypeParameters.Count == 0)
            .Select(c => c.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var typeName in s_builtinTypeNames)
            knownTypes.Add(typeName);

        var wasmImports = new List<WasmImport>(s_hostImports.Length);
        var hostFunctionIndices = new Dictionary<string, uint>(StringComparer.Ordinal);
        for (var i = 0; i < s_hostImports.Length; i++)
        {
            var def = s_hostImports[i];
            var typeIndex = signatures.GetOrAdd(def.Signature);
            wasmImports.Add(new WasmImport(def.Module, def.Name, WasmImportKind.Function, typeIndex));
            hostFunctionIndices[def.Key] = (uint)i;
        }

        var functionDefinitions = new List<FunctionDefinition>(functionMethods.Count);
        foreach (var method in functionMethods)
        {
            var parameterTypes = BuildParameterTypes(method, instanceType: null);
            var returnType = MapReturnTypeDescriptor(method.ReturnType);
            var signature = BuildSignature(parameterTypes, returnType);
            var typeIndex = signatures.GetOrAdd(signature);
            functionDefinitions.Add(new FunctionDefinition(method, typeIndex, parameterTypes, returnType, instanceType: null, declaringType: null));
        }

        foreach (var classDecl in optimizedAst.Items.OfType<ClassDecl>())
        {
            if (classDecl.TypeParameters.Count > 0)
                continue;

            if (!classMetadata.TryGetValue(classDecl.Name, out var metadata))
                continue;

            foreach (var method in classDecl.Members.OfType<MethodDecl>())
            {
                if (method.Body == null)
                    continue;

                var instanceType = ValueType.ForInstance(classDecl.Name);
                var parameterTypes = BuildParameterTypes(method, instanceType);
                var returnType = MapReturnTypeDescriptor(method.ReturnType);
                var signature = BuildSignature(parameterTypes, returnType);
                var typeIndex = signatures.GetOrAdd(signature);
                var function = new FunctionDefinition(method, typeIndex, parameterTypes, returnType, instanceType, classDecl.Name);
                functionDefinitions.Add(function);

                if (method.IsConstructor)
                    metadata.AddOrUpdateConstructor(parameterTypes, function);
                else
                    metadata.AddOrUpdateMethod(method.Name, method.Parameters.Count, function);
            }
        }

        AssignClassTypeIds(classMetadata);

        var sectionBuilder = new WasmSectionBuilder();
        sectionBuilder.EmitTypeSection(signatures.Signatures);
        sectionBuilder.EmitImportSection(wasmImports);

        for (var i = 0; i < functionDefinitions.Count; i++)
        {
            functionDefinitions[i].FunctionIndex = (uint)wasmImports.Count + (uint)i;
        }

        sectionBuilder.EmitFunctionSection(functionDefinitions.Select(f => f.TypeIndex).ToArray());

        var linearMemory = new LinearMemory(LinearMemory.DefaultStart);
        var dataSegmentBuilder = new DataSegmentBuilder();

        sectionBuilder.EmitMemorySection(LinearMemory.DefaultPageCount, null);

        var mainFunction = functionDefinitions.First(f => f.InstanceType is null && string.Equals(f.Method.Name, "Main", StringComparison.Ordinal));
        sectionBuilder.EmitExportSection(
        [
            new WasmExport("Main", 0x00, mainFunction.FunctionIndex),
            new WasmExport("memory", 0x02, 0),
        ]);

        var functionInfo = BuildFunctionInfo(functionDefinitions);

        var bodies = new List<WasmFunctionBody>(functionDefinitions.Count);
        foreach (var function in functionDefinitions)
        {
            var body = EmitFunctionBody(function, hostFunctionIndices, linearMemory, dataSegmentBuilder, functionInfo, classMetadata, knownTypes);
            bodies.Add(body);
        }

        sectionBuilder.EmitCodeSection(bodies);

        var dataSegments = new List<DataSegment>(dataSegmentBuilder.Build());
        var heapBase = linearMemory.CurrentOffset;
        var heapHeader = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(heapHeader, heapBase);
        dataSegments.Insert(0, new DataSegment(0, heapHeader));

        if (dataSegments.Count > 0)
        {
            sectionBuilder.EmitDataSection(dataSegments);
        }

        return sectionBuilder.BuildModule();
    }

    private static void AssignClassTypeIds(Dictionary<string, ClassMetadata> metadata)
    {
        var nextTypeId = 1;

        foreach (var entry in metadata.Values.OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            entry.TypeId = nextTypeId;
            nextTypeId += 1;
        }
    }

    private static WasmFunctionBody EmitFunctionBody(
        FunctionDefinition function,
        IReadOnlyDictionary<string, uint> hostFunctions,
        LinearMemory memory,
        DataSegmentBuilder dataSegmentBuilder,
        IReadOnlyDictionary<string, FunctionInfo> functions,
        IReadOnlyDictionary<string, ClassMetadata> classMetadata,
        IEnumerable<string> knownTypes)
    {
        var compiler = new FunctionCompiler(function.ReturnType, hostFunctions, functions, classMetadata, function.Method.Parameters, memory, dataSegmentBuilder, knownTypes, function.InstanceType);
        compiler.Compile(function.Method);
        return compiler.Build();
    }

    private static IReadOnlyDictionary<string, FunctionInfo> BuildFunctionInfo(IEnumerable<FunctionDefinition> functions)
    {
        var map = new Dictionary<string, FunctionInfo>(StringComparer.Ordinal);
        foreach (var function in functions)
        {
            if (function.InstanceType.HasValue)
                continue;

            if (!map.TryAdd(function.Method.Name, new FunctionInfo(function.Method.Name, function.ParameterTypes, function.ReturnType, function.FunctionIndex)))
                throw new NotSupportedException($"Function overloading for '{function.Method.Name}' is not supported in the wasm backend.");
        }

        return map;
    }

    private static ImmutableArray<ValueType> BuildParameterTypes(MethodDecl method, ValueType? instanceType)
    {
        var builder = ImmutableArray.CreateBuilder<ValueType>(method.Parameters.Count + (instanceType.HasValue ? 1 : 0));
        if (instanceType.HasValue)
        {
            builder.Add(instanceType.Value);
        }

        foreach (var parameter in method.Parameters)
        {
            builder.Add(ValueType.MapValueType(parameter.Type));
        }

        return builder.ToImmutable();
    }

    private static WasmFunctionSignature BuildSignature(ImmutableArray<ValueType> parameterTypes, ValueType? returnDescriptor)
    {
        var parameterKinds = ImmutableArray.CreateBuilder<byte>(parameterTypes.Length);
        foreach (var parameter in parameterTypes)
        {
            parameterKinds.Add(parameter.Kind == ValueKind.F64 ? (byte)WasmType.F64 : (byte)WasmType.I32);
        }

        byte? resultType = returnDescriptor?.Kind switch
        {
            null => null,
            ValueKind.F64 => (byte)WasmType.F64,
            _ => (byte)WasmType.I32,
        };

        return new WasmFunctionSignature(parameterKinds.ToImmutable(), resultType);
    }

    private static ValueType? MapReturnTypeDescriptor(TypeRef? returnType)
        => returnType == null ? null : ValueType.MapValueType(returnType);

    private static Dictionary<string, ClassMetadata> BuildClassMetadata(ProgramAst ast)
    {
        var metadata = new Dictionary<string, ClassMetadata>(StringComparer.Ordinal);
        foreach (var classDecl in ast.Items.OfType<ClassDecl>())
        {
            var classMetadata = new ClassMetadata(classDecl.Name, classDecl.BaseType?.Name, classDecl.TypeParameters.Count > 0, classDecl);
            foreach (var field in classDecl.Members.OfType<FieldDecl>())
            {
                classMetadata.AddField(field);
            }

            metadata[classDecl.Name] = classMetadata;
        }

        return metadata;
    }

    private sealed class SignatureTable
    {
        private readonly List<WasmFunctionSignature> _signatures = new();
        private readonly Dictionary<WasmFunctionSignature, uint> _indices = new();

        public uint GetOrAdd(WasmFunctionSignature signature)
        {
            if (_indices.TryGetValue(signature, out var existing))
                return existing;

            var index = (uint)_signatures.Count;
            _signatures.Add(signature);
            _indices[signature] = index;
            return index;
        }

        public IReadOnlyList<WasmFunctionSignature> Signatures => _signatures;
    }

    private readonly record struct HostImport(string Key, string Module, string Name, WasmFunctionSignature Signature);
}
