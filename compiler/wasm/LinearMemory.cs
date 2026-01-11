namespace ToyLang.Wasm;

internal sealed class LinearMemory
{
    public const uint PageSize = 131_072;
    public const uint DefaultPageCount = 1;
    public const uint DefaultStart = sizeof(uint);

    private uint _cursor;

    public LinearMemory(uint start = DefaultStart)
    {
        _cursor = start;
    }

    public uint Allocate(uint size, uint alignment = 4)
    {
        if (size == 0)
            throw new ArgumentOutOfRangeException(nameof(size));

        if (!IsPowerOfTwo(alignment))
            throw new ArgumentOutOfRangeException(nameof(alignment));

        var aligned = Align(_cursor, alignment);
        _cursor = checked(aligned + size);
        return aligned;
    }

    public uint CurrentOffset => _cursor;

    private static bool IsPowerOfTwo(uint value) => value != 0 && (value & (value - 1)) == 0;

    private static uint Align(uint value, uint alignment)
    {
        var mask = alignment - 1;
        return (value & mask) == 0 ? value : (value + alignment - 1) & ~mask;
    }
}
