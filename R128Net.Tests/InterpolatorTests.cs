namespace R128Net.Tests;

public class InterpolatorTests
{
    private const int Taps = 49;

    [Theory]
    [InlineData(48000, 4)]
    [InlineData(96000, 2)]
    public unsafe void CoefficientsMatchTheReferenceExactly(int rate, int factor)
    {
        ReferenceArray header = ReferenceData.Load($"interp_header_{rate}");
        ReferenceArray counts = ReferenceData.Load($"interp_counts_{rate}");
        ReferenceArray coefficients = ReferenceData.Load($"interp_coeff_{rate}");
        ReferenceArray indices = ReferenceData.Load($"interp_index_{rate}");

        const int Channels = 1;
        using StateMemory memory = new(Interpolator.GetRequiredBytes(Taps, factor, Channels));
        Interpolator interpolator = Interpolator.Bind(memory, Taps, factor, Channels);
        interpolator.Initialize(Taps, factor);

        Assert.Equal(factor, (int)header.Values[0]);
        Assert.Equal(Taps, (int)header.Values[1]);
        Assert.Equal(interpolator.Delay, (int)header.Values[2]);

        int delay = interpolator.Delay;
        Assert.Equal(delay, coefficients.Columns);

        for (int f = 0; f < factor; ++f)
        {
            Assert.Equal((int)counts.Values[f], interpolator.Counts[f]);

            for (int t = 0; t < interpolator.Counts[f]; ++t)
            {
                BitwiseAssert.Equal(
                    coefficients.Values[(f * delay) + t],
                    interpolator.Coefficients[(f * delay) + t],
                    $"factor {factor} subfilter {f} tap {t} coefficient");

                Assert.Equal(
                    (int)indices.Values[(f * delay) + t],
                    interpolator.Indices[(f * delay) + t]);
            }
        }
    }

    [Fact]
    public unsafe void ZeroPhaseIsAPureDelay()
    {
        const int Factor = 4;
        const int Channels = 1;

        using StateMemory memory = new(Interpolator.GetRequiredBytes(Taps, Factor, Channels));
        Interpolator interpolator = Interpolator.Bind(memory, Taps, Factor, Channels);
        interpolator.Initialize(Taps, Factor);

        Assert.Equal(1, interpolator.Counts[0]);
        BitwiseAssert.Equal(1.0, interpolator.Coefficients[0], "zero phase coefficient");
        Assert.Equal((Taps - 1) / 2 / Factor, interpolator.Indices[0]);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(2)]
    public unsafe void DelayMatchesTheUpstreamFormula(int factor)
    {
        Assert.Equal((Taps + factor - 1) / factor, Interpolator.GetDelay(Taps, factor));
    }

    [Fact]
    public unsafe void SymmetricSubfiltersAreNotBitwiseSymmetric()
    {
        const int Factor = 4;
        const int Channels = 1;

        using StateMemory memory = new(Interpolator.GetRequiredBytes(Taps, Factor, Channels));
        Interpolator interpolator = Interpolator.Bind(memory, Taps, Factor, Channels);
        interpolator.Initialize(Taps, Factor);

        int delay = interpolator.Delay;
        int count = interpolator.Counts[1];
        Assert.Equal(count, interpolator.Counts[3]);

        bool mirrored = true;
        for (int t = 0; t < count; ++t)
        {
            if (BitConverter.DoubleToInt64Bits(interpolator.Coefficients[delay + t])
                != BitConverter.DoubleToInt64Bits(
                    interpolator.Coefficients[(3 * delay) + count - 1 - t]))
            {
                mirrored = false;
                break;
            }
        }

        Assert.False(mirrored);
    }
}
