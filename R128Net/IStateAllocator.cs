namespace R128Net;

internal unsafe interface IStateAllocator
{
    void* Allocate(int count, nuint elementSize);
}
