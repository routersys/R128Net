namespace R128Net.Tests;

public class AllocationTests
{
    private const int SampleRate = 48000;
    private const int Channels = 2;
    private const int ChunkFrames = 4801;

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

    private static void AssertNoAllocation(Action warmUp, Action measured)
    {
        const int RequiredCleanWindows = 3;
        const int MaximumWindows = 12;

        long last = 0;
        int clean = 0;

        for (int window = 0; window < MaximumWindows && clean < RequiredCleanWindows; ++window)
        {
            for (int i = 0; i < 8; ++i)
            {
                warmUp();
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 8; ++i)
            {
                measured();
            }
            long after = GC.GetAllocatedBytesForCurrentThread();

            last = after - before;
            clean = last == 0 ? clean + 1 : 0;
        }

        Assert.True(clean >= RequiredCleanWindows,
            $"fewer than {RequiredCleanWindows} consecutive windows without a managed allocation "
            + $"within {MaximumWindows} windows; the last window allocated {last} bytes");
    }

    [Fact]
    public void AddingDoubleFramesDoesNotAllocate()
    {
        using LoudnessMeter meter = new(Channels, SampleRate, LoudnessModes.All);
        double[] chunk = BuildChunk(Channels, ChunkFrames);

        AssertNoAllocation(() => meter.AddFrames(chunk), () => meter.AddFrames(chunk));
    }

    [Fact]
    public void AddingSingleFramesDoesNotAllocate()
    {
        using LoudnessMeter meter = new(Channels, SampleRate, LoudnessModes.All);
        double[] source = BuildChunk(Channels, ChunkFrames);
        float[] chunk = new float[source.Length];
        for (int i = 0; i < source.Length; ++i)
        {
            chunk[i] = (float)source[i];
        }

        AssertNoAllocation(() => meter.AddFrames(chunk), () => meter.AddFrames(chunk));
    }

    [Fact]
    public void AddingShortFramesDoesNotAllocate()
    {
        using LoudnessMeter meter = new(Channels, SampleRate, LoudnessModes.All);
        double[] source = BuildChunk(Channels, ChunkFrames);
        short[] chunk = new short[source.Length];
        for (int i = 0; i < source.Length; ++i)
        {
            chunk[i] = (short)(source[i] * 32767.0);
        }

        AssertNoAllocation(() => meter.AddFrames(chunk), () => meter.AddFrames(chunk));
    }

    [Fact]
    public void AddingIntegerFramesDoesNotAllocate()
    {
        using LoudnessMeter meter = new(Channels, SampleRate, LoudnessModes.All);
        double[] source = BuildChunk(Channels, ChunkFrames);
        int[] chunk = new int[source.Length];
        for (int i = 0; i < source.Length; ++i)
        {
            chunk[i] = (int)(source[i] * 2147483647.0);
        }

        AssertNoAllocation(() => meter.AddFrames(chunk), () => meter.AddFrames(chunk));
    }

    [Fact]
    public void QueriesDoNotAllocate()
    {
        using LoudnessMeter meter = new(Channels, SampleRate, LoudnessModes.All);
        double[] chunk = BuildChunk(Channels, ChunkFrames);

        for (int i = 0; i < 60; ++i)
        {
            meter.AddFrames(chunk);
        }

        double sink = 0.0;

        void Query()
        {
            sink += meter.IntegratedLoudness;
            sink += meter.MomentaryLoudness;
            sink += meter.ShortTermLoudness;
            sink += meter.LoudnessRange;
            sink += meter.RelativeThreshold;
            sink += meter.GetLoudnessOverWindow(400);
            sink += meter.GetSamplePeak(0);
            sink += meter.GetTruePeak(0);
            sink += meter.GetPreviousSamplePeak(1);
            sink += meter.GetPreviousTruePeak(1);
        }

        AssertNoAllocation(Query, Query);
        Assert.NotEqual(0.0, sink);
    }

    [Fact]
    public void HistogramQueriesDoNotAllocate()
    {
        using LoudnessMeter meter = new(Channels, SampleRate,
            LoudnessModes.All | LoudnessModes.Histogram);
        double[] chunk = BuildChunk(Channels, ChunkFrames);

        for (int i = 0; i < 60; ++i)
        {
            meter.AddFrames(chunk);
        }

        double sink = 0.0;

        void Query()
        {
            sink += meter.IntegratedLoudness;
            sink += meter.LoudnessRange;
            sink += meter.RelativeThreshold;
        }

        AssertNoAllocation(Query, Query);
        Assert.NotEqual(0.0, sink);
    }

    [Fact]
    public void PreallocatedHistoryNeverGrowsDuringMeasurement()
    {
        LoudnessMeterOptions options = new()
        {
            MaxHistoryMilliseconds = 600000,
            PreallocateHistory = true,
        };

        using LoudnessMeter meter = new(Channels, SampleRate, LoudnessModes.All, options);
        double[] chunk = BuildChunk(Channels, ChunkFrames);

        AssertNoAllocation(() => meter.AddFrames(chunk), () => meter.AddFrames(chunk));

        Assert.Equal(600000L, meter.MaxHistoryMilliseconds);
    }
}
