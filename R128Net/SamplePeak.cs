using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace R128Net;

internal static unsafe class SamplePeak
{
    public static void Accumulate<TFormat, TSample>(
        TSample* source, double* peaks, int channels, int frames)
        where TFormat : struct, ISampleFormat<TSample>
        where TSample : unmanaged
    {
        if (Vector256.IsHardwareAccelerated
            && (Vector256<double>.Count % channels) == 0
            && frames * channels >= Vector256<double>.Count)
        {
            AccumulateVectorised<TFormat, TSample>(source, peaks, channels, frames);
        }
        else
        {
            AccumulateScalar<TFormat, TSample>(source, peaks, channels, frames);
        }
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
                double current = TFormat.ToRaw(source[(i * channels) + channel]);
                double magnitude = current > -current ? current : -current;
                if (magnitude > max)
                {
                    max = magnitude;
                }
            }

            Publish<TFormat, TSample>(peaks, channel, max);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<double> Load<TFormat, TSample>(TSample* source)
        where TFormat : struct, ISampleFormat<TSample>
        where TSample : unmanaged
    {
        if (typeof(TSample) == typeof(double))
        {
            return Vector256.Load((double*)source);
        }

        return Vector256.Create(
            TFormat.ToRaw(source[0]), TFormat.ToRaw(source[1]),
            TFormat.ToRaw(source[2]), TFormat.ToRaw(source[3]));
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void AccumulateVectorised<TFormat, TSample>(
        TSample* source, double* peaks, int channels, int frames)
        where TFormat : struct, ISampleFormat<TSample>
        where TSample : unmanaged
    {
        int total = frames * channels;
        int width = Vector256<double>.Count;
        Vector256<double> running = Vector256<double>.Zero;

        int index = 0;
        for (; index + width <= total; index += width)
        {
            Vector256<double> values = Load<TFormat, TSample>(source + index);
            Vector256<double> negated = -values;
            Vector256<double> magnitude = Vector256.ConditionalSelect(
                Vector256.GreaterThan(values, negated), values, negated);
            running = Vector256.ConditionalSelect(
                Vector256.GreaterThan(magnitude, running), magnitude, running);
        }

        Span<double> lanes = stackalloc double[Vector256<double>.Count];
        running.CopyTo(lanes);

        for (int channel = 0; channel < channels; ++channel)
        {
            double max = 0.0;

            for (int lane = channel; lane < width; lane += channels)
            {
                if (lanes[lane] > max)
                {
                    max = lanes[lane];
                }
            }

            for (int tail = index + channel; tail < total; tail += channels)
            {
                double current = TFormat.ToRaw(source[tail]);
                double magnitude = current > -current ? current : -current;
                if (magnitude > max)
                {
                    max = magnitude;
                }
            }

            Publish<TFormat, TSample>(peaks, channel, max);
        }
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
}
