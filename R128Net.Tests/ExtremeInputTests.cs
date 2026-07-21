namespace R128Net.Tests;

public class ExtremeInputTests
{
    private const int SampleRate = 48000;
    private const int Channels = 2;
    private const int Seconds = 10;
    private const int ChunkFrames = 4801;

    private static readonly double[] Amplitudes =
    [
        1.0, 1e-20, 1e-40, 1e-310, 0.0, 1.0, 1e-45, 5e-324, 0.5, 1e-300
    ];

    private static double[] BuildInput()
    {
        int frames = SampleRate * Seconds;
        double[] input = new double[frames * Channels];
        uint seed = 2463534242u;

        for (int i = 0; i < frames; ++i)
        {
            double amplitude = Amplitudes[(i / SampleRate) % Amplitudes.Length];
            for (int c = 0; c < Channels; ++c)
            {
                seed ^= seed << 13;
                seed ^= seed >> 17;
                seed ^= seed << 5;
                input[(i * Channels) + c] =
                    (((seed / 4294967296.0) * 2.0) - 1.0) * amplitude;
            }
        }

        return input;
    }

    private static LoudnessMeter Run()
    {
        double[] input = BuildInput();

        ReferenceArray head = ReferenceData.Load("extreme_head");
        for (int i = 0; i < head.Values.Length; ++i)
        {
            BitwiseAssert.Equal(head.Values[i], input[i], $"extreme input sample {i}");
        }

        LoudnessMeter meter = new(Channels, SampleRate,
            LoudnessModes.Integrated | LoudnessModes.LoudnessRange | LoudnessModes.TruePeak);

        int totalFrames = SampleRate * Seconds;
        for (int offset = 0; offset < totalFrames; offset += ChunkFrames)
        {
            int take = Math.Min(ChunkFrames, totalFrames - offset);
            meter.AddFrames(input.AsSpan(offset * Channels, take * Channels));
        }

        return meter;
    }

    [Fact]
    public void ResultsMatchTheReferenceExactly()
    {
        using LoudnessMeter meter = Run();
        double[] expected = ReferenceData.Load("extreme_results").Values;

        BitwiseAssert.Equal(expected[0], meter.IntegratedLoudness, "extreme integrated");
        BitwiseAssert.Equal(expected[1], meter.MomentaryLoudness, "extreme momentary");
        BitwiseAssert.Equal(expected[2], meter.ShortTermLoudness, "extreme short term");
        BitwiseAssert.Equal(expected[3], meter.LoudnessRange, "extreme loudness range");
        BitwiseAssert.Equal(expected[4], meter.RelativeThreshold, "extreme relative threshold");
        BitwiseAssert.Equal(expected[5], meter.GetLoudnessOverWindow(400), "extreme window 400");
    }

    [Fact]
    public void BlockEnergiesMatchTheReferenceExactly()
    {
        using LoudnessMeter meter = Run();

        double[] blocks = ReferenceData.Load("extreme_blocks").Values;
        Assert.Equal((nuint)blocks.Length, meter.BlockCount);
        for (nuint i = 0; i < meter.BlockCount; ++i)
        {
            BitwiseAssert.Equal(blocks[i], meter.GetBlockEnergy(i), $"extreme block {i}");
        }

        double[] shortTerm = ReferenceData.Load("extreme_shortterm").Values;
        Assert.Equal((nuint)shortTerm.Length, meter.ShortTermBlockCount);
        for (nuint i = 0; i < meter.ShortTermBlockCount; ++i)
        {
            BitwiseAssert.Equal(shortTerm[i], meter.GetShortTermBlockEnergy(i),
                $"extreme short term block {i}");
        }
    }

    [Fact]
    public void PeaksMatchTheReferenceExactly()
    {
        using LoudnessMeter meter = Run();
        ReferenceArray expected = ReferenceData.Load("extreme_peaks");

        for (int c = 0; c < Channels; ++c)
        {
            BitwiseAssert.Equal(expected.Values[(c * 4) + 0], meter.GetSamplePeak(c),
                $"extreme channel {c} sample peak");
            BitwiseAssert.Equal(expected.Values[(c * 4) + 1], meter.GetTruePeak(c),
                $"extreme channel {c} true peak");
            BitwiseAssert.Equal(expected.Values[(c * 4) + 2], meter.GetPreviousSamplePeak(c),
                $"extreme channel {c} previous sample peak");
            BitwiseAssert.Equal(expected.Values[(c * 4) + 3], meter.GetPreviousTruePeak(c),
                $"extreme channel {c} previous true peak");
        }
    }

    [Fact]
    public void EmptyBufferIsAcceptedAndChangesNothing()
    {
        using LoudnessMeter meter = new(Channels, SampleRate, LoudnessModes.All);
        double[] chunk = BuildInput();

        meter.AddFrames(chunk.AsSpan(0, SampleRate * Channels));
        double before = meter.IntegratedLoudness;

        meter.AddFrames(ReadOnlySpan<double>.Empty);

        BitwiseAssert.Equal(before, meter.IntegratedLoudness, "integrated after empty add");
    }

    [Fact]
    public void PartialFrameIsRejected()
    {
        using LoudnessMeter meter = new(Channels, SampleRate, LoudnessModes.All);
        double[] odd = new double[Channels + 1];

        Assert.Throws<ArgumentException>(() => meter.AddFrames(odd.AsSpan()));
    }

    [Fact]
    public void SingleFrameChunksAgreeWithBulkFeeding()
    {
        double[] input = BuildInput();
        int frames = SampleRate * 2;

        using LoudnessMeter bulk = new(Channels, SampleRate, LoudnessModes.All);
        bulk.AddFrames(input.AsSpan(0, frames * Channels));

        using LoudnessMeter drip = new(Channels, SampleRate, LoudnessModes.All);
        for (int i = 0; i < frames; ++i)
        {
            drip.AddFrames(input.AsSpan(i * Channels, Channels));
        }

        BitwiseAssert.Equal(bulk.IntegratedLoudness, drip.IntegratedLoudness, "integrated");
        BitwiseAssert.Equal(bulk.MomentaryLoudness, drip.MomentaryLoudness, "momentary");
        BitwiseAssert.Equal(bulk.GetSamplePeak(0), drip.GetSamplePeak(0), "sample peak");
        BitwiseAssert.Equal(bulk.GetTruePeak(0), drip.GetTruePeak(0), "true peak");
    }

    [Fact]
    public void CompleteSilenceReportsNegativeInfinityAndZeroRange()
    {
        using LoudnessMeter meter = new(Channels, SampleRate, LoudnessModes.All);
        double[] silence = new double[SampleRate * Channels];

        for (int i = 0; i < 10; ++i)
        {
            meter.AddFrames(silence);
        }

        Assert.Equal(double.NegativeInfinity, meter.IntegratedLoudness);
        Assert.Equal(double.NegativeInfinity, meter.MomentaryLoudness);
        Assert.Equal(double.NegativeInfinity, meter.ShortTermLoudness);
        Assert.Equal(0.0, meter.LoudnessRange);
        Assert.Equal(-70.0, meter.RelativeThreshold);
        Assert.Equal(0.0, meter.GetSamplePeak(0));
        Assert.Equal(0.0, meter.GetTruePeak(0));
    }

    [Fact]
    public void DivergentSampleRatesAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LoudnessMeter(2, 1682, LoudnessModes.All));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LoudnessMeter(2, 100, LoudnessModes.All));
    }

    [Fact]
    public void ChannelAndSampleRateBoundsAreEnforced()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LoudnessMeter(0, 48000, LoudnessModes.All));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LoudnessMeter(65, 48000, LoudnessModes.All));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LoudnessMeter(2, 15, LoudnessModes.All));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LoudnessMeter(2, 2822401, LoudnessModes.All));
    }

    [Fact]
    public void DualMonoOnlyAppliesToMono()
    {
        using LoudnessMeter stereo = new(2, SampleRate, LoudnessModes.All);
        Assert.Throws<ArgumentException>(
            () => stereo.SetChannel(0, ChannelPosition.DualMono));

        using LoudnessMeter mono = new(1, SampleRate, LoudnessModes.All);
        mono.SetChannel(0, ChannelPosition.DualMono);
        Assert.Equal(ChannelPosition.DualMono, mono.GetChannel(0));
    }
}
