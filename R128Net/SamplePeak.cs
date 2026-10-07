using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace R128Net;

internal static unsafe class SamplePeak
{
    private const int MaximumPeriod = 3;

    public static void Accumulate<TFormat, TSample>(
        TSample* source, double* peaks, int channels, int frames)
        where TFormat : struct, ISampleFormat<TSample>
        where TSample : unmanaged
    {
        if (Avx2.IsSupported && frames > 0 && channels <= 64)
        {
            double* maxima = stackalloc double[channels];

            if (TryAbsoluteMaxima<TSample>(source, channels, frames, maxima))
            {
                for (int channel = 0; channel < channels; ++channel)
                {
                    Publish<TFormat, TSample>(peaks, channel, maxima[channel]);
                }

                return;
            }
        }

        AccumulateScalar<TFormat, TSample>(source, peaks, channels, frames);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void AccumulateScalar<TFormat, TSample>(
        TSample* source, double* peaks, int channels, int frames)
        where TFormat : struct, ISampleFormat<TSample>
        where TSample : unmanaged
    {
        for (int channel = 0; channel < channels; ++channel)
        {
            double max = 0.0;

            for (int i = 0; i < frames; ++i)
            {
                double magnitude = Math.Abs(TFormat.ToRaw(source[(i * channels) + channel]));
                if (magnitude > max)
                {
                    max = magnitude;
                }
            }

            Publish<TFormat, TSample>(peaks, channel, max);
        }
    }

    internal static bool TryAbsoluteMaxima<TSample>(
        TSample* source, int channels, int frames, double* maxima)
        where TSample : unmanaged
    {
        if (typeof(TSample) == typeof(double))
        {
            return Vectorised<DoubleLanes, double, double>(
                (double*)source, channels, frames, maxima);
        }

        if (typeof(TSample) == typeof(float))
        {
            return Vectorised<SingleLanes, float, float>(
                (float*)source, channels, frames, maxima);
        }

        if (typeof(TSample) == typeof(short))
        {
            return Vectorised<Int16Lanes, ushort, short>(
                (short*)source, channels, frames, maxima);
        }

        if (typeof(TSample) == typeof(int))
        {
            return Vectorised<Int32Lanes, uint, int>(
                (int*)source, channels, frames, maxima);
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static bool Vectorised<TLanes, TLane, TSample>(
        TSample* source, int channels, int frames, double* maxima)
        where TLanes : struct, ILanes<TSample, TLane>
        where TLane : unmanaged
        where TSample : unmanaged
    {
        int width = Vector256<TLane>.Count;
        int period = channels / (int)BigInteger.GreatestCommonDivisor(channels, width);
        int total = frames * channels;

        if (period > MaximumPeriod || total < width * period)
        {
            return false;
        }

        Vector256<TLane> first = Vector256<TLane>.Zero;
        Vector256<TLane> second = Vector256<TLane>.Zero;
        Vector256<TLane> third = Vector256<TLane>.Zero;
        Vector256<TLane> fourth = Vector256<TLane>.Zero;
        int index = 0;

        if (period == 1)
        {
            int step = 4 * width;
            for (; index + step <= total; index += step)
            {
                first = TLanes.Fold(first, source + index);
                second = TLanes.Fold(second, source + index + width);
                third = TLanes.Fold(third, source + index + (2 * width));
                fourth = TLanes.Fold(fourth, source + index + (3 * width));
            }

            for (; index + width <= total; index += width)
            {
                first = TLanes.Fold(first, source + index);
            }

            first = TLanes.Merge(TLanes.Merge(first, second), TLanes.Merge(third, fourth));
        }
        else
        {
            int step = period * width;
            for (; index + step <= total; index += step)
            {
                first = TLanes.Fold(first, source + index);
                second = TLanes.Fold(second, source + index + width);
                if (period == 3)
                {
                    third = TLanes.Fold(third, source + index + (2 * width));
                }
            }

            int phase = 0;
            for (; index + width <= total; index += width)
            {
                if (phase == 0)
                {
                    first = TLanes.Fold(first, source + index);
                }
                else if (phase == 1)
                {
                    second = TLanes.Fold(second, source + index);
                }
                else
                {
                    third = TLanes.Fold(third, source + index);
                }

                ++phase;
            }
        }

        for (int channel = 0; channel < channels; ++channel)
        {
            maxima[channel] = 0.0;
        }

        TLane* lanes = stackalloc TLane[width];

        for (int accumulator = 0; accumulator < (period == 1 ? 1 : period); ++accumulator)
        {
            Vector256<TLane> value = accumulator == 0 ? first : accumulator == 1 ? second : third;
            value.Store(lanes);

            for (int lane = 0; lane < width; ++lane)
            {
                int channel = ((accumulator * width) + lane) % channels;
                double candidate = TLanes.Widen(lanes[lane]);
                if (candidate > maxima[channel])
                {
                    maxima[channel] = candidate;
                }
            }
        }

        int tailChannel = index % channels;
        for (int position = index; position < total; ++position)
        {
            double magnitude = TLanes.Magnitude(source[position]);
            if (magnitude > maxima[tailChannel])
            {
                maxima[tailChannel] = magnitude;
            }

            if (++tailChannel == channels)
            {
                tailChannel = 0;
            }
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Publish<TFormat, TSample>(double* peaks, int channel, double max)
        where TFormat : struct, ISampleFormat<TSample>
        where TSample : unmanaged
    {
        double scaled = Denormal.Flush(max / TFormat.ScalingFactor);
        if (scaled > peaks[channel])
        {
            peaks[channel] = scaled;
        }
    }

    private interface ILanes<TSample, TLane>
        where TSample : unmanaged
        where TLane : unmanaged
    {
        static abstract Vector256<TLane> Fold(Vector256<TLane> accumulator, TSample* source);

        static abstract Vector256<TLane> Merge(Vector256<TLane> left, Vector256<TLane> right);

        static abstract double Widen(TLane lane);

        static abstract double Magnitude(TSample sample);
    }

    private readonly struct DoubleLanes : ILanes<double, double>
    {
        public static Vector256<double> Fold(Vector256<double> accumulator, double* source)
        {
            return Avx.Max(Vector256.Abs(Avx.LoadVector256(source)), accumulator);
        }

        public static Vector256<double> Merge(Vector256<double> left, Vector256<double> right)
        {
            return Avx.Max(left, right);
        }

        public static double Widen(double lane) => lane;

        public static double Magnitude(double sample) => Math.Abs(sample);
    }

    private readonly struct SingleLanes : ILanes<float, float>
    {
        public static Vector256<float> Fold(Vector256<float> accumulator, float* source)
        {
            return Avx.Max(Vector256.Abs(Avx.LoadVector256(source)), accumulator);
        }

        public static Vector256<float> Merge(Vector256<float> left, Vector256<float> right)
        {
            return Avx.Max(left, right);
        }

        public static double Widen(float lane) => lane;

        public static double Magnitude(float sample) => Math.Abs((double)sample);
    }

    private readonly struct Int16Lanes : ILanes<short, ushort>
    {
        public static Vector256<ushort> Fold(Vector256<ushort> accumulator, short* source)
        {
            return Avx2.Max(accumulator, Avx2.Abs(Avx.LoadVector256(source)));
        }

        public static Vector256<ushort> Merge(Vector256<ushort> left, Vector256<ushort> right)
        {
            return Avx2.Max(left, right);
        }

        public static double Widen(ushort lane) => lane;

        public static double Magnitude(short sample) => Math.Abs((double)sample);
    }

    private readonly struct Int32Lanes : ILanes<int, uint>
    {
        public static Vector256<uint> Fold(Vector256<uint> accumulator, int* source)
        {
            return Avx2.Max(accumulator, Avx2.Abs(Avx.LoadVector256(source)));
        }

        public static Vector256<uint> Merge(Vector256<uint> left, Vector256<uint> right)
        {
            return Avx2.Max(left, right);
        }

        public static double Widen(uint lane) => lane;

        public static double Magnitude(int sample) => Math.Abs((double)sample);
    }
}
