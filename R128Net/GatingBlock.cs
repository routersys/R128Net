using System.Runtime.Intrinsics;

namespace R128Net;

internal static unsafe class GatingBlock
{
    public static double ChannelWeight(ChannelPosition position)
    {
        switch (position)
        {
            case ChannelPosition.Mp110:
            case ChannelPosition.Mm110:
            case ChannelPosition.Mp060:
            case ChannelPosition.Mm060:
            case ChannelPosition.Mp090:
            case ChannelPosition.Mm090:
                return 1.41;
            case ChannelPosition.DualMono:
                return 2.0;
            default:
                return 1.0;
        }
    }

    public static double Energy(
        double* audioData,
        ChannelPosition* channelMap,
        int channels,
        nuint audioDataFrames,
        nuint audioDataIndex,
        nuint framesPerBlock)
    {
        nuint stride = (nuint)channels;
        nuint filled = audioDataIndex / stride;
        bool wrapped = audioDataIndex < framesPerBlock * stride;

        nuint firstStart = wrapped ? 0 : filled - framesPerBlock;
        nuint firstEnd = filled;
        nuint secondStart = wrapped ? audioDataFrames - (framesPerBlock - filled) : 0;
        nuint secondEnd = wrapped ? audioDataFrames : 0;

        double* sums = stackalloc double[channels];

        int channel = 0;

        if (Vector256.IsHardwareAccelerated)
        {
            for (; channel + 4 <= channels; channel += 4)
            {
                double* data = audioData + channel;
                Vector256<double> lanes = Vector256<double>.Zero;
                lanes = Accumulate256(data, firstStart, firstEnd, stride, lanes);
                lanes = Accumulate256(data, secondStart, secondEnd, stride, lanes);
                lanes.Store(sums + channel);
            }
        }

        if (Vector128.IsHardwareAccelerated)
        {
            for (; channel + 2 <= channels; channel += 2)
            {
                double* data = audioData + channel;
                Vector128<double> lanes = Vector128<double>.Zero;
                lanes = Accumulate128(data, firstStart, firstEnd, stride, lanes);
                lanes = Accumulate128(data, secondStart, secondEnd, stride, lanes);
                lanes.Store(sums + channel);
            }
        }

        for (; channel < channels; ++channel)
        {
            double* data = audioData + channel;
            double channelSum = 0.0;
            channelSum = AccumulateScalar(data, firstStart, firstEnd, stride, channelSum);
            channelSum = AccumulateScalar(data, secondStart, secondEnd, stride, channelSum);
            sums[channel] = channelSum;
        }

        double sum = 0.0;

        for (channel = 0; channel < channels; ++channel)
        {
            ChannelPosition position = channelMap[channel];
            if (position == ChannelPosition.Unused)
            {
                continue;
            }

            double channelSum = sums[channel];
            channelSum *= ChannelWeight(position);

            sum += channelSum;
        }

        return sum / (double)framesPerBlock;
    }

    private static Vector256<double> Accumulate256(
        double* data, nuint start, nuint end, nuint stride, Vector256<double> lanes)
    {
        double* cursor = data + (start * stride);

        for (nuint i = start; i < end; ++i)
        {
            Vector256<double> value = Vector256.Load(cursor);
            lanes += value * value;
            cursor += stride;
        }

        return lanes;
    }

    private static Vector128<double> Accumulate128(
        double* data, nuint start, nuint end, nuint stride, Vector128<double> lanes)
    {
        double* cursor = data + (start * stride);

        for (nuint i = start; i < end; ++i)
        {
            Vector128<double> value = Vector128.Load(cursor);
            lanes += value * value;
            cursor += stride;
        }

        return lanes;
    }

    private static double AccumulateScalar(
        double* data, nuint start, nuint end, nuint stride, double sum)
    {
        double* cursor = data + (start * stride);

        for (nuint i = start; i < end; ++i)
        {
            double value = *cursor;
            sum += value * value;
            cursor += stride;
        }

        return sum;
    }
}
