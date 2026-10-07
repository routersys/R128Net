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
    public double* History;
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
        state.History = (double*)allocator.Allocate(2 * channels * delay, sizeof(double));
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
        for (int i = 0; i < 2 * channels * Delay; ++i)
        {
            History[i] = 0.0;
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
        double magnitude = Math.Abs((double)Denormal.Flush((float)accumulator));
        return magnitude > peak ? magnitude : peak;
    }

    internal void AccumulatePeaksDense<TFormat, TSample>(
        TSample* source, double* peaks, int channels, int frames)
        where TFormat : struct, ISampleFormat<TSample>
        where TSample : unmanaged
    {
        int delay = Delay;
        int stride = 2 * delay;
        int width = Counts[1];
        int zeroIndex = Indices[0];
        double zeroCoefficient = Coefficients[0];
        int position = Position;
        int frame = 0;

        for (; frame + 2 <= frames; frame += 2)
        {
            int first = position;
            int second = position + 1 == delay ? 0 : position + 1;
            int firstBase = first + delay;
            int secondBase = second + delay;

            for (int channel = 0; channel < channels; ++channel)
            {
                double* ring = History + (channel * stride);

                double head = Denormal.Flush(
                    (float)TFormat.ToUnit(source[(frame * channels) + channel]));
                double next = Denormal.Flush(
                    (float)TFormat.ToUnit(source[((frame + 1) * channels) + channel]));

                ring[first] = head;
                ring[firstBase] = head;
                ring[second] = next;
                ring[secondBase] = next;

                double peak = peaks[channel];

                peak = Widen(ring[firstBase - zeroIndex] * zeroCoefficient, peak);
                peak = Widen(ring[secondBase - zeroIndex] * zeroCoefficient, peak);

                Vector256<double> headAccumulator = Vector256<double>.Zero;
                Vector256<double> nextAccumulator = Vector256<double>.Zero;

                double* headTap = ring + firstBase;
                double* nextTap = ring + secondBase;
                double* packed = PackedCoefficients;

                int t = 0;
                for (; t + 4 <= width; t += 4)
                {
                    Vector256<double> c0 = Vector256.Load(packed);
                    Vector256<double> c1 = Vector256.Load(packed + 4);
                    Vector256<double> c2 = Vector256.Load(packed + 8);
                    Vector256<double> c3 = Vector256.Load(packed + 12);

                    headAccumulator += Vector256.Create(headTap[0]) * c0;
                    nextAccumulator += Vector256.Create(nextTap[0]) * c0;
                    headAccumulator += Vector256.Create(headTap[-1]) * c1;
                    nextAccumulator += Vector256.Create(nextTap[-1]) * c1;
                    headAccumulator += Vector256.Create(headTap[-2]) * c2;
                    nextAccumulator += Vector256.Create(nextTap[-2]) * c2;
                    headAccumulator += Vector256.Create(headTap[-3]) * c3;
                    nextAccumulator += Vector256.Create(nextTap[-3]) * c3;

                    headTap -= 4;
                    nextTap -= 4;
                    packed += 16;
                }

                for (; t < width; ++t)
                {
                    Vector256<double> coefficients = Vector256.Load(packed);

                    headAccumulator += Vector256.Create(*headTap) * coefficients;
                    nextAccumulator += Vector256.Create(*nextTap) * coefficients;

                    --headTap;
                    --nextTap;
                    packed += 4;
                }

                peak = Widen(headAccumulator[0], peak);
                peak = Widen(headAccumulator[1], peak);
                peak = Widen(headAccumulator[2], peak);
                peak = Widen(nextAccumulator[0], peak);
                peak = Widen(nextAccumulator[1], peak);
                peak = Widen(nextAccumulator[2], peak);

                peaks[channel] = peak;
            }

            position = second + 1 == delay ? 0 : second + 1;
        }

        for (; frame < frames; ++frame)
        {
            int origin = position + delay;

            for (int channel = 0; channel < channels; ++channel)
            {
                double* ring = History + (channel * stride);

                double head = Denormal.Flush(
                    (float)TFormat.ToUnit(source[(frame * channels) + channel]));
                ring[position] = head;
                ring[origin] = head;

                double peak = peaks[channel];

                peak = Widen(ring[origin - zeroIndex] * zeroCoefficient, peak);

                Vector256<double> accumulator = Vector256<double>.Zero;

                double* tap = ring + origin;
                double* packed = PackedCoefficients;

                for (int t = 0; t < width; ++t)
                {
                    accumulator += Vector256.Create(*tap) * Vector256.Load(packed);

                    --tap;
                    packed += 4;
                }

                peak = Widen(accumulator[0], peak);
                peak = Widen(accumulator[1], peak);
                peak = Widen(accumulator[2], peak);

                peaks[channel] = peak;
            }

            position = position + 1 == delay ? 0 : position + 1;
        }

        Position = position;
    }

    internal void AccumulatePeaksGeneral<TFormat, TSample>(
        TSample* source, double* peaks, int channels, int frames)
        where TFormat : struct, ISampleFormat<TSample>
        where TSample : unmanaged
    {
        int delay = Delay;
        int stride = 2 * delay;
        int factor = Factor;
        int position = Position;

        for (int frame = 0; frame < frames; ++frame)
        {
            for (int channel = 0; channel < channels; ++channel)
            {
                double* ring = History + (channel * stride);
                double head = Denormal.Flush(
                    (float)TFormat.ToUnit(source[(frame * channels) + channel]));
                ring[position] = head;
                ring[position + delay] = head;

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

                        accumulator += ring[i] * coefficients[t];
                    }

                    double magnitude = Math.Abs((double)Denormal.Flush((float)accumulator));
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
