using System.Runtime.InteropServices;

namespace R128Net;

internal unsafe struct BlockEnergyList
{
    private const nuint InitialCapacity = 64;

    private double* _items;
    private nuint _capacity;
    private nuint _head;
    private nuint _count;
    private nuint _maximum;
    private bool _canGrow;

    public readonly nuint Count => _count;

    public readonly nuint Capacity => _capacity;

    public readonly nuint Maximum => _maximum;

    public void Initialize(nuint maximum, bool preallocate)
    {
        _items = null;
        _capacity = 0;
        _head = 0;
        _count = 0;
        _maximum = maximum;
        _canGrow = !preallocate;

        if (preallocate && maximum != 0)
        {
            Reserve(maximum);
        }
    }

    public void Clear()
    {
        _head = 0;
        _count = 0;
    }

    public void Release()
    {
        if (_items is not null)
        {
            NativeMemory.AlignedFree(_items);
            _items = null;
        }

        _capacity = 0;
        _head = 0;
        _count = 0;
    }

    public double this[nuint index]
    {
        get
        {
            nuint slot = _head + index;
            if (slot >= _capacity)
            {
                slot -= _capacity;
            }
            return _items[slot];
        }
    }

    public void Add(double energy)
    {
        if (_maximum == 0)
        {
            return;
        }

        if (_count == _maximum)
        {
            _items[_head] = energy;
            ++_head;
            if (_head == _capacity)
            {
                _head = 0;
            }
            return;
        }

        if (_count == _capacity)
        {
            nuint target = _capacity == 0
                ? InitialCapacity
                : _capacity * 2;
            if (target > _maximum)
            {
                target = _maximum;
            }
            Reserve(target);
        }

        nuint slot = _head + _count;
        if (slot >= _capacity)
        {
            slot -= _capacity;
        }
        _items[slot] = energy;
        ++_count;
    }

    public void Trim(nuint maximum)
    {
        _maximum = maximum;

        while (_count > _maximum)
        {
            ++_head;
            if (_head == _capacity)
            {
                _head = 0;
            }
            --_count;
        }
    }

    public void CopyTo(double* destination)
    {
        for (nuint i = 0; i < _count; ++i)
        {
            destination[i] = this[i];
        }
    }

    private void Reserve(nuint capacity)
    {
        if (capacity <= _capacity)
        {
            return;
        }

        if (!_canGrow && _capacity != 0)
        {
            throw new InvalidOperationException(
                "The preallocated block list cannot grow.");
        }

        double* items = (double*)NativeMemory.AlignedAlloc(
            checked(capacity * (nuint)sizeof(double)), StateMemory.AlignmentBytes);

        for (nuint i = 0; i < _count; ++i)
        {
            items[i] = this[i];
        }

        if (_items is not null)
        {
            NativeMemory.AlignedFree(_items);
        }

        _items = items;
        _capacity = capacity;
        _head = 0;
    }
}
