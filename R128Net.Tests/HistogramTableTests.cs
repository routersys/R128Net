namespace R128Net.Tests;

public class HistogramTableTests
{
    private const int FirstCorrectedBoundary = 227;
    private const int SecondCorrectedBoundary = 725;

    private const double FirstCorrectedValue = 2.183232561846206e-05;
    private const double SecondCorrectedValue = 2.084970910123712;

    [Fact]
    public void EnergiesMatchTheReferenceExactly()
    {
        double[] expected = ReferenceData.Load("histogram_energies").Values;

        Assert.Equal(HistogramTables.BinCount, expected.Length);
        Assert.Equal(HistogramTables.BinCount, HistogramTables.Energies.Length);

        BitwiseAssert.Equal(expected, HistogramTables.Energies, "histogram energy");
    }

    [Fact]
    public void BoundariesMatchTheReferenceOutsideTheTwoCorrectedEntries()
    {
        double[] expected = ReferenceData.Load("histogram_boundaries").Values;

        Assert.Equal(HistogramTables.BinCount + 1, expected.Length);
        Assert.Equal(HistogramTables.BinCount + 1, HistogramTables.Boundaries.Length);

        for (int i = 0; i < expected.Length; ++i)
        {
            if (i is FirstCorrectedBoundary or SecondCorrectedBoundary)
            {
                continue;
            }

            BitwiseAssert.Equal(expected[i], HistogramTables.Boundaries[i],
                $"histogram boundary index {i}");
        }
    }

    [Theory]
    [InlineData(FirstCorrectedBoundary, FirstCorrectedValue)]
    [InlineData(SecondCorrectedBoundary, SecondCorrectedValue)]
    public void CorrectedBoundariesAreOneUlpAboveTheReference(int index, double corrected)
    {
        double[] reference = ReferenceData.Load("histogram_boundaries").Values;

        BitwiseAssert.Equal(corrected, HistogramTables.Boundaries[index],
            $"corrected boundary index {index}");

        Assert.Equal(1, BitwiseAssert.UlpDistance(reference[index],
            HistogramTables.Boundaries[index]));
        Assert.True(HistogramTables.Boundaries[index] > reference[index]);
    }

    [Fact]
    public void BoundariesAreStrictlyIncreasing()
    {
        for (int i = 1; i < HistogramTables.Boundaries.Length; ++i)
        {
            Assert.True(HistogramTables.Boundaries[i] > HistogramTables.Boundaries[i - 1],
                $"boundary index {i} is not greater than its predecessor");
        }
    }

    [Fact]
    public void EnergiesLieStrictlyInsideTheirBins()
    {
        for (int i = 0; i < HistogramTables.BinCount; ++i)
        {
            Assert.True(HistogramTables.Energies[i] > HistogramTables.Boundaries[i],
                $"energy index {i} is not above its lower boundary");
            Assert.True(HistogramTables.Energies[i] < HistogramTables.Boundaries[i + 1],
                $"energy index {i} is not below its upper boundary");
        }
    }
}
