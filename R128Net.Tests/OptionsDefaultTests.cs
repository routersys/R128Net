namespace R128Net.Tests;

public class OptionsDefaultTests
{
    [Fact]
    public void DefaultAndConstructedOptionsAreEqual()
    {
        LoudnessMeterOptions constructed = new();
        LoudnessMeterOptions defaulted = default;

        Assert.Equal(constructed, defaulted);
        Assert.Equal(LoudnessMeterOptions.UpstreamMaxHistoryMilliseconds, defaulted.MaxHistoryMilliseconds);
        Assert.Equal(0, defaulted.MaxWindowMilliseconds);
        Assert.False(defaulted.PreallocateHistory);
        Assert.False(defaulted.UseUpstreamWindowOverflow);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(400L)]
    [InlineData(3000L)]
    [InlineData(4294967295L)]
    [InlineData(4294967296L)]
    [InlineData(long.MaxValue)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public void ExplicitHistoryValuesRoundTrip(long history)
    {
        LoudnessMeterOptions options = new() { MaxHistoryMilliseconds = history };

        Assert.Equal(history, options.MaxHistoryMilliseconds);
    }

    [Fact]
    public void NegativeHistoryIsStillRejectedByTheMeter()
    {
        LoudnessMeterOptions options = new() { MaxHistoryMilliseconds = -1 };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LoudnessMeter(2, 48000, LoudnessModes.All, options));
    }

    [Fact]
    public void ADefaultOptionsValueMeasuresLikeTheDefaultConstructor()
    {
        double[] data = new double[48000 * 2 * 9];
        uint seed = 2463534242u;
        for (int i = 0; i < data.Length; ++i)
        {
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            double amplitude = i < 48000 * 2 * 3 ? 0.25 : 0.01;
            data[i] = (((seed / 4294967296.0) * 2.0) - 1.0) * amplitude;
        }

        using LoudnessMeter constructed = new(2, 48000, LoudnessModes.All);
        using LoudnessMeter defaulted = new(2, 48000, LoudnessModes.All, default(LoudnessMeterOptions));

        constructed.AddFrames(data);
        defaulted.AddFrames(data);

        Assert.Equal(constructed.MaxHistoryMilliseconds, defaulted.MaxHistoryMilliseconds);
        BitwiseAssert.Equal(constructed.IntegratedLoudness, defaulted.IntegratedLoudness, "integrated");
        BitwiseAssert.Equal(constructed.LoudnessRange, defaulted.LoudnessRange, "range");
    }
}
