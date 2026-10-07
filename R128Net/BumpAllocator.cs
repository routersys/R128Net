namespace R128Net;

internal unsafe struct BumpAllocator : IStateAllocator
{
    private readonly byte* _base;
    private readonly nuint _capacity;
    private nuint _used;

    public BumpAllocator(StateMemory memory)
    {
        _base = (byte*)memory.Base;
        _capacity = memory.Capacity;
        _used = 0;
    }

    public readonly nuint Used => _used;

    public void* Allocate(int count, nuint elementSize)
    {
        nuint reserved = StateMemory.GetReservedBytes(count, elementSize);

        if (_used + reserved > _capacity)
        {
            throw new InvalidOperationException(
                $"The state buffer of {_capacity} bytes cannot satisfy a further "
                + $"{reserved} bytes after {_used} bytes.");
        }

        void* result = _base + _used;
        _used += reserved;
        return result;
    }
}
