namespace R128Net.Tests;

public class InterpolatorFlushTests
{
    private const int Taps = 49;
    private const int Factor = 4;

    private static readonly double[] Amplitudes =
    [
        1.0, 1e3, 1e-3, 1e-30, 2e-38, 1.2e-38, 1.1e-38, 5e-39, 1e-45, 1e-300, 0.0, 1e-37,
        3.0e38, 1e38, 1.5e19, 1e39
    ];

    private static double NonFinite(uint seed, int channel)
    {
        return (seed >> 5) % 3u switch
        {
            0u => double.PositiveInfinity,
            1u => double.NegativeInfinity,
            _ => double.NaN,
        };
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public unsafe void PeakBoundSkippingKeepsTheResultExactAtTheBoundary(int channels)
    {
        using StateMemory denseMemory =
            new(Interpolator.GetRequiredBytes(Taps, Factor, channels));
        Interpolator dense = Interpolator.Bind(denseMemory, Taps, Factor, channels);
        dense.Initialize(Taps, Factor);

        int width = dense.Counts[1];
        double bound = 0.0;
        for (int f = 1; f <= 3; ++f)
        {
            double sum = 0.0;
            for (int t = 0; t < width; ++t)
            {
                sum += Math.Abs(dense.Coefficients[(f * dense.Delay) + t]);
            }

            bound = Math.Max(bound, sum);
        }

        FlushingOracle tracker = new(dense, channels);
        double[] reached = new double[channels];

        List<double> samples = [];

        void Append(List<double> segment)
        {
            double[] block = [.. segment];
            tracker.Accumulate(block, 0, block.Length / channels, reached);
            samples.AddRange(block);
        }

        List<double> loud = [];
        for (int i = 0; i < 400; ++i)
        {
            for (int c = 0; c < channels; ++c)
            {
                loud.Add(0.9 * Math.Sin(2.0 * Math.PI * 1000.0 * i / 48000.0 + c));
            }
        }

        for (int i = 0; i < 60 * channels; ++i)
        {
            loud.Add(0.0);
        }

        Append(loud);

        foreach (double factor in new[] { 0.5, 0.999, 0.999999, 1.0, 1.000001, 1.001, 1.5 })
        {
            double magnitude = reached[0] / bound * factor;
            List<double> segment = [];

            for (int k = 0; k < width; ++k)
            {
                double sign = dense.Coefficients[(2 * dense.Delay) + (width - 1 - k)] < 0.0 ? -1.0 : 1.0;
                for (int c = 0; c < channels; ++c)
                {
                    segment.Add(sign * magnitude * (c == 0 ? 1.0 : 0.5));
                }
            }

            for (int k = 0; k < 60 * channels; ++k)
            {
                segment.Add(0.0);
            }

            Append(segment);
        }

        double[] input = [.. samples];
        int frames = input.Length / channels;

        foreach (int chunk in new[] { frames, 4800, 255, 64, 61, 4 })
        {
            using StateMemory memory = new(Interpolator.GetRequiredBytes(Taps, Factor, channels));
            Interpolator candidate = Interpolator.Bind(memory, Taps, Factor, channels);
            candidate.Initialize(Taps, Factor);

            FlushingOracle oracle = new(candidate, channels);
            double[] expected = new double[channels];
            double[] actual = new double[channels];

            fixed (double* source = input)
            fixed (double* peak = actual)
            {
                for (int offset = 0; offset < frames; offset += chunk)
                {
                    int take = Math.Min(chunk, frames - offset);
                    oracle.Accumulate(input, offset, take, expected);
                    candidate.AccumulatePeaksDense<DoubleFormat, double>(
                        source + (offset * channels), peak, channels, take);

                    BitwiseAssert.Equal(expected, actual,
                        $"{channels} channels, chunk {chunk}, after frame {offset + take}");
                }
            }
        }
    }

    [Theory]
    [InlineData(1, 1u)]
    [InlineData(2, 2u)]
    [InlineData(2, 3u)]
    [InlineData(5, 4u)]
    public unsafe void SparseSpikesAfterALargePeakKeepTheResultExact(int channels, uint seed)
    {
        const int Frames = 30000;

        double[] input = new double[Frames * channels];
        uint state = 2463534242u + (seed * 7919u);
        for (int frame = 0; frame < Frames; ++frame)
        {
            for (int c = 0; c < channels; ++c)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                double unit = ((state / 4294967296.0) * 2.0) - 1.0;
                double value = unit * 0.01;

                if (frame < 400)
                {
                    value = 0.9 * Math.Sin(2.0 * Math.PI * 1000.0 * frame / 48000.0 + c);
                }
                else if ((state >> 20) % 97u == 0u)
                {
                    value = unit * (((state >> 8) % 1000u) / 1000.0);
                }

                input[(frame * channels) + c] = value;
            }
        }

        foreach (int chunk in new[] { Frames, 4800, 977, 64, 63, 5 })
        {
            using StateMemory memory = new(Interpolator.GetRequiredBytes(Taps, Factor, channels));
            Interpolator candidate = Interpolator.Bind(memory, Taps, Factor, channels);
            candidate.Initialize(Taps, Factor);

            FlushingOracle oracle = new(candidate, channels);
            double[] expected = new double[channels];
            double[] actual = new double[channels];

            fixed (double* source = input)
            fixed (double* peak = actual)
            {
                for (int offset = 0; offset < Frames; offset += chunk)
                {
                    int take = Math.Min(chunk, Frames - offset);
                    oracle.Accumulate(input, offset, take, expected);
                    candidate.AccumulatePeaksDense<DoubleFormat, double>(
                        source + (offset * channels), peak, channels, take);

                    BitwiseAssert.Equal(expected, actual,
                        $"{channels} channels, seed {seed}, chunk {chunk}, after frame {offset + take}");
                }
            }
        }
    }

    private static double[] BuildInput(int channels, int frames, uint seed)
    {
        double[] input = new double[frames * channels];
        double amplitude = 1.0;
        int mode = 0;

        for (int frame = 0; frame < frames; ++frame)
        {
            if (frame % 37 == 0)
            {
                seed ^= seed << 13;
                seed ^= seed >> 17;
                seed ^= seed << 5;
                amplitude = Amplitudes[(int)(seed % (uint)Amplitudes.Length)];
                mode = (int)((seed >> 8) % 5u);
            }

            for (int c = 0; c < channels; ++c)
            {
                seed ^= seed << 13;
                seed ^= seed >> 17;
                seed ^= seed << 5;
                double unit = ((seed / 4294967296.0) * 2.0) - 1.0;

                double value = mode switch
                {
                    0 => unit * amplitude,
                    1 => ((frame + c) % 2 == 0 ? 1.0 : -1.0) * amplitude,
                    2 => BitConverter.Int32BitsToSingle(
                        0x007FFFFE + (int)((seed >> 4) % 4u)) * (unit < 0.0 ? -1.0 : 1.0),
                    3 => amplitude * (1.0 + (unit * 1e-6)),
                    _ => frame % 5 == 0 ? NonFinite(seed, c) : unit * amplitude,
                };

                input[(frame * channels) + c] = value;
            }
        }

        return input;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public unsafe void FlushFreeAccumulationMatchesPerOperationFlushing(int channels)
    {
        const int Frames = 12000;

        double[] input = BuildInput(channels, Frames, 88172645u + (uint)channels);

        using StateMemory denseMemory =
            new(Interpolator.GetRequiredBytes(Taps, Factor, channels));
        Interpolator dense = Interpolator.Bind(denseMemory, Taps, Factor, channels);
        dense.Initialize(Taps, Factor);
        Assert.Equal(3, dense.DensePhaseCount);

        using StateMemory generalMemory =
            new(Interpolator.GetRequiredBytes(Taps, Factor, channels));
        Interpolator general = Interpolator.Bind(generalMemory, Taps, Factor, channels);
        general.Initialize(Taps, Factor);

        FlushingOracle oracle = new(dense, channels);

        double[] expected = new double[channels];
        double[] densePeaks = new double[channels];
        double[] generalPeaks = new double[channels];

        uint chunkSeed = 2463534242u;
        int offset = 0;

        fixed (double* source = input)
        fixed (double* densePeak = densePeaks)
        fixed (double* generalPeak = generalPeaks)
        {
            while (offset < Frames)
            {
                chunkSeed ^= chunkSeed << 13;
                chunkSeed ^= chunkSeed >> 17;
                chunkSeed ^= chunkSeed << 5;
                int span = (chunkSeed >> 28) switch
                {
                    < 8u => 1 + (int)(chunkSeed % 9u),
                    < 12u => 1 + (int)(chunkSeed % 70u),
                    _ => 1 + (int)(chunkSeed % 400u),
                };
                int take = Math.Min(span, Frames - offset);

                Array.Clear(expected);
                Array.Clear(densePeaks);
                Array.Clear(generalPeaks);

                oracle.Accumulate(input, offset, take, expected);
                dense.AccumulatePeaksDense<DoubleFormat, double>(
                    source + (offset * channels), densePeak, channels, take);
                general.AccumulatePeaksGeneral<DoubleFormat, double>(
                    source + (offset * channels), generalPeak, channels, take);

                BitwiseAssert.Equal(expected, densePeaks,
                    $"dense path, {channels} channels, frame {offset}, {take} frames");
                BitwiseAssert.Equal(expected, generalPeaks,
                    $"general path, {channels} channels, frame {offset}, {take} frames");

                offset += take;
            }
        }
    }
}
