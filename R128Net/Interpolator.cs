using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace R128Net;

[StateLayout]
internal unsafe partial struct Interpolator
{
    public const double AlmostZero = 0.000001;

    public int Factor;
    public int Taps;
    public int Delay;
    public int Position;
    public int* Counts;
    public int* Indices;
    public double* Coefficients;
    public double* PackedCoefficients;
    public float* History;
    public int DensePhaseCount;

    public static int GetDelay(int taps, int factor)
    {
        return (taps + factor - 1) / factor;
    }

    public static void Layout<TAllocator>(ref TAllocator allocator, int taps, int factor,
        int channels, ref Interpolator state)
        where TAllocator : struct, IStateAllocator
    {
        int delay = GetDelay(taps, factor);
        state.Counts = (int*)allocator.Allocate(factor, sizeof(int));
        state.Indices = (int*)allocator.Allocate(factor * delay, (nuint)sizeof(int));
        state.Coefficients = (double*)allocator.Allocate(factor * delay, sizeof(double));
        state.PackedCoefficients = (double*)allocator.Allocate(4 * delay, sizeof(double));
        state.History = (float*)allocator.Allocate(channels * delay, sizeof(float));
    }

    public void Initialize(int taps, int factor)
    {
        Taps = taps;
        Factor = factor;
        Delay = GetDelay(taps, factor);
        Position = 0;

        for (int f = 0; f < factor; ++f)
        {
            Counts[f] = 0;
        }

        for (int j = 0; j < taps; ++j)
        {
            double m = j - ((double)(taps - 1) / 2.0);
            double c = 1.0;
            if (Math.Abs(m) > AlmostZero)
            {
                c = Math.Sin(m * Math.PI / factor) / (m * Math.PI / factor);
            }

            c *= 0.5 * (1 - Math.Cos(2 * Math.PI * j / (taps - 1)));

            if (Math.Abs(c) > AlmostZero)
            {
                int f = j % factor;
                int t = Counts[f]++;
                Coefficients[(f * Delay) + t] = c;
                Indices[(f * Delay) + t] = j / factor;
            }
        }

        DensePhaseCount = MeasureDensePhases();

        for (int i = 0; i < 4 * Delay; ++i)
        {
            PackedCoefficients[i] = 0.0;
        }

        for (int f = 1; f <= DensePhaseCount; ++f)
        {
            for (int t = 0; t < Counts[f]; ++t)
            {
                PackedCoefficients[(t * 4) + f - 1] = Coefficients[(f * Delay) + t];
            }
        }
    }

    private int MeasureDensePhases()
    {
        if (Factor != 4 || Counts[0] != 1)
        {
            return 0;
        }

        int width = Counts[1];
        if (width == 0)
        {
            return 0;
        }

        for (int f = 1; f < Factor; ++f)
        {
            if (Counts[f] != width)
            {
                return 0;
            }

            for (int t = 0; t < width; ++t)
            {
                if (Indices[(f * Delay) + t] != t)
                {
                    return 0;
                }
            }
        }

        return Factor - 1;
    }

    public void Reset(int channels)
    {
        Position = 0;
        for (int i = 0; i < channels * Delay; ++i)
        {
            History[i] = 0.0f;
        }
    }

    public void AccumulatePeaks<TFormat, TSample>(
        TSample* source, double* peaks, int channels, int frames)
        where TFormat : struct, ISampleFormat<TSample>
        where TSample : unmanaged
    {
        if (DensePhaseCount == 3 && Vector256.IsHardwareAccelerated)
        {
            AccumulatePeaksDense<TFormat, TSample>(source, peaks, channels, frames);
        }
        else
        {
            AccumulatePeaksGeneral<TFormat, TSample>(source, peaks, channels, frames);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Widen(double accumulator, double peak)
    {
        double value = Denormal.Flush((float)accumulator);
        double magnitude = value > -value ? value : -value;
        return magnitude > peak ? magnitude : peak;
    }

    internal void AccumulatePeaksDense<TFormat, TSample>(
        TSample* source, double* peaks, int channels, int frames)
        where TFormat : struct, ISampleFormat<TSample>
        where TSample : unmanaged
    {
        int delay = Delay;
        int width = Counts[1];
        int zeroIndex = Indices[0];
        double zeroCoefficient = Coefficients[0];
        int position = Position;

        for (int frame = 0; frame < frames; ++frame)
        {
            for (int channel = 0; channel < channels; ++channel)
            {
                float* line = History + channel;
                line[position * channels] = Denormal.Flush(
                    (float)Denormal.Flush(TFormat.ToUnit(source[(frame * channels) + channel])));

                double peak = peaks[channel];

                int single = position - zeroIndex;
                if (single < 0)
                {
                    single += delay;
                }

                peak = Widen(
                    Denormal.Flush(Denormal.Flush(line[single * channels] * zeroCoefficient)),
                    peak);

                Vector256<double> accumulator = Vector256<double>.Zero;
                int split = position + 1 < width ? position + 1 : width;

                for (int t = 0; t < split; ++t)
                {
                    Vector256<double> z = Vector256.Create((double)line[(position - t) * channels]);
                    accumulator = Denormal.Flush(accumulator
                        + Denormal.Flush(z * Vector256.Load(PackedCoefficients + (t * 4))));
                }

                for (int t = split; t < width; ++t)
                {
                    Vector256<double> z = Vector256.Create(
                        (double)line[(position - t + delay) * channels]);
                    accumulator = Denormal.Flush(accumulator
                        + Denormal.Flush(z * Vector256.Load(PackedCoefficients + (t * 4))));
                }

                peak = Widen(accumulator[0], peak);
                peak = Widen(accumulator[1], peak);
                peak = Widen(accumulator[2], peak);

                peaks[channel] = peak;
            }

            ++position;
            if (position == delay)
            {
                position = 0;
            }
        }

        Position = position;
    }

    internal void AccumulatePeaksGeneral<TFormat, TSample>(
        TSample* source, double* peaks, int channels, int frames)
        where TFormat : struct, ISampleFormat<TSample>
        where TSample : unmanaged
    {
        int delay = Delay;
        int factor = Factor;
        int position = Position;

        for (int frame = 0; frame < frames; ++frame)
        {
            for (int channel = 0; channel < channels; ++channel)
            {
                float* line = History + channel;
                line[position * channels] = Denormal.Flush(
                    (float)Denormal.Flush(TFormat.ToUnit(source[(frame * channels) + channel])));

                double peak = peaks[channel];

                for (int f = 0; f < factor; ++f)
                {
                    int count = Counts[f];
                    double* coefficients = Coefficients + (f * delay);
                    int* indices = Indices + (f * delay);
                    double accumulator = 0.0;

                    for (int t = 0; t < count; ++t)
                    {
                        int i = position - indices[t];
                        if (i < 0)
                        {
                            i += delay;
                        }

                        accumulator = Denormal.Flush(accumulator
                            + Denormal.Flush(line[i * channels] * coefficients[t]));
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

            ++position;
            if (position == delay)
            {
                position = 0;
            }
        }

        Position = position;
    }
}
