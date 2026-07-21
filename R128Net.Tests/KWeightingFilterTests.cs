namespace R128Net.Tests;

public class KWeightingFilterTests
{
    private const double ReferenceSampleRate = 48000;

    private static unsafe (double[] output, double[] state) Run(
        double[] input, int frames, int channels, ChannelPosition[] map, bool vectorized)
    {
        double[] output = new double[frames * channels];
        double[] state = new double[KWeightingFilter.StateTaps * channels];
        KWeighting weighting = KWeighting.Create(ReferenceSampleRate);

        fixed (double* source = input)
        fixed (double* destination = output)
        fixed (double* taps = state)
        fixed (ChannelPosition* channelMap = map)
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

        return (output, state);
    }

    private static (double[] output, double[] state) RunScalar(
        double[] input, int frames, int channels, ChannelPosition[] map)
    {
        return Run(input, frames, channels, map, vectorized: false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FilteredOutputMatchesTheReferenceExactly(bool vectorized)
    {
        ReferenceArray input = ReferenceData.Load("filter_input");
        ReferenceArray expected = ReferenceData.Load("filter_output");

        Assert.Equal(expected.Rows, input.Rows);
        Assert.Equal(expected.Columns, input.Columns);

        ChannelPosition[] map = [ChannelPosition.Left, ChannelPosition.Right];
        Assert.Equal(map.Length, input.Columns);

        (double[] output, _) = Run(
            input.Values, input.Rows, input.Columns, map, vectorized);

        BitwiseAssert.Equal(expected.Values, output, "filtered sample");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(16)]
    public void VectorisedPathAgreesWithTheScalarPathBitForBit(int channels)
    {
        const int Frames = 3000;

        double[] input = new double[Frames * channels];
        uint seed = 88675123u;
        for (int i = 0; i < input.Length; ++i)
        {
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            input[i] = ((seed / 4294967296.0) * 2.0) - 1.0;
        }

        for (int unused = -1; unused < channels; ++unused)
        {
            ChannelPosition[] map = new ChannelPosition[channels];
            for (int c = 0; c < channels; ++c)
            {
                map[c] = c == unused ? ChannelPosition.Unused : ChannelPosition.Left;
            }

            (double[] scalarOutput, double[] scalarState) =
                Run(input, Frames, channels, map, vectorized: false);
            (double[] vectorOutput, double[] vectorState) =
                Run(input, Frames, channels, map, vectorized: true);

            BitwiseAssert.Equal(scalarOutput, vectorOutput,
                $"{channels} channels with channel {unused} unused, output");
            BitwiseAssert.Equal(scalarState, vectorState,
                $"{channels} channels with channel {unused} unused, state");
        }
    }

    [Fact]
    public void FilterStateMatchesTheReferenceExactly()
    {
        ReferenceArray input = ReferenceData.Load("filter_input");
        ReferenceArray expected = ReferenceData.Load("filter_state");

        int channels = input.Columns;
        ChannelPosition[] map = [ChannelPosition.Left, ChannelPosition.Right];

        (_, double[] state) = RunScalar(input.Values, input.Rows, channels, map);

        for (int c = 0; c < channels; ++c)
        {
            BitwiseAssert.Equal(expected.Values[(c * 5) + 0], expected.Values[(c * 5) + 1],
                $"reference channel {c} keeps v0 equal to v1");

            for (int tap = 0; tap < KWeightingFilter.StateTaps; ++tap)
            {
                BitwiseAssert.Equal(
                    expected.Values[(c * 5) + tap + 1],
                    state[(tap * channels) + c],
                    $"channel {c} tap {tap + 1}");
            }
        }
    }

    [Fact]
    public void UnusedChannelsAreLeftUntouched()
    {
        ReferenceArray input = ReferenceData.Load("filter_input");
        int channels = input.Columns;
        ChannelPosition[] map = [ChannelPosition.Left, ChannelPosition.Unused];

        (double[] output, double[] state) = RunScalar(
            input.Values, input.Rows, channels, map);

        for (int i = 0; i < input.Rows; ++i)
        {
            BitwiseAssert.Equal(0.0, output[(i * channels) + 1], $"unused output frame {i}");
        }

        for (int tap = 0; tap < KWeightingFilter.StateTaps; ++tap)
        {
            BitwiseAssert.Equal(0.0, state[(tap * channels) + 1], $"unused state tap {tap}");
        }
    }

    [Fact]
    public unsafe void DenormalDecayMatchesTheReferenceExactly()
    {
        ReferenceArray expected = ReferenceData.Load("denormal_decay");
        Assert.Equal(FilterTaps.Length, expected.Columns);

        const int Chunk = 4800;
        const int Channels = 1;

        double[] noise = new double[Chunk];
        uint seed = 2463534242u;
        for (int i = 0; i < Chunk; ++i)
        {
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            noise[i] = ((seed / 4294967296.0) * 2.0) - 1.0;
        }

        double[] silence = new double[Chunk];
        double[] sink = new double[Chunk];
        double[] state = new double[KWeightingFilter.StateTaps * Channels];
        ChannelPosition[] map = [ChannelPosition.Left];
        KWeighting weighting = KWeighting.Create(ReferenceSampleRate);

        fixed (double* pNoise = noise)
        fixed (double* pSilence = silence)
        fixed (double* pSink = sink)
        fixed (double* pState = state)
        fixed (ChannelPosition* pMap = map)
        {
            KWeightingFilter.ProcessScalar<DoubleFormat, double>(
                pNoise, pSink, pState, pMap, Channels, Chunk, weighting);

            for (int k = 0; k < expected.Rows; ++k)
            {
                KWeightingFilter.ProcessScalar<DoubleFormat, double>(
                    pSilence, pSink, pState, pMap, Channels, Chunk, weighting);

                BitwiseAssert.Equal(expected.Values[(k * 5) + 0], expected.Values[(k * 5) + 1],
                    $"reference chunk {k} keeps v0 equal to v1");

                for (int tap = 0; tap < KWeightingFilter.StateTaps; ++tap)
                {
                    BitwiseAssert.Equal(
                        expected.Values[(k * 5) + tap + 1],
                        state[tap],
                        $"chunk {k} tap {tap + 1}");
                }
            }
        }
    }
}
