namespace R128Net;

internal unsafe struct MeasuringAllocator : IStateAllocator
{
    public nuint Total;

    public void* Allocate(int count, nuint elementSize)
    {
        Total += StateMemory.GetReservedBytes(count, elementSize);
        return null;
    }
}
