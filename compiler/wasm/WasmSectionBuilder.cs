namespace ToyLang.Wasm;

internal sealed class WasmSectionBuilder
{
    private readonly WasmWriter _writer = new();

    public void EmitTypeSection(IReadOnlyList<WasmFunctionSignature> signatures)
    {
        var section = new WasmWriter();
        section.WriteVarUInt32((uint)signatures.Count);
        foreach (var sig in signatures)
        {
            section.WriteByte((byte)WasmControl.Function);
            section.WriteVarUInt32((uint)sig.ParameterCount);
            for (int i = 0; i < sig.ParameterCount; i++)
            {
                section.WriteByte(sig.ParameterTypes[i]);
            }
            if (sig.ResultType.HasValue)
            {
                section.WriteVarUInt32(1);
                section.WriteByte(sig.ResultType.Value);
            }
            else
            {
                section.WriteVarUInt32(0);
            }
        }
        _writer.WriteSection((byte)WasmSection.Type, section);
    }

    public void EmitImportSection(IReadOnlyList<WasmImport> imports)
    {
        if (imports.Count == 0) return;
        var section = new WasmWriter();
        section.WriteVarUInt32((uint)imports.Count);
        foreach (var import in imports)
        {
            section.WriteString(import.Module);
            section.WriteString(import.Name);
            section.WriteByte((byte)import.Kind);
            section.WriteVarUInt32(import.TypeIndex);
        }
        _writer.WriteSection((byte)WasmSection.Import, section);
    }

    public void EmitFunctionSection(IReadOnlyList<uint> typeIndices)
    {
        if (typeIndices.Count == 0) return;
        var section = new WasmWriter();
        section.WriteVarUInt32((uint)typeIndices.Count);
        foreach (var typeIndex in typeIndices)
        {
            section.WriteVarUInt32(typeIndex);
        }
        _writer.WriteSection((byte)WasmSection.Function, section);
    }

    public void EmitMemorySection(uint minimumPages, uint? maximumPages)
    {
        var section = new WasmWriter();
        section.WriteVarUInt32(1);
        if (maximumPages.HasValue)
        {
            section.WriteByte(0x01); // flag has max
            section.WriteVarUInt32(minimumPages);
            section.WriteVarUInt32(maximumPages.Value);
        }
        else
        {
            section.WriteByte(0x00); // flag no max
            section.WriteVarUInt32(minimumPages);
        }
        _writer.WriteSection((byte)WasmSection.Memory, section);
    }

    public void EmitExportSection(IReadOnlyList<WasmExport> exports)
    {
        if (exports.Count == 0) return;
        var section = new WasmWriter();
        section.WriteVarUInt32((uint)exports.Count);
        foreach (var export in exports)
        {
            section.WriteString(export.Name);
            section.WriteByte(export.Kind);
            section.WriteVarUInt32(export.Index);
        }
        _writer.WriteSection((byte)WasmSection.Export, section);
    }

    public void EmitCodeSection(IReadOnlyList<WasmFunctionBody> bodies)
    {
        var section = new WasmWriter();
        section.WriteVarUInt32((uint)bodies.Count);
        foreach (var body in bodies)
        {
            var functionWriter = new WasmWriter();
            functionWriter.WriteVarUInt32((uint)body.Locals.Count);
            foreach (var local in body.Locals)
            {
                functionWriter.WriteVarUInt32(local.Count);
                functionWriter.WriteByte(local.Type);
            }
            functionWriter.WriteBytes(body.Code);
            functionWriter.WriteByte((byte)WasmOpcode.End);
            section.WriteVarUInt32((uint)functionWriter.Length);
            section.WriteBytes(functionWriter.ToArray());
        }
        _writer.WriteSection((byte)WasmSection.Code, section);
    }

    public void EmitDataSection(IReadOnlyList<DataSegment> segments)
    {
        if (segments.Count == 0) return;
        var section = new WasmWriter();
        section.WriteVarUInt32((uint)segments.Count);
        foreach (var segment in segments)
        {
            section.WriteByte(0x00);  // mode = 0
            section.WriteByte((byte)WasmOpcode.ConstInt32);
            section.WriteVarInt32((int)segment.Offset);
            section.WriteByte((byte)WasmOpcode.End);
            section.WriteVarUInt32((uint)segment.Data.Length);
            section.WriteBytes(segment.Data);
        }
        _writer.WriteSection((byte)WasmSection.Data, section);
    }

    public byte[] BuildModule()
    {
        var module = new WasmWriter();
        module.WriteBytes(WasmConstants.WasmMagic);
        module.WriteBytes(WasmConstants.WasmVersion);
        module.WriteBytes(_writer.ToArray());
        return module.ToArray();
    }
}

public enum WasmImportKind : byte
{
    Function = 0x00,
    Table = 0x01,
    Memory = 0x02,
    Global = 0x03,
}

internal readonly record struct WasmImport(string Module, string Name, WasmImportKind Kind, uint TypeIndex);

internal readonly record struct WasmExport(string Name, byte Kind, uint Index);

internal readonly record struct WasmLocal(byte Type, uint Count);

internal readonly record struct WasmFunctionBody(IReadOnlyList<WasmLocal> Locals, byte[] Code);
