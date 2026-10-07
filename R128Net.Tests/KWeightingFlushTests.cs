namespace R128Net.Tests;

public class KWeightingFlushTests
{
    private static readonly double[] Amplitudes =
    [
        1.0, 1e-3, 1e-100, 1e-250, 1e-270, 8.5e-272, 8.4e-272, 1e-273, 1e-280, 1e-300,
        1e-310, 5e-324, 0.0
    ];

    private static unsafe void OracleProcess(double[] input, int firstFrame, int frames,
        int channels, ChannelPosition[] map, double[] state, double[] output,
        in KWeighting weighting)
    {
        for (int channel = 0; channel < channels; ++channel)
        {
            if (map[channel] == ChannelPosition.Unused)
            {
                continue;
            }

            double s1 = state[channel];
            double s2 = state[channels + channel];
            double s3 = state[(2 * channels) + channel];
            double s4 = state[(3 * channels) + channel];

            for (int i = 0; i < frames; ++i)
            {
                int index = ((firstFrame + i) * channels) + channel;
                double x = Denormal.Flush(input[index]);

                double accumulator = Denormal.Flush(
                    x - Denormal.Flush(weighting.Denominator[1] * s1));
                accumulator = Denormal.Flush(
                    accumulator - Denormal.Flush(weighting.Denominator[2] * s2));
                accumulator = Denormal.Flush(
                    accumulator - Denormal.Flush(weighting.Denominator[3] * s3));
                double v0 = Denormal.Flush(
                    accumulator - Denormal.Flush(weighting.Denominator[4] * s4));

                double output0 = Denormal.Flush(
                    Denormal.Flush(weighting.Numerator[0] * v0)
                    + Denormal.Flush(weighting.Numerator[1] * s1));
                output0 = Denormal.Flush(
                    output0 + Denormal.Flush(weighting.Numerator[2] * s2));
                output0 = Denormal.Flush(
                    output0 + Denormal.Flush(weighting.Numerator[3] * s3));
                output0 = Denormal.Flush(
                    output0 + Denormal.Flush(weighting.Numerator[4] * s4));

                output[index] = output0;

                s4 = s3;
                s3 = s2;
                s2 = s1;
                s1 = v0;
            }

            state[channel] = s1;
            state[channels + channel] = s2;
            state[(2 * channels) + channel] = s3;
            state[(3 * channels) + channel] = s4;
        }
    }

    private static double[] BuildInput(int channels, uint seed)
    {
        const int Burst = 9600;
        const int Silence = 192000;
        const int Mixed = 24000;
        const int Random = 48000;
        int frames = Burst + Silence + Mixed + Random;

        double[] input = new double[frames * channels];
        double[] amplitude = new double[channels];

        for (int frame = 0; frame < frames; ++frame)
        {
            for (int c = 0; c < channels; ++c)
            {
                seed ^= seed << 13;
                seed ^= seed >> 17;
                seed ^= seed << 5;
                double unit = ((seed / 4294967296.0) * 2.0) - 1.0;
                double value;

                if (frame < Burst)
                {
                    value = unit * 0.5;
                }
                else if (frame < Burst + Silence)
                {
                    value = 0.0;
                }
                else if (frame < Burst + Silence + Mixed)
                {
                    value = (c % 2 == 0) ? unit * 1e-3 : 0.0;
                }
                else
                {
                    if (frame % 64 == 0)
                    {
                        amplitude[c] = Amplitudes[(int)((seed >> 3) % (uint)Amplitudes.Length)];
                    }

                    value = unit * amplitude[c];
                }

                input[(frame * channels) + c] = value;
            }
        }

        return input;
    }

    private static unsafe void Compare(int channels, double sampleRate, int unused,
        bool vectorized, bool whole = false)
    {
        double[] input = BuildInput(channels, 2463534242u + (uint)channels);
        int frames = input.Length / channels;

        ChannelPosition[] map = new ChannelPosition[channels];
        for (int c = 0; c < channels; ++c)
        {
            map[c] = c == unused ? ChannelPosition.Unused : ChannelPosition.Left;
        }

        KWeighting weighting = KWeighting.Create(sampleRate);

        double[] expected = new double[input.Length];
        double[] actual = new double[input.Length];
        double[] expectedState = new double[KWeightingFilter.StateTaps * channels];
        double[] actualState = new double[KWeightingFilter.StateTaps * channels];

        uint chunkSeed = 362436069u;
        int offset = 0;

        fixed (double* source = input)
        fixed (double* destination = actual)
        fixed (double* taps = actualState)
        fixed (ChannelPosition* channelMap = map)
        {
            while (offset < frames)
            {
                chunkSeed ^= chunkSeed << 13;
                chunkSeed ^= chunkSeed >> 17;
                chunkSeed ^= chunkSeed << 5;
                int span = (chunkSeed >> 28) switch
                {
                    < 4u => 1 + (int)(chunkSeed % 7u),
                    < 12u => 1 + (int)(chunkSeed % 600u),
                    _ => 1 + (int)(chunkSeed % 7000u),
                };
                int take = whole ? frames : Math.Min(span, frames - offset);

                OracleProcess(input, offset, take, channels, map, expectedState, expected,
                    weighting);

                if (vectorized)
                {
                    KWeightingFilter.Process<DoubleFormat, double>(
                        source + (offset * channels), destination + (offset * channels),
                        taps, channelMap, channels, take, weighting);
                }
                else
                {
                    KWeightingFilter.ProcessScalar<DoubleFormat, double>(
                        source + (offset * channels), destination + (offset * channels),
                        taps, channelMap, channels, take, weighting);
                }

                for (int i = 0; i < take * channels; ++i)
                {
                    int index = (offset * channels) + i;
                    if (BitConverter.DoubleToInt64Bits(expected[index])
                        != BitConverter.DoubleToInt64Bits(actual[index]))
                    {
                        BitwiseAssert.Equal(expected[index], actual[index],
                            $"rate {sampleRate}, {channels} channels, unused {unused}, "
                            + $"vectorized {vectorized}, frame {offset + (i / channels)}, "
                            + $"channel {i % channels}");
                    }
                }

                for (int i = 0; i < expectedState.Length; ++i)
                {
                    if (BitConverter.DoubleToInt64Bits(expectedState[i])
                        != BitConverter.DoubleToInt64Bits(actualState[i]))
                    {
                        BitwiseAssert.Equal(expectedState[i], actualState[i],
                            $"rate {sampleRate}, {channels} channels, unused {unused}, "
                            + $"vectorized {vectorized}, state {i} after frame {offset + take}");
                    }
                }

                offset += take;
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(7)]
    public void PlainArithmeticMatchesPerOperationFlushing(int channels)
    {
        Compare(channels, 48000, -1, vectorized: true);
        Compare(channels, 48000, -1, vectorized: false);
        Compare(channels, 48000, -1, vectorized: true, whole: true);
        Compare(channels, 48000, -1, vectorized: false, whole: true);

        if (channels >= 2)
        {
            Compare(channels, 48000, channels / 2, vectorized: true);
            Compare(channels, 48000, channels / 2, vectorized: true, whole: true);
        }
    }

    [Theory]
    [InlineData(16)]
    [InlineData(1122)]
    [InlineData(3364)]
    [InlineData(8000)]
    [InlineData(44100)]
    [InlineData(192000)]
    [InlineData(2822400)]
    public void EveryStableSampleRateMatchesPerOperationFlushing(double sampleRate)
    {
        Compare(2, sampleRate, -1, vectorized: true);
        Compare(5, sampleRate, -1, vectorized: true);
        Compare(2, sampleRate, -1, vectorized: true, whole: true);
        Compare(5, sampleRate, -1, vectorized: true, whole: true);
    }
}
