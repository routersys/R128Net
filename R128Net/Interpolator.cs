using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace R128Net;

[StateLayout]
internal unsafe partial struct Interpolator
{
    public const double AlmostZero = 0.000001;
    public const int BlockFrames = 64;
    private const int DenseTail = BlockFrames + 4;

    public int Factor;
    public int Taps;
    public int Delay;
    public int Position;
    public int* Counts;
    public int* Indices;
    public double* Coefficients;
    public double* History;
    public int DensePhaseCount;

    public static int GetDelay(int taps, int factor)
    {
        return (taps + factor - 1) / factor;
    }

    private static int HistoryLength(int delay)
    {
        return Math.Max(2 * delay, delay + DenseTail);
    }

    public static void Layout<TAllocator>(ref TAllocator allocator, int taps, int factor,
        int channels, ref Interpolator state)
        where TAllocator : struct, IStateAllocator
    {
        int delay = GetDelay(taps, factor);
        state.Counts = (int*)allocator.Allocate(factor, sizeof(int));
        state.Indices = (int*)allocator.Allocate(factor * delay, (nuint)sizeof(int));
        state.Coefficients = (double*)allocator.Allocate(factor * delay, sizeof(double));
        state.History = (double*)allocator.Allocate(
            channels * HistoryLength(delay), sizeof(double));
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
        for (int i = 0; i < channels * HistoryLength(Delay); ++i)
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
    private static Vector256<double> Raise(Vector256<double> peaks, Vector256<double> candidates)
    {
        if (Avx.IsSupported)
        {
            return Avx.Max(candidates, peaks);
        }

        return Vector256.ConditionalSelect(
            Vector256.GreaterThan(candidates, peaks), candidates, peaks);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Narrowed(double magnitude)
    {
        return Denormal.Flush((float)magnitude);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal void AccumulatePeaksDense<TFormat, TSample>(
        TSample* source, double* peaks, int channels, int frames)
        where TFormat : struct, ISampleFormat<TSample>
        where TSample : unmanaged
    {
        int delay = Delay;
        int stride = HistoryLength(delay);
        int width = Counts[1];
        int zeroIndex = Indices[0];
        Vector256<double> zeroCoefficient = Vector256.Create(Coefficients[0]);
        double* first = Coefficients + delay;
        double* second = Coefficients + (2 * delay);
        double* third = Coefficients + (3 * delay);

        double* lanes = stackalloc double[channels * 4];
        for (int i = 0; i < channels * 4; ++i)
        {
            lanes[i] = double.NegativeInfinity;
        }

        int start = 0;
        while (start < frames)
        {
            int count = Math.Min(BlockFrames, frames - start);

            for (int channel = 0; channel < channels; ++channel)
            {
                double* work = History + (channel * stride);

                for (int j = 0; j < count; ++j)
                {
                    work[delay + j] = Denormal.Flush(
                        (float)TFormat.ToUnit(source[((start + j) * channels) + channel]));
                }

                double* origin = work + delay;
                Vector256<double> lane = Vector256.Load(lanes + (channel * 4));

                for (int n = 0; n < count; n += 4)
                {
                    double* tap = origin + n;

                    Vector256<double> firstPhase = Vector256<double>.Zero;
                    Vector256<double> secondPhase = Vector256<double>.Zero;
                    Vector256<double> thirdPhase = Vector256<double>.Zero;

                    for (int t = 0; t < width; ++t)
                    {
                        Vector256<double> delayed = Vector256.Load(tap - t);

                        firstPhase += delayed * Vector256.Create(first[t]);
                        secondPhase += delayed * Vector256.Create(second[t]);
                        thirdPhase += delayed * Vector256.Create(third[t]);
                    }

                    Vector256<double> zeroPhase = Vector256.Load(tap - zeroIndex) * zeroCoefficient;

                    int remaining = count - n;
                    if (remaining >= 4)
                    {
                        lane = Raise(lane, Vector256.Abs(firstPhase));
                        lane = Raise(lane, Vector256.Abs(secondPhase));
                        lane = Raise(lane, Vector256.Abs(thirdPhase));
                        lane = Raise(lane, Vector256.Abs(zeroPhase));
                    }
                    else
                    {
                        Vector256<double> live = Vector256.LessThan(
                            Vector256.Create(0L, 1L, 2L, 3L),
                            Vector256.Create((long)remaining)).AsDouble();
                        Vector256<double> none = Vector256.Create(double.NegativeInfinity);

                        lane = Raise(lane, Vector256.ConditionalSelect(
                            live, Vector256.Abs(firstPhase), none));
                        lane = Raise(lane, Vector256.ConditionalSelect(
                            live, Vector256.Abs(secondPhase), none));
                        lane = Raise(lane, Vector256.ConditionalSelect(
                            live, Vector256.Abs(thirdPhase), none));
                        lane = Raise(lane, Vector256.ConditionalSelect(
                            live, Vector256.Abs(zeroPhase), none));
                    }
                }

                lane.Store(lanes + (channel * 4));

                for (int j = 0; j < delay; ++j)
                {
                    work[j] = work[count + j];
                }
            }

            start += count;
        }

        for (int channel = 0; channel < channels; ++channel)
        {
            double peak = peaks[channel];

            double top = double.NegativeInfinity;
            for (int k = 0; k < 4; ++k)
            {
                double candidate = lanes[(channel * 4) + k];
                if (candidate > top)
                {
                    top = candidate;
                }
            }

            if (top >= 0.0)
            {
                double narrowed = Narrowed(top);
                if (narrowed > peak)
                {
                    peak = narrowed;
                }
            }

            peaks[channel] = peak;
        }

        Position = (int)((Position + (long)frames) % delay);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    internal void AccumulatePeaksGeneral<TFormat, TSample>(
        TSample* source, double* peaks, int channels, int frames)
        where TFormat : struct, ISampleFormat<TSample>
        where TSample : unmanaged
    {
        int delay = Delay;
        int stride = HistoryLength(delay);
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
