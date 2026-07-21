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
        double sum = 0.0;

        for (int channel = 0; channel < channels; ++channel)
        {
            ChannelPosition position = channelMap[channel];
            if (position == ChannelPosition.Unused)
            {
                continue;
            }

            double channelSum = 0.0;
            nuint filled = audioDataIndex / (nuint)channels;

            if (audioDataIndex < framesPerBlock * (nuint)channels)
            {
                for (nuint i = 0; i < filled; ++i)
                {
                    double value = audioData[(i * (nuint)channels) + (nuint)channel];
                    channelSum += value * value;
                }

                for (nuint i = audioDataFrames - (framesPerBlock - filled);
                    i < audioDataFrames; ++i)
                {
                    double value = audioData[(i * (nuint)channels) + (nuint)channel];
                    channelSum += value * value;
                }
            }
            else
            {
                for (nuint i = filled - framesPerBlock; i < filled; ++i)
                {
                    double value = audioData[(i * (nuint)channels) + (nuint)channel];
                    channelSum += value * value;
                }
            }

            channelSum *= ChannelWeight(position);

            sum += channelSum;
        }

        return sum / (double)framesPerBlock;
    }
}
