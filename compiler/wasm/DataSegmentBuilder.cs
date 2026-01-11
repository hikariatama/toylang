using System.Buffers.Binary;
using System.Text;

namespace ToyLang.Wasm;

internal sealed class DataSegmentBuilder
{
    private readonly Dictionary<string, uint> _stringLookup = new(StringComparer.Ordinal);
    private readonly List<DataSegment> _segments = new();

    public uint AddString(string value, LinearMemory memory)
    {
        if (_stringLookup.TryGetValue(value, out var existing))
            return existing;

        var bytes = Encoding.UTF8.GetBytes(value);
        var length = (uint)bytes.Length;
        var totalLength = 4u + length + 1u;
        var offset = memory.Allocate(totalLength, 4);

        var data = new byte[totalLength];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), length);
        if (length > 0)
        {
            Buffer.BlockCopy(bytes, 0, data, 4, (int)length);
        }
        data[^1] = 0;

        _segments.Add(new DataSegment(offset, data));
        _stringLookup[value] = offset;
        return offset;
    }

    public bool HasData => _segments.Count > 0;

    public IReadOnlyList<DataSegment> Build() => _segments;
}

internal readonly record struct DataSegment(uint Offset, byte[] Data);
