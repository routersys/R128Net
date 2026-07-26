namespace R128Net;

[StateLayout]
internal unsafe partial struct MeterBuffers
{
    public double* AudioData;
    public double* FilterState;
    public ChannelPosition* ChannelMap;
    public double* SamplePeak;
    public double* PreviousSamplePeak;
    public double* TruePeak;
    public double* PreviousTruePeak;
    public ulong* BlockHistogram;
    public ulong* ShortTermHistogram;
    public Interpolator Interpolator;

    public int Channels;
    public int AudioDataSamples;
    public int HistogramBins;

    public static void Layout<TAllocator>(
        ref TAllocator allocator,
        int channels,
        int audioDataSamples,
        int taps,
        int factor,
        int histogramBins,
        ref MeterBuffers state)
        where TAllocator : struct, IStateAllocator
    {
        state.AudioData = (double*)allocator.Allocate(audioDataSamples, sizeof(double));
        state.FilterState = (double*)allocator.Allocate(
            KWeightingFilter.StateTaps * channels, sizeof(double));
        state.ChannelMap = (ChannelPosition*)allocator.Allocate(
            channels, (nuint)sizeof(ChannelPosition));
        state.SamplePeak = (double*)allocator.Allocate(channels, sizeof(double));
        state.PreviousSamplePeak = (double*)allocator.Allocate(channels, sizeof(double));
        state.TruePeak = (double*)allocator.Allocate(channels, sizeof(double));
        state.PreviousTruePeak = (double*)allocator.Allocate(channels, sizeof(double));
        state.BlockHistogram = (ulong*)allocator.Allocate(histogramBins, sizeof(ulong));
        state.ShortTermHistogram = (ulong*)allocator.Allocate(histogramBins, sizeof(ulong));

        if (factor > 0)
        {
            Interpolator.Layout(ref allocator, taps, factor, channels, ref state.Interpolator);
        }
    }

    public void Initialize(int channels, int audioDataSamples, int taps, int factor,
        int histogramBins)
    {
        Channels = channels;
        AudioDataSamples = audioDataSamples;
        HistogramBins = histogramBins;

        if (factor > 0)
        {
            Interpolator.Initialize(taps, factor);
        }
    }

    public void ClearMeasurementState()
    {
        new Span<double>(AudioData, AudioDataSamples).Clear();
        new Span<double>(FilterState, KWeightingFilter.StateTaps * Channels).Clear();
        new Span<double>(SamplePeak, Channels).Clear();
        new Span<double>(PreviousSamplePeak, Channels).Clear();
        new Span<double>(TruePeak, Channels).Clear();
        new Span<double>(PreviousTruePeak, Channels).Clear();

        if (HistogramBins > 0)
        {
            new Span<ulong>(BlockHistogram, HistogramBins).Clear();
            new Span<ulong>(ShortTermHistogram, HistogramBins).Clear();
        }

        if (Interpolator.Factor > 0)
        {
            Interpolator.Reset(Channels);
        }
    }
}
