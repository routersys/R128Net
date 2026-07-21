namespace R128Net.Tests;

public class BlockEnergyListTests
{
    [Fact]
    public void GrowingListKeepsInsertionOrder()
    {
        BlockEnergyList list = default;
        list.Initialize(1000, preallocate: false);

        try
        {
            for (int i = 0; i < 500; ++i)
            {
                list.Add(i);
            }

            Assert.Equal((nuint)500, list.Count);
            for (nuint i = 0; i < 500; ++i)
            {
                Assert.Equal((double)i, list[i]);
            }
        }
        finally
        {
            list.Release();
        }
    }

    [Fact]
    public void ReachingTheMaximumEvictsTheOldestEntry()
    {
        BlockEnergyList list = default;
        list.Initialize(8, preallocate: false);

        try
        {
            for (int i = 0; i < 20; ++i)
            {
                list.Add(i);
            }

            Assert.Equal((nuint)8, list.Count);
            for (nuint i = 0; i < 8; ++i)
            {
                Assert.Equal(12.0 + i, list[i]);
            }
        }
        finally
        {
            list.Release();
        }
    }

    [Fact]
    public void TrimmingDropsTheOldestEntries()
    {
        BlockEnergyList list = default;
        list.Initialize(100, preallocate: false);

        try
        {
            for (int i = 0; i < 30; ++i)
            {
                list.Add(i);
            }

            list.Trim(10);

            Assert.Equal((nuint)10, list.Count);
            for (nuint i = 0; i < 10; ++i)
            {
                Assert.Equal(20.0 + i, list[i]);
            }
        }
        finally
        {
            list.Release();
        }
    }

    [Fact]
    public void PreallocatedListNeverGrows()
    {
        BlockEnergyList list = default;
        list.Initialize(16, preallocate: true);

        try
        {
            Assert.Equal((nuint)16, list.Capacity);

            for (int i = 0; i < 64; ++i)
            {
                list.Add(i);
            }

            Assert.Equal((nuint)16, list.Capacity);
            Assert.Equal((nuint)16, list.Count);
        }
        finally
        {
            list.Release();
        }
    }

    [Fact]
    public unsafe void CopyToProducesEntriesInOrderAfterWrapping()
    {
        BlockEnergyList list = default;
        list.Initialize(6, preallocate: true);

        try
        {
            for (int i = 0; i < 15; ++i)
            {
                list.Add(i);
            }

            double[] destination = new double[6];
            fixed (double* target = destination)
            {
                list.CopyTo(target);
            }

            Assert.Equal([9.0, 10.0, 11.0, 12.0, 13.0, 14.0], destination);
        }
        finally
        {
            list.Release();
        }
    }

    [Fact]
    public void ZeroMaximumDiscardsEverything()
    {
        BlockEnergyList list = default;
        list.Initialize(0, preallocate: false);

        try
        {
            list.Add(1.0);
            Assert.Equal((nuint)0, list.Count);
        }
        finally
        {
            list.Release();
        }
    }
}
