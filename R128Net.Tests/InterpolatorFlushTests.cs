namespace R128Net.Tests;

public class InterpolatorFlushTests
{
    private const int Taps = 49;
    private const int Factor = 4;

    private static readonly double[] Amplitudes =
    [
        1.0, 1e3, 1e-3, 1e-30, 2e-38, 1.2e-38, 1.1e-38, 5e-39, 1e-45, 1e-300, 0.0, 1e-37
    ];

    private sealed unsafe class FlushingOracle
    {
        private readonly int _channels;
        private readonly int _delay;
        private readonly int[] _counts;
        private readonly int[] _indices;
        private readonly double[] _coefficients;
        private readonly double[][] _history;
        private int _position;

        public FlushingOracle(Interpolator interpolator, int channels)
        {
            _channels = channels;
            _delay = interpolator.Delay;
            _counts = new int[Factor];
            _indices = new int[Factor * _delay];
            _coefficients = new double[Factor * _delay];
            _history = new double[channels][];

            for (int f = 0; f < Factor; ++f)
            {
                _counts[f] = interpolator.Counts[f];
            }

            for (int i = 0; i < Factor * _delay; ++i)
            {
                _indices[i] = interpolator.Indices[i];
                _coefficients[i] = interpolator.Coefficients[i];
            }

            for (int c = 0; c < channels; ++c)
            {
                _history[c] = new double[_delay];
            }
        }

        public void Accumulate(double[] input, int offset, int frames, double[] peaks)
        {
            for (int frame = 0; frame < frames; ++frame)
            {
                for (int channel = 0; channel < _channels; ++channel)
                {
                    double[] line = _history[channel];
                    line[_position] = Denormal.Flush((float)Denormal.Flush(
                        input[((offset + frame) * _channels) + channel]));

                    double peak = peaks[channel];

                    for (int f = 0; f < Factor; ++f)
                    {
                        double accumulator = 0.0;

                        for (int t = 0; t < _counts[f]; ++t)
                        {
                            int i = _position - _indices[(f * _delay) + t];
                            if (i < 0)
                            {
                                i += _delay;
                            }

                            accumulator = Denormal.Flush(accumulator
                                + Denormal.Flush(line[i] * _coefficients[(f * _delay) + t]));
                        }

                        double value = Denormal.Flush((float)accumulator);
                        double magnitude = value > -value ? value : -value;
                        if (magnitude > peak)
                        {
                            peak = magnitude;
                        }
                    }

                    peaks[channel] = peak;
                }

                ++_position;
                if (_position == _delay)
                {
                    _position = 0;
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
                mode = (int)((seed >> 8) % 4u);
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
                    _ => amplitude * (1.0 + (unit * 1e-6)),
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
                int take = Math.Min(1 + (int)(chunkSeed % 9u), Frames - offset);

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
