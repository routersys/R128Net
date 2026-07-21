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
