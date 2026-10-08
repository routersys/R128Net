namespace R128Net.Tests;

public unsafe class SortTests
{
    private static uint Next(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }

    private static void AssertSameAsRuntimeSort(double[] input, string context)
    {
        double[] expected = (double[])input.Clone();
        Array.Sort(expected);

        double[] actual = (double[])input.Clone();
        fixed (double* values = actual)
        {
            LoudnessMath.SortAscending(values, (nuint)actual.Length);
        }

        for (int i = 0; i < expected.Length; ++i)
        {
            bool same = double.IsNaN(expected[i])
                ? double.IsNaN(actual[i])
                : expected[i] == actual[i];
            Assert.True(same, $"{context}: element {i} is {actual[i]:R} but the runtime sort gives {expected[i]:R}");
        }
    }

    [Fact]
    public void EveryLengthUpToAFewHundredMatchesTheRuntimeSort()
    {
        uint state = 2463534242u;
        for (int length = 0; length <= 300; ++length)
        {
            double[] input = new double[length];
            for (int i = 0; i < length; ++i)
            {
                input[i] = Next(ref state) / 4294967296.0;
            }

            AssertSameAsRuntimeSort(input, $"random, length {length}");
        }
    }

    [Fact]
    public void OrderedAndRepeatedInputsMatchTheRuntimeSort()
    {
        foreach (int length in new[] { 2, 3, 16, 17, 18, 100, 1000, 4097 })
        {
            double[] ascending = new double[length];
            double[] descending = new double[length];
            double[] constant = new double[length];
            double[] fewValues = new double[length];
            uint state = (uint)length * 2654435761u;
            for (int i = 0; i < length; ++i)
            {
                ascending[i] = i;
                descending[i] = length - i;
                constant[i] = 0.25;
                fewValues[i] = Next(ref state) % 4u;
            }

            AssertSameAsRuntimeSort(ascending, $"ascending, length {length}");
            AssertSameAsRuntimeSort(descending, $"descending, length {length}");
            AssertSameAsRuntimeSort(constant, $"constant, length {length}");
            AssertSameAsRuntimeSort(fewValues, $"few distinct values, length {length}");
        }
    }

    [Fact]
    public void NotANumberSortsFirstAsInTheRuntimeSort()
    {
        uint state = 88172645u;
        foreach (int length in new[] { 5, 16, 17, 64, 500 })
        {
            for (int round = 0; round < 20; ++round)
            {
                double[] input = new double[length];
                for (int i = 0; i < length; ++i)
                {
                    uint pick = Next(ref state) % 8u;
                    input[i] = pick switch
                    {
                        0u => double.NaN,
                        1u => double.PositiveInfinity,
                        2u => double.NegativeInfinity,
                        3u => 0.0,
                        _ => (Next(ref state) / 4294967296.0) - 0.5,
                    };
                }

                AssertSameAsRuntimeSort(input, $"with NaN and infinities, length {length}, round {round}");
            }
        }
    }

    [Fact]
    public void LargeInputsMatchTheRuntimeSort()
    {
        uint state = 123456789u;
        double[] input = new double[100000];
        for (int i = 0; i < input.Length; ++i)
        {
            input[i] = Math.Exp(Next(ref state) / 4294967296.0 * 40.0 - 30.0);
        }

        AssertSameAsRuntimeSort(input, "large");
    }
}
