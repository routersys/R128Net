namespace R128Net.Tests;

public class GatingBlockTests
{
    private static unsafe double ScalarEnergy(
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

            channelSum *= GatingBlock.ChannelWeight(position);

            sum += channelSum;
        }

        return sum / (double)framesPerBlock;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(16)]
    public unsafe void EnergyMatchesTheScalarSumBitForBit(int channels)
    {
        const int Frames = 3001;

        double[] data = new double[Frames * channels];
        uint seed = 123456789u + (uint)channels;
        for (int i = 0; i < data.Length; ++i)
        {
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            double unit = ((seed / 4294967296.0) * 2.0) - 1.0;
            data[i] = (seed & 31u) == 0u ? unit * 1e-200 : unit * Math.Pow(10.0, (seed >> 8) % 7u);
        }

        ChannelPosition[] positions =
        [
            ChannelPosition.Left, ChannelPosition.Right, ChannelPosition.Center,
            ChannelPosition.Unused, ChannelPosition.Mp110, ChannelPosition.Mm110,
            ChannelPosition.DualMono, ChannelPosition.Mp060, ChannelPosition.Mm090,
        ];

        nuint[] blockLengths = [1, 2, 7, 480, 1999, 3001];

        fixed (double* audio = data)
        {
            for (int unused = -1; unused < channels; ++unused)
            {
                ChannelPosition[] map = new ChannelPosition[channels];
                for (int c = 0; c < channels; ++c)
                {
                    map[c] = c == unused
                        ? ChannelPosition.Unused
                        : positions[c % positions.Length] == ChannelPosition.Unused
                            ? ChannelPosition.Left
                            : positions[c % positions.Length];
                }

                fixed (ChannelPosition* channelMap = map)
                {
                    foreach (nuint block in blockLengths)
                    {
                        for (nuint index = 0; index <= Frames; index += 137)
                        {
                            nuint audioIndex = index * (nuint)channels;
                            if (audioIndex == (nuint)Frames * (nuint)channels)
                            {
                                audioIndex = 0;
                            }

                            double expected = ScalarEnergy(audio, channelMap, channels,
                                Frames, audioIndex, block);
                            double actual = GatingBlock.Energy(audio, channelMap, channels,
                                Frames, audioIndex, block);

                            BitwiseAssert.Equal(expected, actual,
                                $"{channels} channels, unused {unused}, block {block}, "
                                + $"index {audioIndex}");
                        }
                    }
                }
            }
        }
    }
}
