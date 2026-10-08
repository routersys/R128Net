using System.Runtime.CompilerServices;

namespace R128Net;

internal static class LoudnessMath
{
    public const double RelativeGate = -10.0;

    public static readonly double RelativeGateFactor = Math.Pow(10.0, RelativeGate / 10.0);

    public static readonly double MinusTwentyDecibels = Math.Pow(10.0, -20.0 / 10.0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double EnergyToLoudness(double energy)
    {
        return (10 * (Math.Log(energy) / Math.Log(10.0))) - 0.691;
    }

    public static unsafe void SortAscending(double* values, nuint count)
    {
        if (count < 2)
        {
            return;
        }

        int length = checked((int)count);
        if (length <= InsertionSortLimit)
        {
            InsertionSort(values, length);
            return;
        }

        for (int start = (length / 2) - 1; start >= 0; --start)
        {
            SiftDown(values, start, length);
        }

        for (int end = length - 1; end > 0; --end)
        {
            (values[0], values[end]) = (values[end], values[0]);
            SiftDown(values, 0, end);
        }
    }

    private const int InsertionSortLimit = 16;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Precedes(double left, double right)
    {
        return left < right || (double.IsNaN(left) && !double.IsNaN(right));
    }

    private static unsafe void InsertionSort(double* values, int length)
    {
        for (int i = 1; i < length; ++i)
        {
            double item = values[i];
            int j = i - 1;
            while (j >= 0 && Precedes(item, values[j]))
            {
                values[j + 1] = values[j];
                --j;
            }

            values[j + 1] = item;
        }
    }

    private static unsafe void SiftDown(double* values, int root, int length)
    {
        double item = values[root];
        while (true)
        {
            int child = (2 * root) + 1;
            if (child >= length)
            {
                break;
            }

            if (child + 1 < length && Precedes(values[child], values[child + 1]))
            {
                ++child;
            }

            if (!Precedes(item, values[child]))
            {
                break;
            }

            values[root] = values[child];
            root = child;
        }

        values[root] = item;
    }

    public static int FindHistogramIndex(double energy)
    {
        int minimum = 0;
        int maximum = HistogramTables.BinCount;

        do
        {
            int middle = (minimum + maximum) / 2;
            if (energy >= HistogramTables.Boundaries[middle])
            {
                minimum = middle;
            }
            else
            {
                maximum = middle;
            }
        }
        while (maximum - minimum != 1);

        return minimum;
    }
}
