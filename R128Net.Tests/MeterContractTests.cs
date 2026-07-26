namespace R128Net.Tests;

public class MeterContractTests
{
    private const int SampleRate = 48000;
    private const int Channels = 2;

    private static double[] BuildChunk(int channels, int frames)
    {
        double[] chunk = new double[frames * channels];
        uint seed = 2463534242u;
        for (int i = 0; i < chunk.Length; ++i)
        {
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            chunk[i] = ((seed / 4294967296.0) * 2.0) - 1.0;
        }
        return chunk;
    }

    private static LoudnessMeter Measured(LoudnessModes modes)
    {
        LoudnessMeter meter = new(Channels, SampleRate, modes);
        meter.AddFrames(BuildChunk(Channels, SampleRate * 4));
        return meter;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryQueryRejectsADisposedMeter(bool histogram)
    {
        LoudnessModes modes = LoudnessModes.All;
        if (histogram)
        {
            modes |= LoudnessModes.Histogram;
        }

        LoudnessMeter meter = Measured(modes);
        meter.Dispose();

        Assert.Throws<ObjectDisposedException>(() => meter.MomentaryLoudness);
        Assert.Throws<ObjectDisposedException>(() => meter.ShortTermLoudness);
        Assert.Throws<ObjectDisposedException>(() => meter.IntegratedLoudness);
        Assert.Throws<ObjectDisposedException>(() => meter.LoudnessRange);
        Assert.Throws<ObjectDisposedException>(() => meter.RelativeThreshold);
        Assert.Throws<ObjectDisposedException>(() => meter.GetLoudnessOverWindow(400));
        Assert.Throws<ObjectDisposedException>(() => meter.GetSamplePeak(0));
        Assert.Throws<ObjectDisposedException>(() => meter.GetPreviousSamplePeak(0));
        Assert.Throws<ObjectDisposedException>(() => meter.GetTruePeak(0));
        Assert.Throws<ObjectDisposedException>(() => meter.GetPreviousTruePeak(0));
        Assert.Throws<ObjectDisposedException>(() => meter.GetChannel(0));
        Assert.Throws<ObjectDisposedException>(() => meter.SetChannel(0, ChannelPosition.Left));
        Assert.Throws<ObjectDisposedException>(() => meter.SetMaxHistory(4000));
        Assert.Throws<ObjectDisposedException>(meter.Reset);
        Assert.Throws<ObjectDisposedException>(
            () => meter.AddFrames(BuildChunk(Channels, 10)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AggregationRejectsADisposedMeter(bool histogram)
    {
        LoudnessModes modes = LoudnessModes.All;
        if (histogram)
        {
            modes |= LoudnessModes.Histogram;
        }

        using LoudnessMeter live = Measured(modes);
        LoudnessMeter dead = Measured(modes);
        dead.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => LoudnessMeter.GatedLoudness([live, dead]));
        Assert.Throws<ObjectDisposedException>(
            () => LoudnessMeter.LoudnessRangeOf([live, dead]));
        Assert.Throws<ObjectDisposedException>(
            () => LoudnessMeter.GatedLoudness([dead]));
        Assert.Throws<ObjectDisposedException>(
            () => LoudnessMeter.LoudnessRangeOf([dead]));
    }

    [Fact]
    public void DisposingTwiceIsHarmless()
    {
        LoudnessMeter meter = Measured(LoudnessModes.All);
        meter.Dispose();
        meter.Dispose();
        meter.Dispose();
    }

    [Fact]
    public void NullEntriesAreSkippedByAggregation()
    {
        using LoudnessMeter meter = Measured(LoudnessModes.All);

        BitwiseAssert.Equal(meter.IntegratedLoudness,
            LoudnessMeter.GatedLoudness([null, meter, null]), "integrated with null entries");
        BitwiseAssert.Equal(meter.LoudnessRange,
            LoudnessMeter.LoudnessRangeOf([null, meter, null]), "range with null entries");

        Assert.Equal(double.NegativeInfinity, LoudnessMeter.GatedLoudness([null, null]));
        Assert.Equal(0.0, LoudnessMeter.LoudnessRangeOf([null, null]));
    }

    [Fact]
    public void ShortTermRequiresItsMode()
    {
        LoudnessMeterOptions options = new() { MaxWindowMilliseconds = 5000 };
        using LoudnessMeter meter = new(Channels, SampleRate, LoudnessModes.Momentary, options);
        meter.AddFrames(BuildChunk(Channels, SampleRate * 4));

        Assert.Throws<InvalidOperationException>(() => meter.ShortTermLoudness);
    }

    [Fact]
    public void IntegratedAndRangeRequireTheirModes()
    {
        using LoudnessMeter momentary = new(Channels, SampleRate, LoudnessModes.Momentary);
        Assert.Throws<InvalidOperationException>(() => momentary.IntegratedLoudness);
        Assert.Throws<InvalidOperationException>(() => momentary.RelativeThreshold);
        Assert.Throws<InvalidOperationException>(() => momentary.LoudnessRange);

        using LoudnessMeter integrated = new(Channels, SampleRate, LoudnessModes.Integrated);
        Assert.Throws<InvalidOperationException>(() => integrated.LoudnessRange);
    }

    [Fact]
    public void PeakQueriesRequireTheirModes()
    {
        using LoudnessMeter plain = new(Channels, SampleRate, LoudnessModes.Integrated);
        Assert.Throws<InvalidOperationException>(() => plain.GetSamplePeak(0));
        Assert.Throws<InvalidOperationException>(() => plain.GetPreviousSamplePeak(0));
        Assert.Throws<InvalidOperationException>(() => plain.GetTruePeak(0));
        Assert.Throws<InvalidOperationException>(() => plain.GetPreviousTruePeak(0));

        using LoudnessMeter sample = new(Channels, SampleRate,
            LoudnessModes.Integrated | LoudnessModes.SamplePeak);
        Assert.Equal(0.0, sample.GetSamplePeak(0));
        Assert.Throws<InvalidOperationException>(() => sample.GetTruePeak(0));
    }

    [Fact]
    public void WindowQueryRejectsOutOfRangeArguments()
    {
        using LoudnessMeter meter = Measured(LoudnessModes.All);

        Assert.Throws<ArgumentOutOfRangeException>(() => meter.GetLoudnessOverWindow(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => meter.GetLoudnessOverWindow(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => meter.GetLoudnessOverWindow(3001));

        Assert.Equal(meter.ShortTermLoudness, meter.GetLoudnessOverWindow(3000));
    }

    [Fact]
    public void ChannelIndicesAreValidated()
    {
        using LoudnessMeter meter = Measured(LoudnessModes.All);

        Assert.Throws<ArgumentOutOfRangeException>(() => meter.GetChannel(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => meter.GetChannel(Channels));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => meter.SetChannel(Channels, ChannelPosition.Left));
        Assert.Throws<ArgumentOutOfRangeException>(() => meter.GetSamplePeak(Channels));
        Assert.Throws<ArgumentOutOfRangeException>(() => meter.GetTruePeak(-1));
    }

    [Fact]
    public void MaximumHistoryIsValidatedAndClamped()
    {
        using LoudnessMeter meter = new(Channels, SampleRate, LoudnessModes.All);

        Assert.Throws<ArgumentOutOfRangeException>(() => meter.SetMaxHistory(-1));

        meter.SetMaxHistory(0);
        Assert.Equal(3000L, meter.MaxHistoryMilliseconds);

        meter.SetMaxHistory(120000);
        Assert.Equal(120000L, meter.MaxHistoryMilliseconds);
    }

    [Fact]
    public void ConstructionClampsTheHistoryTheSameWayAsTheSetter()
    {
        LoudnessMeterOptions options = new() { MaxHistoryMilliseconds = 0 };

        using LoudnessMeter range = new(Channels, SampleRate, LoudnessModes.All, options);
        Assert.Equal(3000L, range.MaxHistoryMilliseconds);

        using LoudnessMeter momentary = new(
            Channels, SampleRate, LoudnessModes.Momentary, options);
        Assert.Equal(400L, momentary.MaxHistoryMilliseconds);
    }

    [Fact]
    public void PreallocatedHistoryRefusesToGrow()
    {
        LoudnessMeterOptions options = new()
        {
            MaxHistoryMilliseconds = 30000,
            PreallocateHistory = true,
        };

        using LoudnessMeter meter = new(Channels, SampleRate, LoudnessModes.All, options);

        meter.SetMaxHistory(10000);
        Assert.Equal(10000L, meter.MaxHistoryMilliseconds);

        meter.SetMaxHistory(30000);
        Assert.Equal(30000L, meter.MaxHistoryMilliseconds);

        Assert.Throws<InvalidOperationException>(() => meter.SetMaxHistory(30001));
    }

    [Fact]
    public void NegativeOptionsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LoudnessMeter(
            Channels, SampleRate, LoudnessModes.All,
            new LoudnessMeterOptions { MaxWindowMilliseconds = -1 }));

        Assert.Throws<ArgumentOutOfRangeException>(() => new LoudnessMeter(
            Channels, SampleRate, LoudnessModes.All,
            new LoudnessMeterOptions { MaxHistoryMilliseconds = -1 }));
    }

    [Fact]
    public void MaximumWindowExtendsTheAvailableQueryRange()
    {
        LoudnessMeterOptions options = new() { MaxWindowMilliseconds = 10000 };
        using LoudnessMeter wide = new(Channels, SampleRate, LoudnessModes.All, options);

        Assert.Equal(10000L, wide.MaxWindowMilliseconds);

        wide.AddFrames(BuildChunk(Channels, SampleRate * 12));
        double overTen = wide.GetLoudnessOverWindow(10000);
        Assert.True(double.IsFinite(overTen));

        using LoudnessMeter narrow = new(Channels, SampleRate, LoudnessModes.All);
        Assert.Equal(3000L, narrow.MaxWindowMilliseconds);
        Assert.Throws<ArgumentOutOfRangeException>(() => narrow.GetLoudnessOverWindow(10000));
    }

    private static (LoudnessMeter Narrow, LoudnessMeter Wide) FeedBoth(int seconds)
    {
        int frames = SampleRate * seconds;
        double[] input = BuildChunk(Channels, frames);

        LoudnessMeter narrow = new(Channels, SampleRate, LoudnessModes.All);
        LoudnessMeter wide = new(Channels, SampleRate, LoudnessModes.All,
            new LoudnessMeterOptions { MaxWindowMilliseconds = 20000 });

        for (int offset = 0; offset < frames; offset += 4801)
        {
            int take = Math.Min(4801, frames - offset);
            narrow.AddFrames(input.AsSpan(offset * Channels, take * Channels));
            wide.AddFrames(input.AsSpan(offset * Channels, take * Channels));
        }

        return (narrow, wide);
    }

    [Fact]
    public void MaximumWindowLeavesResultsIdenticalWhileNeitherRingWraps()
    {
        (LoudnessMeter narrow, LoudnessMeter wide) = FeedBoth(2);

        using (narrow)
        using (wide)
        {
            BitwiseAssert.Equal(narrow.IntegratedLoudness, wide.IntegratedLoudness, "integrated");
            BitwiseAssert.Equal(narrow.MomentaryLoudness, wide.MomentaryLoudness, "momentary");
            BitwiseAssert.Equal(narrow.GetTruePeak(0), wide.GetTruePeak(0), "true peak");
        }
    }

    [Fact]
    public void MaximumWindowShiftsGatedResultsOnlyByRounding()
    {
        (LoudnessMeter narrow, LoudnessMeter wide) = FeedBoth(12);

        using (narrow)
        using (wide)
        {
            BitwiseAssert.Equal(narrow.GetTruePeak(0), wide.GetTruePeak(0), "true peak");
            BitwiseAssert.Equal(narrow.GetSamplePeak(0), wide.GetSamplePeak(0), "sample peak");

            Assert.True(
                BitwiseAssert.UlpDistance(narrow.IntegratedLoudness, wide.IntegratedLoudness) <= 8,
                "the integrated loudness moved by more than rounding");
            Assert.True(
                BitwiseAssert.UlpDistance(narrow.MomentaryLoudness, wide.MomentaryLoudness) <= 8,
                "the momentary loudness moved by more than rounding");
        }
    }
}
