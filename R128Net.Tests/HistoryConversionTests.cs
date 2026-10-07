namespace R128Net.Tests;

public class HistoryConversionTests
{
    private const int Stride = 160;

    private static uint Next(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }

    private static unsafe void Check<TFormat, TSample>(TSample[] data, int channels, int frames,
        string context)
        where TFormat : struct, ISampleFormat<TSample>
        where TSample : unmanaged
    {
        float[] actual = new float[(channels * Stride) + 16];
        float[] expected = new float[(channels * Stride) + 16];

        Array.Fill(actual, 12345.0f);
        Array.Fill(expected, 12345.0f);

        for (int channel = 0; channel < channels; ++channel)
        {
            for (int j = 0; j < frames; ++j)
            {
                expected[(channel * Stride) + 8 + j] =
                    Denormal.Flush((float)TFormat.ToUnit(data[(j * channels) + channel]));
            }
        }

        fixed (TSample* source = data)
        fixed (float* history = actual)
        {
            Interpolator.ConvertBlock<TFormat, TSample>(source, channels, frames, history + 8, Stride);
        }

        for (int i = 0; i < actual.Length; ++i)
        {
            if (float.IsNaN(expected[i]) && float.IsNaN(actual[i]))
            {
                continue;
            }

            if (BitConverter.SingleToInt32Bits(expected[i]) != BitConverter.SingleToInt32Bits(actual[i]))
            {
                Assert.Fail($"{context}: element {i} expected {expected[i]:R} "
                    + $"({BitConverter.SingleToInt32Bits(expected[i]):X8}) but was {actual[i]:R} "
                    + $"({BitConverter.SingleToInt32Bits(actual[i]):X8})");
            }
        }
    }

    private static double[] TrickyDoubles()
    {
        return
        [
            0.0, -0.0, 1.0, -1.0, 0.5, 1e-40, -1e-40, 1.1754943508222875e-38, 1.1754942e-38,
            1.17549421e-38, 1.1754944e-38, 5e-324, -5e-324, 2.2250738585072014e-308, 1e-300,
            3.4028234663852886e38, 3.4028235677973366e38, 3.402823669209385e38, 1e39, -1e39,
            1e300, double.MaxValue, double.PositiveInfinity, double.NegativeInfinity,
            double.NaN, 16777217.0, 16777219.0, 0.1, 0.7, 1.0000001192092896,
        ];
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(6)]
    public void DoubleSamplesConvertExactlyAsTheScalarDefinition(int channels)
    {
        double[] tricky = TrickyDoubles();
        uint state = 88172645u + (uint)channels;

        for (int frames = 0; frames <= 70; ++frames)
        {
            for (int round = 0; round < 6; ++round)
            {
                double[] data = new double[frames * channels];
                for (int i = 0; i < data.Length; ++i)
                {
                    uint pick = Next(ref state);
                    data[i] = (pick % 3u) switch
                    {
                        0u => tricky[(int)(pick >> 8) % tricky.Length],
                        1u => BitConverter.Int64BitsToDouble(
                            ((long)Next(ref state) << 32) | Next(ref state)),
                        _ => (((Next(ref state) / 4294967296.0) * 2.0) - 1.0)
                            * Math.Pow(10.0, ((int)(Next(ref state) % 90u)) - 60),
                    };
                }

                Check<DoubleFormat, double>(data, channels, frames,
                    $"double, {channels} channels, {frames} frames, round {round}");
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(6)]
    public void SingleSamplesConvertExactlyAsTheScalarDefinition(int channels)
    {
        uint state = 2463534242u + (uint)channels;

        for (int frames = 0; frames <= 70; ++frames)
        {
            for (int round = 0; round < 6; ++round)
            {
                float[] data = new float[frames * channels];
                for (int i = 0; i < data.Length; ++i)
                {
                    uint pick = Next(ref state);
                    data[i] = (pick % 4u) switch
                    {
                        0u => BitConverter.UInt32BitsToSingle(Next(ref state)),
                        1u => BitConverter.UInt32BitsToSingle(Next(ref state) & 0x807FFFFFu),
                        2u => BitConverter.UInt32BitsToSingle(
                            (Next(ref state) & 0x80000000u) | 0x00800000u
                            | ((Next(ref state) % 3u) - 1u)),
                        _ => ((Next(ref state) / 4294967296.0f) * 2.0f) - 1.0f,
                    };
                }

                Check<SingleFormat, float>(data, channels, frames,
                    $"float, {channels} channels, {frames} frames, round {round}");
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(6)]
    public void Int16SamplesConvertExactlyAsTheScalarDefinition(int channels)
    {
        uint state = 123456789u + (uint)channels;

        for (int frames = 0; frames <= 70; ++frames)
        {
            for (int round = 0; round < 6; ++round)
            {
                short[] data = new short[frames * channels];
                for (int i = 0; i < data.Length; ++i)
                {
                    uint pick = Next(ref state);
                    data[i] = (pick % 5u) switch
                    {
                        0u => short.MinValue,
                        1u => short.MaxValue,
                        2u => (short)((pick >> 8) % 3u - 1u),
                        _ => (short)Next(ref state),
                    };
                }

                Check<Int16Format, short>(data, channels, frames,
                    $"int16, {channels} channels, {frames} frames, round {round}");
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(6)]
    public void Int32SamplesConvertExactlyAsTheScalarDefinition(int channels)
    {
        uint state = 362436069u + (uint)channels;

        for (int frames = 0; frames <= 70; ++frames)
        {
            for (int round = 0; round < 6; ++round)
            {
                int[] data = new int[frames * channels];
                for (int i = 0; i < data.Length; ++i)
                {
                    uint pick = Next(ref state);
                    data[i] = (pick % 6u) switch
                    {
                        0u => int.MinValue,
                        1u => int.MaxValue,
                        2u => (int)((pick >> 8) % 3u) - 1,
                        3u => (1 << 24) + (int)((pick >> 8) % 5u) - 2,
                        4u => int.MaxValue - (int)((pick >> 8) % 300u),
                        _ => (int)Next(ref state),
                    };
                }

                Check<Int32Format, int>(data, channels, frames,
                    $"int32, {channels} channels, {frames} frames, round {round}");
            }
        }
    }
}
