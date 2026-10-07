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
                    $"factor {factor} subfilter {f} tap {t} coefficient",
                    1);

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

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(6)]
    public unsafe void DensePathAgreesWithTheGeneralPathBitForBit(int channels)
    {
        const int Factor = 4;
        const int Frames = 2000;

        double[] input = new double[Frames * channels];
        uint seed = 362436069u;
        for (int i = 0; i < input.Length; ++i)
        {
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            input[i] = ((seed / 4294967296.0) * 2.0) - 1.0;
        }

        using StateMemory denseMemory =
            new(Interpolator.GetRequiredBytes(Taps, Factor, channels));
        Interpolator dense = Interpolator.Bind(denseMemory, Taps, Factor, channels);
        dense.Initialize(Taps, Factor);
        Assert.Equal(3, dense.DensePhaseCount);

        using StateMemory generalMemory =
            new(Interpolator.GetRequiredBytes(Taps, Factor, channels));
        Interpolator general = Interpolator.Bind(generalMemory, Taps, Factor, channels);
        general.Initialize(Taps, Factor);

        double[] densePeaks = new double[channels];
        double[] generalPeaks = new double[channels];

        fixed (double* source = input)
        fixed (double* densePeak = densePeaks)
        fixed (double* generalPeak = generalPeaks)
        {
            for (int pass = 0; pass < 4; ++pass)
            {
                dense.AccumulatePeaksDense<DoubleFormat, double>(
                    source, densePeak, channels, Frames);
                general.AccumulatePeaksGeneral<DoubleFormat, double>(
                    source, generalPeak, channels, Frames);
            }
        }

        Assert.Equal(dense.Position, general.Position);
        BitwiseAssert.Equal(generalPeaks, densePeaks, $"{channels} channel true peak");
    }
}
