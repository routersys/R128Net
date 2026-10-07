using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace R128Net;

internal static unsafe class KWeightingFilter
{
    public const int StateTaps = 4;

    private const long MagnitudeMask = 0x7FFFFFFFFFFFFFFFL;
    internal const long SettledBits = 0x07B0000000000000L;
    internal const double SmallestCoefficient = 8.67361737988404E-19;

    public static void Process<TFormat, TSample>(
        TSample* source,
        double* destination,
        double* state,
        ChannelPosition* channelMap,
        int channels,
        int frames,
        in KWeighting weighting)
        where TFormat : struct, ISampleFormat<TSample>
        where TSample : unmanaged
    {
        bool plain = AllowsPlainArithmetic(weighting);
        int channel = 0;

        if (Vector256.IsHardwareAccelerated)
        {
            for (; channel + 4 <= channels; channel += 4)
            {
                if (!IsGroupUsable(channelMap, channel, 4))
                {
                    break;
                }

                ProcessGroup256<TFormat, TSample>(
                    source, destination, state, channels, frames, weighting, channel, plain);
            }
        }

        if (Vector128.IsHardwareAccelerated)
        {
            for (; channel + 2 <= channels; channel += 2)
            {
                if (!IsGroupUsable(channelMap, channel, 2))
                {
                    break;
                }

                ProcessGroup128<TFormat, TSample>(
                    source, destination, state, channels, frames, weighting, channel, plain);
            }
        }

        for (; channel < channels; ++channel)
        {
            if (channelMap[channel] == ChannelPosition.Unused)
            {
                continue;
            }

            ProcessChannel<TFormat, TSample>(
                source, destination, state, channels, frames, weighting, channel, plain);
        }
    }

    public static void ProcessScalar<TFormat, TSample>(
        TSample* source,
        double* destination,
        double* state,
        ChannelPosition* channelMap,
        int channels,
        int frames,
        in KWeighting weighting)
        where TFormat : struct, ISampleFormat<TSample>
        where TSample : unmanaged
    {
        bool plain = AllowsPlainArithmetic(weighting);

        for (int channel = 0; channel < channels; ++channel)
        {
            if (channelMap[channel] == ChannelPosition.Unused)
            {
                continue;
            }

            ProcessChannel<TFormat, TSample>(
                source, destination, state, channels, frames, weighting, channel, plain);
        }
    }

    private static bool IsGroupUsable(ChannelPosition* channelMap, int start, int width)
    {
        for (int i = 0; i < width; ++i)
        {
            if (channelMap[start + i] == ChannelPosition.Unused)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsUsableCoefficient(double coefficient)
    {
        return coefficient == 0.0
            || (double.IsFinite(coefficient) && Math.Abs(coefficient) >= SmallestCoefficient);
    }

    private static bool AllowsPlainArithmetic(in KWeighting weighting)
    {
        for (int i = 0; i < FilterTaps.Length; ++i)
        {
            if (!IsUsableCoefficient(weighting.Numerator[i])
                || !IsUsableCoefficient(weighting.Denominator[i]))
            {
                return false;
            }
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Settled(double value)
    {
        long magnitude = BitConverter.DoubleToInt64Bits(value) & MagnitudeMask;
        return (ulong)(magnitude - 1) >= (ulong)(SettledBits - 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<long> Unsettled(Vector128<double> value)
    {
        Vector128<long> magnitude = value.AsInt64() & Vector128.Create(MagnitudeMask);
        return Vector128.GreaterThan(magnitude, Vector128<long>.Zero)
            & Vector128.LessThan(magnitude, Vector128.Create(SettledBits));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<long> Unsettled(Vector256<double> value)
    {
        Vector256<long> magnitude = value.AsInt64() & Vector256.Create(MagnitudeMask);
        return Vector256.GreaterThan(magnitude, Vector256<long>.Zero)
            & Vector256.LessThan(magnitude, Vector256.Create(SettledBits));
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void ProcessChannel<TFormat, TSample>(
        TSample* source,
        double* destination,
        double* state,
        int channels,
        int frames,
        in KWeighting weighting,
        int channel,
        bool plain)
        where TFormat : struct, ISampleFormat<TSample>
        where TSample : unmanaged
    {
        double a1 = weighting.Denominator[1];
        double a2 = weighting.Denominator[2];
        double a3 = weighting.Denominator[3];
        double a4 = weighting.Denominator[4];
        double b0 = weighting.Numerator[0];
        double b1 = weighting.Numerator[1];
        double b2 = weighting.Numerator[2];
        double b3 = weighting.Numerator[3];
        double b4 = weighting.Numerator[4];

        double s1 = state[channel];
        double s2 = state[channels + channel];
        double s3 = state[(2 * channels) + channel];
        double s4 = state[(3 * channels) + channel];

        int i = 0;

        while (i < frames)
        {
            if (plain && Settled(s1) && Settled(s2) && Settled(s3) && Settled(s4))
            {
                for (; i < frames; ++i)
                {
                    int index = (i * channels) + channel;
                    double x = TFormat.ToUnit(source[index]);

                    double v0 = x - (a1 * s1);
                    v0 -= a2 * s2;
                    v0 -= a3 * s3;
                    v0 -= a4 * s4;

                    if (!(Settled(x) & Settled(v0)))
                    {
                        break;
                    }

                    double fast = (b0 * v0) + (b1 * s1);
                    fast += b2 * s2;
                    fast += b3 * s3;
                    fast += b4 * s4;

                    destination[index] = fast;

                    s4 = s3;
                    s3 = s2;
                    s2 = s1;
                    s1 = v0;
                }

                if (i == frames)
                {
                    break;
                }
            }

            int offset = (i * channels) + channel;
            double input = Denormal.Flush(TFormat.ToUnit(source[offset]));

            double accumulator = Denormal.Flush(input - Denormal.Flush(a1 * s1));
            accumulator = Denormal.Flush(accumulator - Denormal.Flush(a2 * s2));
            accumulator = Denormal.Flush(accumulator - Denormal.Flush(a3 * s3));
            double w0 = Denormal.Flush(accumulator - Denormal.Flush(a4 * s4));

            double output = Denormal.Flush(
                Denormal.Flush(b0 * w0) + Denormal.Flush(b1 * s1));
            output = Denormal.Flush(output + Denormal.Flush(b2 * s2));
            output = Denormal.Flush(output + Denormal.Flush(b3 * s3));
            output = Denormal.Flush(output + Denormal.Flush(b4 * s4));

            destination[offset] = output;

            s4 = s3;
            s3 = s2;
            s2 = s1;
            s1 = w0;
            ++i;
        }

        state[channel] = s1;
        state[channels + channel] = s2;
        state[(2 * channels) + channel] = s3;
        state[(3 * channels) + channel] = s4;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<double> Load128<TFormat, TSample>(TSample* source)
        where TFormat : struct, ISampleFormat<TSample>
        where TSample : unmanaged
    {
        if (typeof(TSample) == typeof(double))
        {
            return Vector128.Load((double*)source);
        }

        return Vector128.Create(TFormat.ToUnit(source[0]), TFormat.ToUnit(source[1]));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<double> Load256<TFormat, TSample>(TSample* source)
        where TFormat : struct, ISampleFormat<TSample>
        where TSample : unmanaged
    {
        if (typeof(TSample) == typeof(double))
        {
            return Vector256.Load((double*)source);
        }

        return Vector256.Create(
            TFormat.ToUnit(source[0]), TFormat.ToUnit(source[1]),
            TFormat.ToUnit(source[2]), TFormat.ToUnit(source[3]));
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void ProcessGroup128<TFormat, TSample>(
        TSample* source,
        double* destination,
        double* state,
        int channels,
        int frames,
        in KWeighting weighting,
        int channel,
        bool plain)
        where TFormat : struct, ISampleFormat<TSample>
        where TSample : unmanaged
    {
        Vector128<double> a1 = Vector128.Create(weighting.Denominator[1]);
        Vector128<double> a2 = Vector128.Create(weighting.Denominator[2]);
        Vector128<double> a3 = Vector128.Create(weighting.Denominator[3]);
        Vector128<double> a4 = Vector128.Create(weighting.Denominator[4]);
        Vector128<double> b0 = Vector128.Create(weighting.Numerator[0]);
        Vector128<double> b1 = Vector128.Create(weighting.Numerator[1]);
        Vector128<double> b2 = Vector128.Create(weighting.Numerator[2]);
        Vector128<double> b3 = Vector128.Create(weighting.Numerator[3]);
        Vector128<double> b4 = Vector128.Create(weighting.Numerator[4]);

        Vector128<double> s1 = Vector128.Load(state + channel);
        Vector128<double> s2 = Vector128.Load(state + channels + channel);
        Vector128<double> s3 = Vector128.Load(state + (2 * channels) + channel);
        Vector128<double> s4 = Vector128.Load(state + (3 * channels) + channel);

        int i = 0;

        while (i < frames)
        {
            if (plain && (Unsettled(s1) | Unsettled(s2) | Unsettled(s3) | Unsettled(s4))
                == Vector128<long>.Zero)
            {
                for (; i < frames; ++i)
                {
                    int index = (i * channels) + channel;
                    Vector128<double> x = Load128<TFormat, TSample>(source + index);

                    Vector128<double> v0 = x - (a1 * s1);
                    v0 -= a2 * s2;
                    v0 -= a3 * s3;
                    v0 -= a4 * s4;

                    if ((Unsettled(x) | Unsettled(v0)) != Vector128<long>.Zero)
                    {
                        break;
                    }

                    Vector128<double> fast = (b0 * v0) + (b1 * s1);
                    fast += b2 * s2;
                    fast += b3 * s3;
                    fast += b4 * s4;

                    fast.Store(destination + index);

                    s4 = s3;
                    s3 = s2;
                    s2 = s1;
                    s1 = v0;
                }

                if (i == frames)
                {
                    break;
                }
            }

            int offset = (i * channels) + channel;

            Vector128<double> input = Denormal.Flush(
                Load128<TFormat, TSample>(source + offset));

            Vector128<double> accumulator = Denormal.Flush(input - Denormal.Flush(a1 * s1));
            accumulator = Denormal.Flush(accumulator - Denormal.Flush(a2 * s2));
            accumulator = Denormal.Flush(accumulator - Denormal.Flush(a3 * s3));
            Vector128<double> w0 = Denormal.Flush(accumulator - Denormal.Flush(a4 * s4));

            Vector128<double> output = Denormal.Flush(
                Denormal.Flush(b0 * w0) + Denormal.Flush(b1 * s1));
            output = Denormal.Flush(output + Denormal.Flush(b2 * s2));
            output = Denormal.Flush(output + Denormal.Flush(b3 * s3));
            output = Denormal.Flush(output + Denormal.Flush(b4 * s4));

            output.Store(destination + offset);

            s4 = s3;
            s3 = s2;
            s2 = s1;
            s1 = w0;
            ++i;
        }

        s1.Store(state + channel);
        s2.Store(state + channels + channel);
        s3.Store(state + (2 * channels) + channel);
        s4.Store(state + (3 * channels) + channel);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void ProcessGroup256<TFormat, TSample>(
        TSample* source,
        double* destination,
        double* state,
        int channels,
        int frames,
        in KWeighting weighting,
        int channel,
        bool plain)
        where TFormat : struct, ISampleFormat<TSample>
        where TSample : unmanaged
    {
        Vector256<double> a1 = Vector256.Create(weighting.Denominator[1]);
        Vector256<double> a2 = Vector256.Create(weighting.Denominator[2]);
        Vector256<double> a3 = Vector256.Create(weighting.Denominator[3]);
        Vector256<double> a4 = Vector256.Create(weighting.Denominator[4]);
        Vector256<double> b0 = Vector256.Create(weighting.Numerator[0]);
        Vector256<double> b1 = Vector256.Create(weighting.Numerator[1]);
        Vector256<double> b2 = Vector256.Create(weighting.Numerator[2]);
        Vector256<double> b3 = Vector256.Create(weighting.Numerator[3]);
        Vector256<double> b4 = Vector256.Create(weighting.Numerator[4]);

        Vector256<double> s1 = Vector256.Load(state + channel);
        Vector256<double> s2 = Vector256.Load(state + channels + channel);
        Vector256<double> s3 = Vector256.Load(state + (2 * channels) + channel);
        Vector256<double> s4 = Vector256.Load(state + (3 * channels) + channel);

        int i = 0;

        while (i < frames)
        {
            if (plain && (Unsettled(s1) | Unsettled(s2) | Unsettled(s3) | Unsettled(s4))
                == Vector256<long>.Zero)
            {
                for (; i < frames; ++i)
                {
                    int index = (i * channels) + channel;
                    Vector256<double> x = Load256<TFormat, TSample>(source + index);

                    Vector256<double> v0 = x - (a1 * s1);
                    v0 -= a2 * s2;
                    v0 -= a3 * s3;
                    v0 -= a4 * s4;

                    if ((Unsettled(x) | Unsettled(v0)) != Vector256<long>.Zero)
                    {
                        break;
                    }

                    Vector256<double> fast = (b0 * v0) + (b1 * s1);
                    fast += b2 * s2;
                    fast += b3 * s3;
                    fast += b4 * s4;

                    fast.Store(destination + index);

                    s4 = s3;
                    s3 = s2;
                    s2 = s1;
                    s1 = v0;
                }

                if (i == frames)
                {
                    break;
                }
            }

            int offset = (i * channels) + channel;

            Vector256<double> input = Denormal.Flush(
                Load256<TFormat, TSample>(source + offset));

            Vector256<double> accumulator = Denormal.Flush(input - Denormal.Flush(a1 * s1));
            accumulator = Denormal.Flush(accumulator - Denormal.Flush(a2 * s2));
            accumulator = Denormal.Flush(accumulator - Denormal.Flush(a3 * s3));
            Vector256<double> w0 = Denormal.Flush(accumulator - Denormal.Flush(a4 * s4));

            Vector256<double> output = Denormal.Flush(
                Denormal.Flush(b0 * w0) + Denormal.Flush(b1 * s1));
            output = Denormal.Flush(output + Denormal.Flush(b2 * s2));
            output = Denormal.Flush(output + Denormal.Flush(b3 * s3));
            output = Denormal.Flush(output + Denormal.Flush(b4 * s4));

            output.Store(destination + offset);

            s4 = s3;
            s3 = s2;
            s2 = s1;
            s1 = w0;
            ++i;
        }

        s1.Store(state + channel);
        s2.Store(state + channels + channel);
        s3.Store(state + (2 * channels) + channel);
        s4.Store(state + (3 * channels) + channel);
    }
}
