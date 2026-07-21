namespace R128Net.Tests;

public class LoudnessPipelineTests
{
    private static readonly double[] Amplitudes =
    [
        1.0, 0.5, 0.25, 0.1, 0.03, 0.008, 0.0, 0.7, 0.002, 0.35
    ];

    private static double[] BuildInput(int channels, int sampleRate, int seconds)
    {
        int frames = sampleRate * seconds;
        double[] input = new double[frames * channels];
        uint seed = 2463534242u;

        for (int i = 0; i < frames; ++i)
        {
            double amplitude = Amplitudes[(i / sampleRate) % Amplitudes.Length];
            for (int c = 0; c < channels; ++c)
            {
                seed ^= seed << 13;
                seed ^= seed >> 17;
                seed ^= seed << 5;
                input[(i * channels) + c] =
                    (((seed / 4294967296.0) * 2.0) - 1.0) * amplitude;
            }
        }

        return input;
    }

    private static LoudnessMeter Run(string suffix, int channels, int sampleRate,
        int seconds, bool histogram, ChannelPosition[]? map, int chunkFrames)
    {
        double[] input = BuildInput(channels, sampleRate, seconds);

        ReferenceArray head = ReferenceData.Load($"loudness_head_{suffix}");
        Assert.Equal(channels, head.Columns);
        for (int i = 0; i < head.Values.Length; ++i)
        {
            BitwiseAssert.Equal(head.Values[i], input[i], $"{suffix} input sample {i}");
        }

        LoudnessModes modes = LoudnessModes.Integrated
            | LoudnessModes.LoudnessRange
            | LoudnessModes.TruePeak;
        if (histogram)
        {
            modes |= LoudnessModes.Histogram;
        }

        LoudnessMeter meter = new(channels, sampleRate, modes);

        if (map is not null)
        {
            for (int c = 0; c < channels; ++c)
            {
                meter.SetChannel(c, map[c]);
            }
        }

        int totalFrames = sampleRate * seconds;
        for (int offset = 0; offset < totalFrames; offset += chunkFrames)
        {
            int take = Math.Min(chunkFrames, totalFrames - offset);
            meter.AddFrames(input.AsSpan(offset * channels, take * channels));
        }

        return meter;
    }

    public static TheoryData<string, int, int, int, bool, int> Cases => new()
    {
        { "stereo", 2, 48000, 20, false, 4801 },
        { "surround", 5, 48000, 20, false, 4801 },
        { "dualmono", 1, 48000, 20, false, 4801 },
        { "histogram", 2, 48000, 20, true, 4801 },
        { "aligned", 2, 48000, 20, false, 4800 },
        { "rate44100", 2, 44100, 20, false, 4801 },
        { "rate96000", 2, 96000, 12, false, 4801 },
    };

    private static ChannelPosition[]? MapFor(string suffix)
    {
        return suffix switch
        {
            "surround" =>
            [
                ChannelPosition.Left, ChannelPosition.Right, ChannelPosition.Center,
                ChannelPosition.LeftSurround, ChannelPosition.RightSurround
            ],
            "dualmono" => [ChannelPosition.DualMono],
            _ => null,
        };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void StateGeometryMatchesTheReference(string suffix, int channels,
        int sampleRate, int seconds, bool histogram, int chunkFrames)
    {
        using LoudnessMeter meter = Run(
            suffix, channels, sampleRate, seconds, histogram, MapFor(suffix), chunkFrames);

        double[] expected = ReferenceData.Load($"loudness_geometry_{suffix}").Values;

        Assert.Equal((nuint)expected[0], meter.AudioDataIndex);
        Assert.Equal((nuint)expected[1], meter.AudioDataFrames);
        Assert.Equal((nuint)expected[2], meter.NeededFrames);
        Assert.Equal((nuint)expected[3], meter.ShortTermFrameCounter);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void BlockEnergiesMatchTheReferenceExactly(string suffix, int channels,
        int sampleRate, int seconds, bool histogram, int chunkFrames)
    {
        using LoudnessMeter meter = Run(
            suffix, channels, sampleRate, seconds, histogram, MapFor(suffix), chunkFrames);

        double[] expected = ReferenceData.Load($"loudness_blocks_{suffix}").Values;
        Assert.Equal((nuint)expected.Length, meter.BlockCount);

        for (nuint i = 0; i < meter.BlockCount; ++i)
        {
            BitwiseAssert.Equal(expected[i], meter.GetBlockEnergy(i),
                $"{suffix} gating block {i}");
        }

        double[] shortTerm = ReferenceData.Load($"loudness_shortterm_{suffix}").Values;
        Assert.Equal((nuint)shortTerm.Length, meter.ShortTermBlockCount);

        for (nuint i = 0; i < meter.ShortTermBlockCount; ++i)
        {
            BitwiseAssert.Equal(shortTerm[i], meter.GetShortTermBlockEnergy(i),
                $"{suffix} short term block {i}");
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void LoudnessResultsMatchTheReferenceExactly(string suffix, int channels,
        int sampleRate, int seconds, bool histogram, int chunkFrames)
    {
        using LoudnessMeter meter = Run(
            suffix, channels, sampleRate, seconds, histogram, MapFor(suffix), chunkFrames);

        double[] expected = ReferenceData.Load($"loudness_results_{suffix}").Values;

        BitwiseAssert.Equal(expected[0], meter.IntegratedLoudness, $"{suffix} integrated");
        BitwiseAssert.Equal(expected[1], meter.MomentaryLoudness, $"{suffix} momentary");
        BitwiseAssert.Equal(expected[2], meter.ShortTermLoudness, $"{suffix} short term");
        BitwiseAssert.Equal(expected[3], meter.LoudnessRange, $"{suffix} loudness range");
        BitwiseAssert.Equal(expected[4], meter.RelativeThreshold, $"{suffix} relative threshold");
        BitwiseAssert.Equal(expected[5], meter.GetLoudnessOverWindow(400), $"{suffix} window 400");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void PeaksMatchTheReferenceExactly(string suffix, int channels,
        int sampleRate, int seconds, bool histogram, int chunkFrames)
    {
        using LoudnessMeter meter = Run(
            suffix, channels, sampleRate, seconds, histogram, MapFor(suffix), chunkFrames);

        ReferenceArray expected = ReferenceData.Load($"loudness_peaks_{suffix}");
        Assert.Equal(channels, expected.Rows);

        for (int c = 0; c < channels; ++c)
        {
            BitwiseAssert.Equal(expected.Values[(c * 4) + 0], meter.GetSamplePeak(c),
                $"{suffix} channel {c} sample peak");
            BitwiseAssert.Equal(expected.Values[(c * 4) + 1], meter.GetTruePeak(c),
                $"{suffix} channel {c} true peak");
            BitwiseAssert.Equal(expected.Values[(c * 4) + 2], meter.GetPreviousSamplePeak(c),
                $"{suffix} channel {c} previous sample peak");
            BitwiseAssert.Equal(expected.Values[(c * 4) + 3], meter.GetPreviousTruePeak(c),
                $"{suffix} channel {c} previous true peak");
        }
    }

    [Fact]
    public void HistogramBinsMatchTheReferenceExactly()
    {
        using LoudnessMeter meter = Run("histogram", 2, 48000, 20, true, null, 4801);

        ReferenceArray expected = ReferenceData.Load("loudness_histogram_histogram");
        Assert.Equal(HistogramTables.BinCount, expected.Rows);

        for (int i = 0; i < HistogramTables.BinCount; ++i)
        {
            Assert.Equal((ulong)expected.Values[(i * 2) + 0], meter.GetBlockHistogramBin(i));
            Assert.Equal((ulong)expected.Values[(i * 2) + 1], meter.GetShortTermHistogramBin(i));
        }
    }
}
