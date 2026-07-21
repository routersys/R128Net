namespace R128Net.Tests;

public class TranscendentalTests
{
    private static void AssertExact(string name, Func<double, double> evaluate)
    {
        double[] input = ReferenceData.Load($"tr_{name}_input").Values;
        double[] expected = ReferenceData.Load($"tr_{name}_output").Values;

        for (int i = 0; i < input.Length; ++i)
        {
            BitwiseAssert.Equal(expected[i], evaluate(input[i]), $"{name} index {i}");
        }
    }

    [Fact]
    public void TanMatchesTheReferenceExactly()
    {
        AssertExact("tan", Math.Tan);
    }

    [Fact]
    public void LogMatchesTheReferenceExactly()
    {
        AssertExact("log", Math.Log);
    }

    [Fact]
    public void PowerOfTenStaysWithinOneUlpOfTheReference()
    {
        double[] input = ReferenceData.Load("tr_pow10_input").Values;
        double[] expected = ReferenceData.Load("tr_pow10_output").Values;

        long worst = 0;
        int mismatches = 0;
        for (int i = 0; i < input.Length; ++i)
        {
            long distance = BitwiseAssert.UlpDistance(expected[i], Math.Pow(10.0, input[i]));
            if (distance != 0)
            {
                ++mismatches;
                worst = Math.Max(worst, distance);
            }
        }

        Assert.True(worst <= 1, $"pow(10, x) diverges by {worst} ulp");
        Assert.True(mismatches * 1000 < input.Length,
            $"pow(10, x) mismatches {mismatches} of {input.Length} samples");
    }

    [Fact]
    public void EnergyToLoudnessMustNotUseLogBaseTenDirectly()
    {
        int differing = 0;
        for (int i = 1; i <= 200000; ++i)
        {
            double energy = i * 1e-7;
            long viaRatio = BitConverter.DoubleToInt64Bits(
                (10 * (Math.Log(energy) / Math.Log(10.0))) - 0.691);
            long viaLog10 = BitConverter.DoubleToInt64Bits((10 * Math.Log10(energy)) - 0.691);
            if (viaRatio != viaLog10)
            {
                ++differing;
            }
        }

        Assert.True(differing > 0,
            "the two formulations agree, so the reference formulation is no longer load bearing");
    }
}
