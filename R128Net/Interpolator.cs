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
    private const int ScreenFrames = 8;
    private const int ScreenReach = 12;
    private const int HeadWidth = 4;
    private const float ThresholdScale = 0.99999f;
    private const float MarginScale = 2e-5f;
    private const float MarginFloor = 1e-35f;

    public int Factor;
    public int Taps;
    public int Delay;
    public int Position;
    public int* Counts;
    public int* Indices;
    public double* Coefficients;
    public double* History;
    public int DensePhaseCount;
    public float* SingleHistory;
    public float* SingleCoefficients;
    public int HeadStart;
    public int HeadCount;
    public float ZeroSingle;
    public float TailFirst;
    public float TailSecond;
    public float TailThird;

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
        state.SingleCoefficients = (float*)allocator.Allocate(factor * delay, sizeof(float));
        state.SingleHistory = (float*)allocator.Allocate(
            channels * HistoryLength(delay), sizeof(float));
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
        PrepareScreen();
    }

    private void PrepareScreen()
    {
        HeadCount = 0;
        if (DensePhaseCount != 3)
        {
            return;
        }

        int width = Counts[1];
        if (width < HeadWidth || width > ScreenReach || Delay < ScreenReach)
        {
            return;
        }

        HeadStart = (width - HeadWidth) / 2;

        for (int f = 1; f <= DensePhaseCount; ++f)
        {
            for (int t = 0; t < width; ++t)
            {
                SingleCoefficients[(f * Delay) + t] = (float)Coefficients[(f * Delay) + t];
            }
        }

        ZeroSingle = (float)Coefficients[0];
        TailFirst = MeasureTail(1);
        TailSecond = MeasureTail(2);
        TailThird = MeasureTail(3);
        HeadCount = HeadWidth;
    }

    private float MeasureTail(int phase)
    {
        int width = Counts[1];
        double sum = 0.0;

        for (int t = 0; t < width; ++t)
        {
            if (t < HeadStart || t >= HeadStart + HeadWidth)
            {
                sum += Math.Abs(Coefficients[(phase * Delay) + t]);
            }
        }

        return (float)(sum * 1.000001);
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
            SingleHistory[i] = 0.0f;
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

    [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.NoInlining)]
    private static void Convert<TFormat, TSample>(
        TSample* source, int stride, int count, float* destination)
        where TFormat : struct, ISampleFormat<TSample>
        where TSample : unmanaged
    {
        for (int j = 0; j < count; ++j)
        {
            destination[j] = Denormal.Flush((float)TFormat.ToUnit(*source));
            source += stride;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<double> Widen(float* source)
    {
        if (Avx.IsSupported)
        {
            return Avx.ConvertToVector256Double(Sse.LoadVector128(source));
        }

        Vector128<float> narrow = Vector128.Load(source);
        return Vector256.Create(Vector128.WidenLower(narrow), Vector128.WidenUpper(narrow));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<float> Raise(Vector256<float> peaks, Vector256<float> candidates)
    {
        if (Avx.IsSupported)
        {
            return Avx.Max(candidates, peaks);
        }

        return Vector256.ConditionalSelect(
            Vector256.GreaterThan(candidates, peaks), candidates, peaks);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Highest(Vector256<float> value)
    {
        if (Sse.IsSupported)
        {
            Vector128<float> folded = Sse.Max(value.GetLower(), value.GetUpper());
            folded = Sse.Max(folded, Sse.Shuffle(folded, folded, 0x4E));
            folded = Sse.Max(folded, Sse.Shuffle(folded, folded, 0xB1));
            return folded.ToScalar();
        }

        float highest = value.GetElement(0);
        for (int i = 1; i < 8; ++i)
        {
            if (value.GetElement(i) > highest)
            {
                highest = value.GetElement(i);
            }
        }

        return highest;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Highest(Vector256<double> value)
    {
        Vector128<double> folded = Vector128.Max(value.GetLower(), value.GetUpper());
        return Math.Max(folded.GetElement(0), folded.GetElement(1));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CannotExceed(
        float* single, float threshold, float* firstSingle, float* secondSingle,
        float* thirdSingle, Vector256<float> zeroSingle, float tailFirst, float tailSecond,
        float tailThird, int headStart, int zeroIndex)
    {
        Vector256<float> window = Raise(
            Raise(Vector256.Abs(Vector256.Load(single - ScreenReach)),
                Vector256.Abs(Vector256.Load(single - 4))),
            Vector256.Abs(Vector256.Load(single)));
        float magnitude = Highest(window);
        float room = threshold - (MarginFloor + (MarginScale * magnitude));

        float* head = single - headStart;
        Vector256<float> d0 = Vector256.Load(head);
        Vector256<float> d1 = Vector256.Load(head - 1);
        Vector256<float> d2 = Vector256.Load(head - 2);
        Vector256<float> d3 = Vector256.Load(head - 3);

        Vector256<float> a1 = ((d0 * Vector256.Create(firstSingle[0]))
            + (d1 * Vector256.Create(firstSingle[1])))
            + ((d2 * Vector256.Create(firstSingle[2]))
            + (d3 * Vector256.Create(firstSingle[3])));
        Vector256<float> a2 = ((d0 * Vector256.Create(secondSingle[0]))
            + (d1 * Vector256.Create(secondSingle[1])))
            + ((d2 * Vector256.Create(secondSingle[2]))
            + (d3 * Vector256.Create(secondSingle[3])));
        Vector256<float> a3 = ((d0 * Vector256.Create(thirdSingle[0]))
            + (d1 * Vector256.Create(thirdSingle[1])))
            + ((d2 * Vector256.Create(thirdSingle[2]))
            + (d3 * Vector256.Create(thirdSingle[3])));
        Vector256<float> a0 = Vector256.Load(single - zeroIndex) * zeroSingle;

        Vector256<float> fits =
            Vector256.LessThanOrEqual(Vector256.Abs(a1),
                Vector256.Create(room - (tailFirst * magnitude)))
            & Vector256.LessThanOrEqual(Vector256.Abs(a2),
                Vector256.Create(room - (tailSecond * magnitude)))
            & Vector256.LessThanOrEqual(Vector256.Abs(a3),
                Vector256.Create(room - (tailThird * magnitude)))
            & Vector256.LessThanOrEqual(Vector256.Abs(a0), Vector256.Create(room));

        return fits.ExtractMostSignificantBits() == 0xFFu;
    }

    internal bool CannotExceedForTest(float* single, double top)
    {
        return HeadCount != 0
            && CannotExceed(single, Threshold(top),
                SingleCoefficients + Delay + HeadStart,
                SingleCoefficients + (2 * Delay) + HeadStart,
                SingleCoefficients + (3 * Delay) + HeadStart,
                Vector256.Create(ZeroSingle), TailFirst, TailSecond, TailThird, HeadStart,
                Indices[0]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Threshold(double top)
    {
        return (float)Math.Min(top, (double)float.MaxValue) * ThresholdScale;
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

        bool screened = HeadCount != 0;
        int headStart = HeadStart;
        float* firstSingle = SingleCoefficients + delay + headStart;
        float* secondSingle = SingleCoefficients + (2 * delay) + headStart;
        float* thirdSingle = SingleCoefficients + (3 * delay) + headStart;
        Vector256<float> zeroSingle = Vector256.Create(ZeroSingle);
        float tailFirst = TailFirst;
        float tailSecond = TailSecond;
        float tailThird = TailThird;

        double* lanes = stackalloc double[channels * 5];
        for (int i = 0; i < channels * 4; ++i)
        {
            lanes[i] = double.NegativeInfinity;
        }

        double* tops = lanes + (channels * 4);
        for (int i = 0; i < channels; ++i)
        {
            tops[i] = peaks[i];
        }

        int start = 0;
        while (start < frames)
        {
            int count = Math.Min(BlockFrames, frames - start);

            for (int channel = 0; channel < channels; ++channel)
            {
                float* singleOrigin = SingleHistory + (channel * stride) + delay;
                Convert<TFormat, TSample>(
                    source + (start * channels) + channel, channels, count, singleOrigin);

                Vector256<double> lane = Vector256.Load(lanes + (channel * 4));
                double top = tops[channel];
                float threshold = Threshold(top);

                for (int n = 0; n < count; n += 4)
                {
                    float* single = singleOrigin + n;

                    if (screened && (n & 4) == 0 && n + ScreenFrames <= count
                        && CannotExceed(single, threshold, firstSingle, secondSingle,
                            thirdSingle, zeroSingle, tailFirst, tailSecond, tailThird,
                            headStart, zeroIndex))
                    {
                        n += 4;
                        continue;
                    }

                    Vector256<double> firstPhase = Vector256<double>.Zero;
                    Vector256<double> secondPhase = Vector256<double>.Zero;
                    Vector256<double> thirdPhase = Vector256<double>.Zero;

                    for (int t = 0; t < width; ++t)
                    {
                        Vector256<double> delayed = Widen(single - t);

                        firstPhase += delayed * Vector256.Create(first[t]);
                        secondPhase += delayed * Vector256.Create(second[t]);
                        thirdPhase += delayed * Vector256.Create(third[t]);
                    }

                    Vector256<double> zeroPhase = Widen(single - zeroIndex) * zeroCoefficient;

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

                    if (screened)
                    {
                        double reached = Highest(lane);
                        if (reached > top)
                        {
                            top = reached;
                            threshold = Threshold(top);
                        }
                    }
                }

                lane.Store(lanes + (channel * 4));
                tops[channel] = top;

                for (int j = 0; j < delay; ++j)
                {
                    singleOrigin[j - delay] = singleOrigin[count + j - delay];
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
