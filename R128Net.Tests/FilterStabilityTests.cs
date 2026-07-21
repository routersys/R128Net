namespace R128Net.Tests;

public class FilterStabilityTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(18)]
    [InlineData(1122)]
    [InlineData(1681)]
    [InlineData(3364)]
    [InlineData(8000)]
    [InlineData(44100)]
    [InlineData(48000)]
    [InlineData(96000)]
    [InlineData(192000)]
    [InlineData(2822400)]
    public void StableSampleRatesAreReportedStable(double sampleRate)
    {
        Assert.True(KWeighting.Create(sampleRate).IsStable);
    }

    [Theory]
    [InlineData(17)]
    [InlineData(100)]
    [InlineData(421)]
    [InlineData(480)]
    [InlineData(561)]
    [InlineData(841)]
    [InlineData(1121)]
    [InlineData(1682)]
    [InlineData(3363)]
    public void DivergentSampleRatesAreReportedUnstable(double sampleRate)
    {
        Assert.False(KWeighting.Create(sampleRate).IsStable);
    }

    [Fact]
    public void EverySampleRateAtOrAboveThreeThousandThreeHundredSixtyFourIsStable()
    {
        for (int rate = 3364; rate <= 200000; ++rate)
        {
            Assert.True(KWeighting.Create(rate).IsStable, $"rate {rate} is unstable");
        }
    }

    [Fact]
    public void StabilityAgreesWithTheDecayOfALongImpulseResponse()
    {
        ReadOnlySpan<int> rates = [17, 100, 1682, 3363, 3364, 8000, 48000, 192000];
        Span<double> v = stackalloc double[FilterTaps.Length];

        const int Settle = 200000;
        const int Window = 100000;

        foreach (int rate in rates)
        {
            KWeighting weighting = KWeighting.Create(rate);

            v.Clear();
            double earlyPeak = 0.0;
            double latePeak = 0.0;
            bool overflowed = false;

            for (int n = 0; n < Settle + (2 * Window); ++n)
            {
                double v0 = (n == 0 ? 1.0 : 0.0)
                    - (weighting.Denominator[1] * v[1])
                    - (weighting.Denominator[2] * v[2])
                    - (weighting.Denominator[3] * v[3])
                    - (weighting.Denominator[4] * v[4]);
                v[4] = v[3];
                v[3] = v[2];
                v[2] = v[1];
                v[1] = v0;

                if (!double.IsFinite(v0))
                {
                    overflowed = true;
                    break;
                }

                double magnitude = Math.Abs(v0);
                if (n >= Settle + Window)
                {
                    latePeak = Math.Max(latePeak, magnitude);
                }
                else if (n >= Settle)
                {
                    earlyPeak = Math.Max(earlyPeak, magnitude);
                }
            }

            bool decays = !overflowed && latePeak <= earlyPeak;
            Assert.Equal(weighting.IsStable, decays);
        }
    }
}
