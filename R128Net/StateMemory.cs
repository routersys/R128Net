using System.Runtime.InteropServices;

namespace R128Net;

internal sealed unsafe class StateMemory : IDisposable
{
    public const int AlignmentBytes = 64;

    private void* _buffer;

    public StateMemory(nuint byteCount)
    {
        Capacity = byteCount;
        if (byteCount == 0)
        {
            return;
        }

        _buffer = NativeMemory.AlignedAlloc(byteCount, AlignmentBytes);
        NativeMemory.Clear(_buffer, byteCount);
    }

    ~StateMemory()
    {
        Release();
    }

    public nuint Capacity { get; }

    public void* Base => _buffer;

    public static nuint GetReservedBytes(int count, nuint elementSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        nuint requested = checked((nuint)count * elementSize);
        nuint reserved = (requested + (AlignmentBytes - 1)) & ~(nuint)(AlignmentBytes - 1);

        if (reserved < requested)
        {
            throw new OverflowException(
                "The requested allocation size cannot be aligned without overflowing.");
        }

        return reserved;
    }

    public void Dispose()
    {
        Release();
        GC.SuppressFinalize(this);
    }

    private void Release()
    {
        if (_buffer is null)
        {
            return;
        }

        NativeMemory.AlignedFree(_buffer);
        _buffer = null;
    }
}
