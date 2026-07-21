namespace R128Net.Tests;

public class LoudnessMathTests
{
    [Fact]
    public void GateFactorsMatchTheReferenceExactly()
    {
        BitwiseAssert.Equal(
            ReferenceData.Load("relative_gate_factor").Values[0],
            LoudnessMath.RelativeGateFactor,
            "relative gate factor");

        BitwiseAssert.Equal(
            ReferenceData.Load("minus_twenty_decibels").Values[0],
            LoudnessMath.MinusTwentyDecibels,
            "minus twenty decibels");
    }

    [Fact]
    public void HistogramIndexPlacesEveryBinEnergyInItsOwnBin()
    {
        for (int i = 0; i < HistogramTables.BinCount; ++i)
        {
            Assert.Equal(i, LoudnessMath.FindHistogramIndex(HistogramTables.Energies[i]));
        }
    }

    [Fact]
    public void HistogramIndexPlacesEveryBoundaryAtItsOwnBin()
    {
        for (int i = 0; i < HistogramTables.BinCount; ++i)
        {
            Assert.Equal(i, LoudnessMath.FindHistogramIndex(HistogramTables.Boundaries[i]));
        }
    }

    [Fact]
    public void HistogramIndexClampsBelowAndAbove()
    {
        Assert.Equal(0, LoudnessMath.FindHistogramIndex(0.0));
        Assert.Equal(0, LoudnessMath.FindHistogramIndex(double.Epsilon));
        Assert.Equal(HistogramTables.BinCount - 1,
            LoudnessMath.FindHistogramIndex(double.MaxValue));
    }

    [Fact]
    public void EnergyToLoudnessMatchesTheReferenceForEveryDumpedBlock()
    {
        double[] energies = ReferenceData.Load("loudness_blocks_stereo").Values;
        Assert.NotEmpty(energies);

        foreach (double energy in energies)
        {
            double expected = (10 * (Math.Log(energy) / Math.Log(10.0))) - 0.691;
            BitwiseAssert.Equal(expected, LoudnessMath.EnergyToLoudness(energy),
                $"energy {energy:E17}");
        }
    }

    [Fact]
    public void AbsoluteGateSitsAtMinusSeventyLufs()
    {
        BitwiseAssert.Equal(-70.0,
            Math.Round(LoudnessMath.EnergyToLoudness(HistogramTables.Boundaries[0]), 9),
            "absolute gate boundary");
    }
}
