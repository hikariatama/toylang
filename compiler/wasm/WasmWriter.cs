using System.Text;

namespace ToyLang.Wasm;

internal sealed class WasmWriter
{
    private readonly List<byte> _buffer = new();

    public int Length => _buffer.Count;

    public void WriteByte(byte value) => _buffer.Add(value);

    public void WriteBytes(byte[] values)
    {
        if (values.Length == 0) return;
        _buffer.AddRange(values);
    }

    public void WriteVarUInt32(uint value)
    {
        var remaining = value;
        do
        {
            var next = (byte)(remaining & 0x7F);
            remaining >>= 7;
            if (remaining != 0)
            {
                next |= 0x80;
            }
            _buffer.Add(next);
        } while (remaining != 0);
    }

    public void WriteVarInt32(int value)
    {
        var more = true;
        var current = value;
        while (more)
        {
            var next = (byte)(current & 0x7F);
            current >>= 7;
            var signBitSet = (next & 0x40) != 0;
            var shouldStop = (current == 0 && !signBitSet) || (current == -1 && signBitSet);
            if (!shouldStop)
            {
                next |= 0x80;
            }
            else
            {
                more = false;
            }
            _buffer.Add(next);
        }
    }

    public void WriteString(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteVarUInt32((uint)bytes.Length);
        WriteBytes(bytes);
    }

    public void WriteSection(byte sectionId, WasmWriter content)
    {
        WriteByte(sectionId);
        WriteVarUInt32((uint)content.Length);
        WriteBytes(content.ToArray());
    }

    public byte[] ToArray() => _buffer.ToArray();
}
