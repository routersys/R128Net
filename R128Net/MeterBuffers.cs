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
}
