namespace R128Net.Tests;

public class SamplePeakTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(8)]
    public unsafe void VectorisedPathAgreesWithTheScalarPathBitForBit(int channels)
    {
        foreach (int frames in new[] { 1, 2, 3, 7, 64, 1000, 4801 })
        {
            double[] input = new double[frames * channels];
            uint seed = 88675123u;
            for (int i = 0; i < input.Length; ++i)
            {
                seed ^= seed << 13;
                seed ^= seed >> 17;
                seed ^= seed << 5;
                input[i] = ((seed / 4294967296.0) * 2.0) - 1.0;
            }

            input[0] = 0.0;
            if (input.Length > 1)
            {
                input[1] = -0.0;
            }

            double[] vectorised = new double[channels];
            double[] scalar = new double[channels];

            fixed (double* source = input)
            fixed (double* first = vectorised)
            fixed (double* second = scalar)
            {
                SamplePeak.Accumulate<DoubleFormat, double>(source, first, channels, frames);
                SamplePeak.AccumulateScalar<DoubleFormat, double>(source, second, channels, frames);
            }

            BitwiseAssert.Equal(scalar, vectorised, $"{channels} channels, {frames} frames");
        }
    }

    [Fact]
    public unsafe void NotANumberIsIgnoredExactlyAsTheReferenceDoes()
    {
        const int Channels = 2;
        double[] input = [double.NaN, 0.5, 0.25, double.NaN, -0.75, 0.125];

        double[] vectorised = new double[Channels];
        double[] scalar = new double[Channels];

        fixed (double* source = input)
        fixed (double* first = vectorised)
        fixed (double* second = scalar)
        {
            SamplePeak.Accumulate<DoubleFormat, double>(source, first, Channels, 3);
            SamplePeak.AccumulateScalar<DoubleFormat, double>(source, second, Channels, 3);
        }

        BitwiseAssert.Equal(scalar, vectorised, "peaks with not a number present");
        BitwiseAssert.Equal(0.75, vectorised[0], "channel 0 peak");
        BitwiseAssert.Equal(0.5, vectorised[1], "channel 1 peak");
    }

    private static uint Next(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }

    private static unsafe void Compare<TFormat, TSample>(TSample[] data, int channels, int frames,
        string context)
        where TFormat : struct, ISampleFormat<TSample>
        where TSample : unmanaged
    {
        double[] vectorised = new double[channels];
        double[] scalar = new double[channels];

        fixed (TSample* source = data)
        fixed (double* first = vectorised)
        fixed (double* second = scalar)
        {
            SamplePeak.Accumulate<TFormat, TSample>(source, first, channels, frames);
            SamplePeak.AccumulateScalar<TFormat, TSample>(source, second, channels, frames);
        }

        BitwiseAssert.Equal(scalar, vectorised, context);
    }

    public static TheoryData<int> ChannelCounts => new() { 1, 2, 3, 4, 5, 6, 7, 8, 9, 12, 16, 24 };

    [Theory]
    [MemberData(nameof(ChannelCounts))]
    public void DoubleAndSingleSamplesAgreeWithTheScalarPathOnTrickyValues(int channels)
    {
        double[] tricky =
        [
            0.0, -0.0, 1.0, -1.0, 0.5, -0.75, 1e-300, -5e-324, 1e300, -1e300, double.MaxValue,
            double.PositiveInfinity, double.NegativeInfinity, double.NaN, 3.4028234663852886e38,
        ];
        uint state = 2463534242u + (uint)channels;

        for (int frames = 1; frames <= 300; frames += (frames < 40 ? 1 : 7))
        {
            for (int round = 0; round < 4; ++round)
            {
                double[] doubles = new double[frames * channels];
                float[] singles = new float[frames * channels];
                for (int i = 0; i < doubles.Length; ++i)
                {
                    uint pick = Next(ref state);
                    doubles[i] = (pick % 4u) == 0u
                        ? tricky[(int)(pick >> 8) % tricky.Length]
                        : (((Next(ref state) / 4294967296.0) * 2.0) - 1.0)
                            * ((pick >> 4) % 7u == 0u ? 1e30 : 1.0);
                    singles[i] = (pick % 5u) == 0u
                        ? BitConverter.UInt32BitsToSingle(Next(ref state))
                        : (float)doubles[i];
                }

                Compare<DoubleFormat, double>(doubles, channels, frames,
                    $"double, {channels} channels, {frames} frames, round {round}");
                Compare<SingleFormat, float>(singles, channels, frames,
                    $"float, {channels} channels, {frames} frames, round {round}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(ChannelCounts))]
    public void IntegerSamplesAgreeWithTheScalarPathAtTheExtremes(int channels)
    {
        uint state = 88172645u + (uint)channels;

        for (int frames = 1; frames <= 300; frames += (frames < 40 ? 1 : 7))
        {
            for (int round = 0; round < 4; ++round)
            {
                short[] shorts = new short[frames * channels];
                int[] ints = new int[frames * channels];
                for (int i = 0; i < shorts.Length; ++i)
                {
                    uint pick = Next(ref state);
                    shorts[i] = (pick % 6u) switch
                    {
                        0u => short.MinValue,
                        1u => short.MaxValue,
                        2u => 0,
                        _ => (short)Next(ref state),
                    };
                    ints[i] = (pick % 7u) switch
                    {
                        0u => int.MinValue,
                        1u => int.MaxValue,
                        2u => 0,
                        3u => -int.MaxValue,
                        _ => (int)Next(ref state),
                    };
                }

                Compare<Int16Format, short>(shorts, channels, frames,
                    $"int16, {channels} channels, {frames} frames, round {round}");
                Compare<Int32Format, int>(ints, channels, frames,
                    $"int32, {channels} channels, {frames} frames, round {round}");
            }
        }
    }
}
