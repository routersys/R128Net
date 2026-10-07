namespace R128Net.Tests;

public class InterpolatorScreenTests
{
    private const int Taps = 49;
    private const int Factor = 4;
    private const int Base = 16;

    private static readonly double[] Amplitudes =
    [
        1e-35, 1e-30, 1e-20, 1e-8, 1e-3, 0.25, 1.0, 1e3, 1e10, 1e20, 1e30, 1e37, 3e38
    ];

    private static readonly double[] Deltas =
    [
        -1e-2, -1e-4, -1e-5, -2e-6, -1e-6, -1e-7, -1e-9, 0.0, 1e-9, 1e-7, 1e-6, 2e-6, 1e-5,
        1e-4, 1e-3, 1e-2, 0.1, 1.0
    ];

    private static uint Next(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }

    private static double Unit(ref uint state)
    {
        return ((Next(ref state) / 4294967296.0) * 2.0) - 1.0;
    }

    private static unsafe Interpolator Prepare(StateMemory memory, int channels)
    {
        Interpolator interpolator = Interpolator.Bind(memory, Taps, Factor, channels);
        interpolator.Initialize(Taps, Factor);
        return interpolator;
    }

    private static unsafe double HighestOutput(Interpolator interpolator, float[] history)
    {
        int width = interpolator.Counts[1];
        int delay = interpolator.Delay;
        int zeroIndex = interpolator.Indices[0];
        double zeroCoefficient = interpolator.Coefficients[0];
        double highest = 0.0;

        for (int k = 0; k < 8; ++k)
        {
            for (int f = 1; f <= 3; ++f)
            {
                double accumulator = 0.0;
                for (int t = 0; t < width; ++t)
                {
                    accumulator += (double)history[Base + k - t]
                        * interpolator.Coefficients[(f * delay) + t];
                }

                double magnitude = Math.Abs(accumulator);
                if (magnitude > highest)
                {
                    highest = magnitude;
                }
            }

            double zero = Math.Abs((double)history[Base + k - zeroIndex] * zeroCoefficient);
            if (zero > highest)
            {
                highest = zero;
            }
        }

        return highest;
    }

    private static unsafe void Fill(float[] history, ref uint state, int round,
        Interpolator interpolator)
    {
        double amplitude = Amplitudes[(int)(Next(ref state) % (uint)Amplitudes.Length)];
        int from = Base - 12;
        int to = Base + 8;
        Array.Clear(history);

        switch (round % 8)
        {
            case 0:
                for (int i = from; i < to; ++i)
                {
                    history[i] = (float)(Unit(ref state) * amplitude);
                }

                break;

            case 1:
                for (int spikes = 1 + (int)(Next(ref state) % 3u); spikes > 0; --spikes)
                {
                    int at = from + (int)(Next(ref state) % (uint)(to - from));
                    history[at] = (float)(Unit(ref state) * amplitude);
                }

                break;

            case 2:
                {
                    double frequency = 0.01 + (0.45 * (Unit(ref state) + 1.0) / 2.0);
                    for (int i = from; i < to; ++i)
                    {
                        history[i] = (float)(amplitude
                            * (Math.Sin(2.0 * Math.PI * frequency * i) + (0.01 * Unit(ref state))));
                    }

                    break;
                }

            case 3:
                {
                    int phase = 1 + (int)(Next(ref state) % 3u);
                    int lane = (int)(Next(ref state) % 8u);
                    int delay = interpolator.Delay;
                    for (int t = 0; t < interpolator.Counts[1]; ++t)
                    {
                        double sign = interpolator.Coefficients[(phase * delay) + t] < 0.0 ? -1.0 : 1.0;
                        history[Base + lane - t] = (float)(sign * amplitude);
                    }

                    break;
                }

            case 4:
                for (int i = from; i < to; ++i)
                {
                    history[i] = (float)amplitude;
                }

                break;

            case 5:
                for (int i = from; i < to; ++i)
                {
                    history[i] = (float)(Unit(ref state) * amplitude);
                }

                history[from + (int)(Next(ref state) % (uint)(to - from))] =
                    (Next(ref state) & 3u) switch
                    {
                        0u => float.NaN,
                        1u => float.PositiveInfinity,
                        2u => float.NegativeInfinity,
                        _ => float.NaN,
                    };
                break;

            case 6:
                for (int i = from; i < to; ++i)
                {
                    history[i] = (Next(ref state) % 4u) switch
                    {
                        0u => 0.0f,
                        1u => BitConverter.Int32BitsToSingle(
                            (int)(Next(ref state) % 0x007FFFFFu) + 1),
                        2u => BitConverter.Int32BitsToSingle(0x00800000),
                        _ => (float)(Unit(ref state) * amplitude),
                    };
                }

                break;

            default:
                for (int i = from; i < to; ++i)
                {
                    double exponent = -35.0 + (72.0 * (Unit(ref state) + 1.0) / 2.0);
                    history[i] = (float)(Math.Pow(10.0, exponent) * (Unit(ref state) < 0.0 ? -1.0 : 1.0));
                }

                break;
        }
    }

    [Fact]
    public unsafe void TheScreenNeverPassesWhenAnOutputWouldExceedTheTop()
    {
        using StateMemory memory = new(Interpolator.GetRequiredBytes(Taps, Factor, 1));
        Interpolator interpolator = Prepare(memory, 1);
        Assert.NotEqual(0, interpolator.HeadCount);

        float[] history = new float[Base + 24];
        uint state = 88172645u;
        long passes = 0;

        for (int round = 0; round < 400000; ++round)
        {
            Fill(history, ref state, round, interpolator);
            double highest = HighestOutput(interpolator, history);

            fixed (float* single = &history[Base])
            {
                foreach (double delta in Deltas)
                {
                    double top = highest * (1.0 + delta);
                    if (interpolator.CannotExceedForTest(single, top))
                    {
                        ++passes;
                        Assert.True(highest <= top,
                            $"round {round}, delta {delta}: the screen passed with top {top:R} "
                            + $"below the highest output {highest:R}");
                    }
                }

                foreach (double top in new[] { 0.0, 1e-38, 1e-30, double.MaxValue,
                    double.PositiveInfinity })
                {
                    if (interpolator.CannotExceedForTest(single, top))
                    {
                        ++passes;
                        Assert.True(highest <= top,
                            $"round {round}, top {top:R}: the screen passed below the highest "
                            + $"output {highest:R}");
                    }
                }
            }
        }

        Assert.True(passes > 100000, $"the screen passed only {passes} times");
    }

    [Fact]
    public unsafe void TheScreenPassesAlmostEveryBlockOfSteadyNoise()
    {
        const int Frames = 200000;

        using StateMemory memory = new(Interpolator.GetRequiredBytes(Taps, Factor, 1));
        Interpolator interpolator = Prepare(memory, 1);

        float[] stream = new float[Frames + 32];
        uint state = 2463534242u;
        for (int i = 0; i < stream.Length; ++i)
        {
            stream[i] = (float)(Unit(ref state) * 0.25);
        }

        int width = interpolator.Counts[1];
        int delay = interpolator.Delay;
        int zeroIndex = interpolator.Indices[0];

        double[] running = new double[Frames + 32];
        double highest = 0.0;
        for (int n = 16; n < Frames + 16; ++n)
        {
            for (int f = 1; f <= 3; ++f)
            {
                double accumulator = 0.0;
                for (int t = 0; t < width; ++t)
                {
                    accumulator += (double)stream[n - t] * interpolator.Coefficients[(f * delay) + t];
                }

                highest = Math.Max(highest, Math.Abs(accumulator));
            }

            highest = Math.Max(highest,
                Math.Abs((double)stream[n - zeroIndex] * interpolator.Coefficients[0]));
            running[n] = highest;
        }

        int passed = 0;
        int tried = 0;
        fixed (float* origin = stream)
        {
            for (int n = 4000; n + 8 <= Frames; n += 8)
            {
                ++tried;
                if (interpolator.CannotExceedForTest(origin + n, running[n - 1]))
                {
                    ++passed;
                }
            }
        }

        Assert.True(passed >= tried * 0.95,
            $"the screen passed {passed} of {tried} blocks of steady noise");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public unsafe void ResultsStayExactAroundTheRunningPeak(int channels)
    {
        const int WarmFrames = 640;
        const int TailFrames = 80;
        double[] ratios = [0.5, 0.97, 0.9999, 1.0, 1.00001, 1.01, 3.0];

        using StateMemory reference = new(Interpolator.GetRequiredBytes(Taps, Factor, channels));
        Interpolator prototype = Prepare(reference, channels);
        double center = prototype.Coefficients[(2 * prototype.Delay) + (prototype.Counts[1] / 2)];

        foreach (int shift in Enumerable.Range(0, 24))
        {
            foreach (double ratio in ratios)
            {
                foreach (double sign in new[] { 1.0, -1.0 })
                {
                    int frames = WarmFrames + 24 + TailFrames;
                    double[] input = new double[frames * channels];
                    uint state = 2463534242u;

                    for (int frame = 0; frame < WarmFrames; ++frame)
                    {
                        for (int c = 0; c < channels; ++c)
                        {
                            input[(frame * channels) + c] = Unit(ref state) * 0.25;
                        }
                    }

                    FlushingOracle probe = new(prototype, channels);
                    double[] warmPeaks = new double[channels];
                    probe.Accumulate(input, 0, WarmFrames, warmPeaks);

                    double magnitude = warmPeaks[0] * ratio / Math.Abs(center);
                    input[((WarmFrames + shift) * channels)] = sign * magnitude;
                    if (channels > 1)
                    {
                        input[((WarmFrames + shift) * channels) + 1] = -sign * magnitude * 0.5;
                    }

                    foreach (int chunk in new[] { frames, 64, 61, 8, 5 })
                    {
                        using StateMemory memory =
                            new(Interpolator.GetRequiredBytes(Taps, Factor, channels));
                        Interpolator candidate = Prepare(memory, channels);

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
                                    $"{channels} channels, shift {shift}, ratio {ratio}, "
                                    + $"sign {sign}, chunk {chunk}, after frame {offset + take}");
                            }
                        }
                    }
                }
            }
        }
    }
}
