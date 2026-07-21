using System.Diagnostics;
using System.Globalization;
using Xunit.Abstractions;

namespace R128Net.Tests;

public class FilterThroughputTests
{
    private readonly ITestOutputHelper _output;

    public FilterThroughputTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static unsafe double RunOnce(bool vectorized, int channels, int frames,
        int repeats, double[] input, double[] output, double[] state,
        ChannelPosition[] map, in KWeighting weighting)
    {
        fixed (double* source = input)
        fixed (double* destination = output)
        fixed (double* taps = state)
        fixed (ChannelPosition* channelMap = map)
        {
            for (int i = 0; i < repeats; ++i)
            {
                if (vectorized)
                {
                    KWeightingFilter.Process<DoubleFormat, double>(
                        source, destination, taps, channelMap, channels, frames, weighting);
                }
                else
                {
                    KWeightingFilter.ProcessScalar<DoubleFormat, double>(
                        source, destination, taps, channelMap, channels, frames, weighting);
                }
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < repeats; ++i)
            {
                if (vectorized)
                {
                    KWeightingFilter.Process<DoubleFormat, double>(
                        source, destination, taps, channelMap, channels, frames, weighting);
                }
                else
                {
                    KWeightingFilter.ProcessScalar<DoubleFormat, double>(
                        source, destination, taps, channelMap, channels, frames, weighting);
                }
            }
            stopwatch.Stop();

            return stopwatch.Elapsed.TotalMilliseconds * 1e6
                / ((double)frames * channels * repeats);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    public void VectorisedFilterIsNotSlowerThanScalar(int channels)
    {
        const int Frames = 4800;
        const int Repeats = 200;

        double[] input = new double[Frames * channels];
        uint seed = 2463534242u;
        for (int i = 0; i < input.Length; ++i)
        {
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            input[i] = ((seed / 4294967296.0) * 2.0) - 1.0;
        }

        double[] output = new double[Frames * channels];
        double[] state = new double[KWeightingFilter.StateTaps * channels];
        ChannelPosition[] map = new ChannelPosition[channels];
        Array.Fill(map, ChannelPosition.Left);
        KWeighting weighting = KWeighting.Create(48000);

        double scalar = double.MaxValue;
        double vector = double.MaxValue;

        for (int round = 0; round < 9; ++round)
        {
            Array.Clear(state);
            double s = RunOnce(false, channels, Frames, Repeats,
                input, output, state, map, weighting);
            Array.Clear(state);
            double v = RunOnce(true, channels, Frames, Repeats,
                input, output, state, map, weighting);

            if (round == 0)
            {
                continue;
            }

            scalar = Math.Min(scalar, s);
            vector = Math.Min(vector, v);
        }

        CultureInfo culture = CultureInfo.InvariantCulture;
        _output.WriteLine(
            "channels {0}: scalar {1} ns/sample, vector {2} ns/sample, speedup {3}x",
            channels.ToString(culture),
            scalar.ToString("F3", culture),
            vector.ToString("F3", culture),
            (scalar / vector).ToString("F2", culture));

        Assert.True(vector <= scalar * 1.5,
            $"the vectorised path is slower than the scalar path at {channels} channels");
    }
}
