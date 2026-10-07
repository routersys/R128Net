namespace R128Net.Tests;

public class KWeightingTests
{
    [Fact]
    public void CoefficientsMatchTheReferenceExactlyAtEverySampleRate()
    {
        double[] rates = ReferenceData.Load("filter_rates").Values;
        ReferenceArray coefficients = ReferenceData.Load("filter_coeffs");

        Assert.Equal(rates.Length, coefficients.Rows);
        Assert.Equal(10, coefficients.Columns);

        for (int r = 0; r < rates.Length; ++r)
        {
            KWeighting weighting = KWeighting.Create(rates[r]);

            for (int i = 0; i < FilterTaps.Length; ++i)
            {
                BitwiseAssert.Equal(coefficients.Values[(r * 10) + i], weighting.Numerator[i],
                    $"rate {rates[r]} numerator {i}", 4);
                BitwiseAssert.Equal(coefficients.Values[(r * 10) + 5 + i], weighting.Denominator[i],
                    $"rate {rates[r]} denominator {i}", 4);
            }
        }
    }

    [Fact]
    public void LeadingDenominatorTapIsUnity()
    {
        double[] rates = ReferenceData.Load("filter_rates").Values;

        foreach (double rate in rates)
        {
            KWeighting weighting = KWeighting.Create(rate);
            BitwiseAssert.Equal(1.0, weighting.Denominator[0], $"rate {rate} denominator 0");
        }
    }
}
