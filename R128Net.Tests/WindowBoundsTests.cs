namespace R128Net.Tests;

public class WindowBoundsTests
{
    [Theory]
    [InlineData(48000, 1L << 57, false)]
    [InlineData(48000, 1L << 58, false)]
    [InlineData(48000, 1L << 62, false)]
    [InlineData(44100, 1L << 62, false)]
    [InlineData(2822400, long.MaxValue, false)]
    [InlineData(48000, 33554432L, true)]
    [InlineData(48000, 67108864L, true)]
    public void WindowsThatWrapTheFrameCountAreRejected(int sampleRate, long window, bool upstream)
    {
        LoudnessMeterOptions options = new()
        {
            MaxWindowMilliseconds = window,
            UseUpstreamWindowOverflow = upstream,
        };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LoudnessMeter(2, sampleRate, LoudnessModes.All, options));
    }

    [Fact]
    public void LargeWindowsThatFitAreAcceptedAndMeasureNormally()
    {
        LoudnessMeterOptions options = new() { MaxWindowMilliseconds = 60000 };

        using LoudnessMeter meter = new(2, 48000, LoudnessModes.All, options);
        double[] data = new double[48000 * 2];
        uint seed = 2463534242u;
        for (int i = 0; i < data.Length; ++i)
        {
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            data[i] = (((seed / 4294967296.0) * 2.0) - 1.0) * 0.25;
        }

        meter.AddFrames(data);
        Assert.True(double.IsFinite(meter.IntegratedLoudness));
        Assert.True(double.IsFinite(meter.GetLoudnessOverWindow(60000)));
    }

    [Fact]
    public void UpstreamOverflowStillReproducesTheDocumentedWrap()
    {
        LoudnessMeterOptions options = new() { UseUpstreamWindowOverflow = true };

        using LoudnessMeter meter = new(2, 2822400, LoudnessModes.All, options);
        Assert.Equal(3000, meter.MaxWindowMilliseconds);
    }
}
