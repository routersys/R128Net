namespace R128Net.Tests;

public class MeterReuseTests
{
    private const int SampleRate = 48000;
    private const int Channels = 2;
    private const int ChunkFrames = 4801;

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

    private static double[] BuildContaminant(int channels, int frames)
    {
        double[] input = new double[frames * channels];
        uint seed = 1103515245u;
        for (int i = 0; i < input.Length; ++i)
        {
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            input[i] = ((seed / 4294967296.0) * 2.0) - 1.0;
        }
        return input;
    }

    private static void Feed(LoudnessMeter meter, double[] input, int channels,
        int totalFrames, int chunkFrames)
    {
        for (int offset = 0; offset < totalFrames; offset += chunkFrames)
        {
            int take = Math.Min(chunkFrames, totalFrames - offset);
            meter.AddFrames(input.AsSpan(offset * channels, take * channels));
        }
    }

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
    [MemberData(nameof(LoudnessPipelineTests.Cases), MemberType = typeof(LoudnessPipelineTests))]
    public void ResettingRestoresTheStateOfAFreshMeter(string suffix, int channels,
        int sampleRate, int seconds, bool histogram, int chunkFrames)
    {
        LoudnessModes modes = LoudnessModes.Integrated
            | LoudnessModes.LoudnessRange
            | LoudnessModes.TruePeak;
        if (histogram)
        {
            modes |= LoudnessModes.Histogram;
        }

        using LoudnessMeter meter = new(channels, sampleRate, modes);

        ChannelPosition[]? map = MapFor(suffix);
        if (map is not null)
        {
            for (int c = 0; c < channels; ++c)
            {
                meter.SetChannel(c, map[c]);
            }
        }

        double[] contaminant = BuildContaminant(channels, sampleRate * 5);
        Feed(meter, contaminant, channels, sampleRate * 5, 997);

        meter.Reset();

        double[] input = BuildInput(channels, sampleRate, seconds);
        Feed(meter, input, channels, sampleRate * seconds, chunkFrames);

        double[] expected = ReferenceData.Load($"loudness_results_{suffix}").Values;
        BitwiseAssert.Equal(expected[0], meter.IntegratedLoudness, $"{suffix} integrated");
        BitwiseAssert.Equal(expected[1], meter.MomentaryLoudness, $"{suffix} momentary");
        BitwiseAssert.Equal(expected[2], meter.ShortTermLoudness, $"{suffix} short term");
        BitwiseAssert.Equal(expected[3], meter.LoudnessRange, $"{suffix} loudness range");
        BitwiseAssert.Equal(expected[4], meter.RelativeThreshold, $"{suffix} relative threshold");
        BitwiseAssert.Equal(expected[5], meter.GetLoudnessOverWindow(400), $"{suffix} window 400");

        ReferenceArray peaks = ReferenceData.Load($"loudness_peaks_{suffix}");
        for (int c = 0; c < channels; ++c)
        {
            BitwiseAssert.Equal(peaks.Values[(c * 4) + 0], meter.GetSamplePeak(c),
                $"{suffix} channel {c} sample peak");
            BitwiseAssert.Equal(peaks.Values[(c * 4) + 1], meter.GetTruePeak(c),
                $"{suffix} channel {c} true peak");
        }

        double[] blocks = ReferenceData.Load($"loudness_blocks_{suffix}").Values;
        Assert.Equal((nuint)blocks.Length, meter.BlockCount);
        for (nuint i = 0; i < meter.BlockCount; ++i)
        {
            BitwiseAssert.Equal(blocks[i], meter.GetBlockEnergy(i), $"{suffix} block {i}");
        }

        double[] geometry = ReferenceData.Load($"loudness_geometry_{suffix}").Values;
        Assert.Equal((nuint)geometry[0], meter.AudioDataIndex);
        Assert.Equal((nuint)geometry[2], meter.NeededFrames);
        Assert.Equal((nuint)geometry[3], meter.ShortTermFrameCounter);
    }

    [Fact]
    public void ResettingPreservesConfiguration()
    {
        LoudnessMeterOptions options = new()
        {
            MaxWindowMilliseconds = 10000,
            MaxHistoryMilliseconds = 60000,
        };

        using LoudnessMeter meter = new(5, SampleRate, LoudnessModes.All, options);
        meter.SetChannel(3, ChannelPosition.Unused);
        meter.SetChannel(4, ChannelPosition.Mp060);

        meter.AddFrames(BuildContaminant(5, SampleRate));
        meter.Reset();

        Assert.Equal(ChannelPosition.Unused, meter.GetChannel(3));
        Assert.Equal(ChannelPosition.Mp060, meter.GetChannel(4));
        Assert.Equal(10000L, meter.MaxWindowMilliseconds);
        Assert.Equal(60000L, meter.MaxHistoryMilliseconds);
        Assert.Equal(5, meter.Channels);
        Assert.Equal(SampleRate, meter.SampleRate);
    }

    [Fact]
    public void ResettingClearsEveryObservableQuantity()
    {
        using LoudnessMeter meter = new(Channels, SampleRate, LoudnessModes.All);

        meter.AddFrames(BuildContaminant(Channels, SampleRate * 4));
        Assert.NotEqual(0.0, meter.GetSamplePeak(0));

        meter.Reset();

        Assert.Equal(double.NegativeInfinity, meter.IntegratedLoudness);
        Assert.Equal(double.NegativeInfinity, meter.MomentaryLoudness);
        Assert.Equal(double.NegativeInfinity, meter.ShortTermLoudness);
        Assert.Equal(0.0, meter.LoudnessRange);
        Assert.Equal(-70.0, meter.RelativeThreshold);
        Assert.Equal(0.0, meter.FramesProcessed);

        for (int c = 0; c < Channels; ++c)
        {
            Assert.Equal(0.0, meter.GetSamplePeak(c));
            Assert.Equal(0.0, meter.GetTruePeak(c));
            Assert.Equal(0.0, meter.GetPreviousSamplePeak(c));
            Assert.Equal(0.0, meter.GetPreviousTruePeak(c));
        }

        Assert.Equal((nuint)0, meter.BlockCount);
        Assert.Equal((nuint)0, meter.ShortTermBlockCount);
        Assert.Equal((nuint)0, meter.AudioDataIndex);
        Assert.Equal((nuint)0, meter.ShortTermFrameCounter);
    }

    [Fact]
    public void ResettingClearsTheHistogramBins()
    {
        using LoudnessMeter meter = new(Channels, SampleRate,
            LoudnessModes.All | LoudnessModes.Histogram);

        meter.AddFrames(BuildContaminant(Channels, SampleRate * 4));

        bool populated = false;
        for (int i = 0; i < HistogramTables.BinCount; ++i)
        {
            populated |= meter.GetBlockHistogramBin(i) != 0;
        }
        Assert.True(populated);

        meter.Reset();

        for (int i = 0; i < HistogramTables.BinCount; ++i)
        {
            Assert.Equal(0UL, meter.GetBlockHistogramBin(i));
            Assert.Equal(0UL, meter.GetShortTermHistogramBin(i));
        }
    }

    [Fact]
    public void RepeatedCandidatesProduceIndependentResults()
    {
        double[] first = BuildInput(Channels, SampleRate, 6);
        double[] second = BuildContaminant(Channels, SampleRate * 6);

        double[] expected = new double[2];

        using (LoudnessMeter fresh = new(Channels, SampleRate, LoudnessModes.All))
        {
            Feed(fresh, first, Channels, SampleRate * 6, ChunkFrames);
            expected[0] = fresh.IntegratedLoudness;
        }

        using (LoudnessMeter fresh = new(Channels, SampleRate, LoudnessModes.All))
        {
            Feed(fresh, second, Channels, SampleRate * 6, ChunkFrames);
            expected[1] = fresh.IntegratedLoudness;
        }

        using LoudnessMeter reused = new(Channels, SampleRate, LoudnessModes.All);

        for (int round = 0; round < 3; ++round)
        {
            reused.Reset();
            Feed(reused, first, Channels, SampleRate * 6, ChunkFrames);
            BitwiseAssert.Equal(expected[0], reused.IntegratedLoudness,
                $"round {round} first candidate");

            reused.Reset();
            Feed(reused, second, Channels, SampleRate * 6, ChunkFrames);
            BitwiseAssert.Equal(expected[1], reused.IntegratedLoudness,
                $"round {round} second candidate");
        }
    }

    [Fact]
    public void ResettingDoesNotAllocateManagedMemory()
    {
        using LoudnessMeter meter = new(Channels, SampleRate, LoudnessModes.All);
        double[] chunk = BuildContaminant(Channels, ChunkFrames);

        for (int i = 0; i < 8; ++i)
        {
            meter.AddFrames(chunk);
            meter.Reset();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 8; ++i)
        {
            meter.AddFrames(chunk);
            meter.Reset();
        }
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0L, after - before);
    }

    [Fact]
    public void FramesProcessedCountsEveryAddedFrame()
    {
        using LoudnessMeter meter = new(Channels, SampleRate, LoudnessModes.Momentary);

        Assert.Equal(0L, meter.FramesProcessed);

        double[] chunk = BuildContaminant(Channels, 1000);
        meter.AddFrames(chunk);
        Assert.Equal(1000L, meter.FramesProcessed);

        meter.AddFrames(chunk.AsSpan(0, 7 * Channels));
        Assert.Equal(1007L, meter.FramesProcessed);

        meter.AddFrames(ReadOnlySpan<double>.Empty);
        Assert.Equal(1007L, meter.FramesProcessed);

        meter.Reset();
        Assert.Equal(0L, meter.FramesProcessed);
    }
}
