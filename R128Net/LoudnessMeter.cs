using System.Runtime.InteropServices;

namespace R128Net;

public sealed unsafe class LoudnessMeter : IDisposable
{
    public const int MaximumChannels = 64;
    public const int MinimumSampleRate = 16;
    public const int MaximumSampleRate = 2822400;

    private const int InterpolatorTaps = 49;

    private readonly int _channels;
    private readonly int _sampleRate;
    private readonly LoudnessModes _modes;
    private readonly bool _useHistogram;
    private readonly bool _upstreamOverflow;
    private readonly bool _preallocate;
    private readonly nuint _samplesIn100ms;
    private readonly KWeighting _weighting;

    private StateMemory _memory;
    private MeterBuffers _buffers;
    private BlockEnergyList _blocks;
    private BlockEnergyList _shortTermBlocks;

    private nuint _audioDataFrames;
    private nuint _audioDataIndex;
    private nuint _neededFrames;
    private nuint _shortTermFrameCounter;
    private long _window;
    private long _history;

    private double* _sortScratch;
    private nuint _sortCapacity;
    private bool _disposed;

    public LoudnessMeter(int channels, int sampleRate, LoudnessModes modes)
        : this(channels, sampleRate, modes, new LoudnessMeterOptions())
    {
    }

    public LoudnessMeter(int channels, int sampleRate, LoudnessModes modes,
        in LoudnessMeterOptions options)
    {
        if (channels is < 1 or > MaximumChannels)
        {
            throw new ArgumentOutOfRangeException(nameof(channels), channels,
                $"The channel count must lie between 1 and {MaximumChannels}.");
        }

        if (sampleRate is < MinimumSampleRate or > MaximumSampleRate)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate,
                $"The sample rate must lie between {MinimumSampleRate} and {MaximumSampleRate}.");
        }

        if ((modes & LoudnessModes.Momentary) != LoudnessModes.Momentary)
        {
            throw new ArgumentException(
                "At least the momentary mode must be requested.", nameof(modes));
        }

        _weighting = KWeighting.Create(sampleRate);
        if (!_weighting.IsStable)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate,
                "The BS.1770 pre-filter is divergent at this sample rate because the "
                + "shelving section aliases past the Nyquist frequency.");
        }

        if (options.MaxHistoryMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options),
                options.MaxHistoryMilliseconds,
                "The maximum history must not be negative.");
        }

        _channels = channels;
        _sampleRate = sampleRate;
        _modes = modes;
        _useHistogram = (modes & LoudnessModes.Histogram) == LoudnessModes.Histogram;
        _upstreamOverflow = options.UseUpstreamWindowOverflow;
        _preallocate = options.PreallocateHistory;
        _samplesIn100ms = (nuint)((sampleRate + 5) / 10);

        _window = (modes & LoudnessModes.ShortTerm) == LoudnessModes.ShortTerm ? 3000 : 400;
        _audioDataFrames = ComputeAudioDataFrames(_window);

        nuint samples = checked(_audioDataFrames * (nuint)channels);
        if (samples > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate,
                "The requested window does not fit a single addressable buffer.");
        }

        int factor = sampleRate < 96000 ? 4 : sampleRate < 192000 ? 2 : 0;
        int bins = _useHistogram ? HistogramTables.BinCount : 0;

        _memory = new StateMemory(MeterBuffers.GetRequiredBytes(
            channels, (int)samples, InterpolatorTaps, factor, bins));
        _buffers = MeterBuffers.Bind(
            _memory, channels, (int)samples, InterpolatorTaps, factor, bins);

        if (factor > 0)
        {
            _buffers.Interpolator.Initialize(InterpolatorTaps, factor);
        }

        InitializeChannelMap();

        _history = options.MaxHistoryMilliseconds;
        _blocks.Initialize((nuint)(_history / 100), _preallocate);
        _shortTermBlocks.Initialize((nuint)(_history / 3000), _preallocate);

        _neededFrames = _samplesIn100ms * 4;
        _audioDataIndex = 0;
        _shortTermFrameCounter = 0;
    }

    ~LoudnessMeter()
    {
        Release();
    }

    public int Channels => _channels;

    public int SampleRate => _sampleRate;

    public LoudnessModes Modes => _modes;

    public long MaxWindowMilliseconds => _window;

    public long MaxHistoryMilliseconds => _history;

    internal nuint BlockCount => _blocks.Count;

    internal nuint ShortTermBlockCount => _shortTermBlocks.Count;

    internal nuint AudioDataIndex => _audioDataIndex;

    internal nuint AudioDataFrames => _audioDataFrames;

    internal nuint NeededFrames => _neededFrames;

    internal nuint ShortTermFrameCounter => _shortTermFrameCounter;

    internal double GetBlockEnergy(nuint index) => _blocks[index];

    internal double GetShortTermBlockEnergy(nuint index) => _shortTermBlocks[index];

    internal ulong GetBlockHistogramBin(int index) => _buffers.BlockHistogram[index];

    internal ulong GetShortTermHistogramBin(int index) => _buffers.ShortTermHistogram[index];

    private nuint ComputeAudioDataFrames(long window)
    {
        nuint frames;
        if (_upstreamOverflow)
        {
            uint product = unchecked((uint)_sampleRate * (uint)window);
            frames = product / 1000;
        }
        else
        {
            frames = (nuint)((ulong)_sampleRate * (ulong)window / 1000);
        }

        nuint remainder = frames % _samplesIn100ms;
        if (remainder != 0)
        {
            frames = frames + _samplesIn100ms - remainder;
        }

        return frames;
    }

    private void InitializeChannelMap()
    {
        ChannelPosition* map = _buffers.ChannelMap;

        if (_channels == 4)
        {
            map[0] = ChannelPosition.Left;
            map[1] = ChannelPosition.Right;
            map[2] = ChannelPosition.LeftSurround;
            map[3] = ChannelPosition.RightSurround;
            return;
        }

        if (_channels == 5)
        {
            map[0] = ChannelPosition.Left;
            map[1] = ChannelPosition.Right;
            map[2] = ChannelPosition.Center;
            map[3] = ChannelPosition.LeftSurround;
            map[4] = ChannelPosition.RightSurround;
            return;
        }

        for (int i = 0; i < _channels; ++i)
        {
            map[i] = i switch
            {
                0 => ChannelPosition.Left,
                1 => ChannelPosition.Right,
                2 => ChannelPosition.Center,
                4 => ChannelPosition.LeftSurround,
                5 => ChannelPosition.RightSurround,
                _ => ChannelPosition.Unused,
            };
        }
    }

    public void SetChannel(int channel, ChannelPosition position)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (channel < 0 || channel >= _channels)
        {
            throw new ArgumentOutOfRangeException(nameof(channel), channel,
                "The channel index lies outside the configured channel count.");
        }

        if (position == ChannelPosition.DualMono && (_channels != 1 || channel != 0))
        {
            throw new ArgumentException(
                "The dual mono position only applies to the single channel of a mono meter.",
                nameof(position));
        }

        _buffers.ChannelMap[channel] = position;
    }

    public ChannelPosition GetChannel(int channel)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (channel < 0 || channel >= _channels)
        {
            throw new ArgumentOutOfRangeException(nameof(channel), channel,
                "The channel index lies outside the configured channel count.");
        }

        return _buffers.ChannelMap[channel];
    }

    public void AddFrames(ReadOnlySpan<short> interleaved)
    {
        Add<Int16Format, short>(interleaved);
    }

    public void AddFrames(ReadOnlySpan<int> interleaved)
    {
        Add<Int32Format, int>(interleaved);
    }

    public void AddFrames(ReadOnlySpan<float> interleaved)
    {
        Add<SingleFormat, float>(interleaved);
    }

    public void AddFrames(ReadOnlySpan<double> interleaved)
    {
        Add<DoubleFormat, double>(interleaved);
    }

    private void Add<TFormat, TSample>(ReadOnlySpan<TSample> interleaved)
        where TFormat : struct, ISampleFormat<TSample>
        where TSample : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (interleaved.Length % _channels != 0)
        {
            throw new ArgumentException(
                "The interleaved buffer must hold a whole number of frames.",
                nameof(interleaved));
        }

        fixed (TSample* source = interleaved)
        {
            AddCore<TFormat, TSample>(source, (nuint)(interleaved.Length / _channels));
        }
    }

    private void AddCore<TFormat, TSample>(TSample* source, nuint frames)
        where TFormat : struct, ISampleFormat<TSample>
        where TSample : unmanaged
    {
        for (int c = 0; c < _channels; ++c)
        {
            _buffers.PreviousSamplePeak[c] = 0.0;
            _buffers.PreviousTruePeak[c] = 0.0;
        }

        nuint sourceIndex = 0;

        while (frames > 0)
        {
            if (frames >= _neededFrames)
            {
                Filter<TFormat, TSample>(source + sourceIndex, _neededFrames);
                sourceIndex += _neededFrames * (nuint)_channels;
                frames -= _neededFrames;
                _audioDataIndex += _neededFrames * (nuint)_channels;

                if ((_modes & LoudnessModes.Integrated) == LoudnessModes.Integrated)
                {
                    AddGatingBlock(_samplesIn100ms * 4);
                }

                if ((_modes & LoudnessModes.LoudnessRange) == LoudnessModes.LoudnessRange)
                {
                    _shortTermFrameCounter += _neededFrames;
                    if (_shortTermFrameCounter == _samplesIn100ms * 30)
                    {
                        AddShortTermBlock();
                        _shortTermFrameCounter = _samplesIn100ms * 20;
                    }
                }

                _neededFrames = _samplesIn100ms;

                if (_audioDataIndex == _audioDataFrames * (nuint)_channels)
                {
                    _audioDataIndex = 0;
                }
            }
            else
            {
                Filter<TFormat, TSample>(source + sourceIndex, frames);
                _audioDataIndex += frames * (nuint)_channels;

                if ((_modes & LoudnessModes.LoudnessRange) == LoudnessModes.LoudnessRange)
                {
                    _shortTermFrameCounter += frames;
                }

                _neededFrames -= frames;
                frames = 0;
            }
        }

        for (int c = 0; c < _channels; ++c)
        {
            if (_buffers.PreviousSamplePeak[c] > _buffers.SamplePeak[c])
            {
                _buffers.SamplePeak[c] = _buffers.PreviousSamplePeak[c];
            }

            if (_buffers.PreviousTruePeak[c] > _buffers.TruePeak[c])
            {
                _buffers.TruePeak[c] = _buffers.PreviousTruePeak[c];
            }
        }
    }

    private void Filter<TFormat, TSample>(TSample* source, nuint frames)
        where TFormat : struct, ISampleFormat<TSample>
        where TSample : unmanaged
    {
        int count = (int)frames;

        if ((_modes & LoudnessModes.SamplePeak) == LoudnessModes.SamplePeak)
        {
            SamplePeak.Accumulate<TFormat, TSample>(
                source, _buffers.PreviousSamplePeak, _channels, count);
        }

        if ((_modes & LoudnessModes.TruePeak) == LoudnessModes.TruePeak
            && _buffers.Interpolator.Factor > 0)
        {
            _buffers.Interpolator.AccumulatePeaks<TFormat, TSample>(
                source, _buffers.PreviousTruePeak, _channels, count);
        }

        KWeightingFilter.Process<TFormat, TSample>(
            source, _buffers.AudioData + _audioDataIndex, _buffers.FilterState,
            _buffers.ChannelMap, _channels, count, _weighting);
    }

    private double BlockEnergy(nuint framesPerBlock)
    {
        return GatingBlock.Energy(_buffers.AudioData, _buffers.ChannelMap, _channels,
            _audioDataFrames, _audioDataIndex, framesPerBlock);
    }

    private void AddGatingBlock(nuint framesPerBlock)
    {
        double energy = BlockEnergy(framesPerBlock);

        if (energy < HistogramTables.Boundaries[0])
        {
            return;
        }

        if (_useHistogram)
        {
            ++_buffers.BlockHistogram[LoudnessMath.FindHistogramIndex(energy)];
        }
        else
        {
            _blocks.Add(energy);
        }
    }

    private void AddShortTermBlock()
    {
        nuint interval = _samplesIn100ms * 30;
        if (interval > _audioDataFrames)
        {
            return;
        }

        double energy = BlockEnergy(interval);

        if (energy < HistogramTables.Boundaries[0])
        {
            return;
        }

        if (_useHistogram)
        {
            ++_buffers.ShortTermHistogram[LoudnessMath.FindHistogramIndex(energy)];
        }
        else
        {
            _shortTermBlocks.Add(energy);
        }
    }

    private double EnergyInInterval(nuint intervalFrames)
    {
        if (intervalFrames > _audioDataFrames)
        {
            throw new InvalidOperationException(
                "The requested interval exceeds the configured window.");
        }

        return BlockEnergy(intervalFrames);
    }

    private static double LoudnessOfEnergy(double energy)
    {
        return energy <= 0.0
            ? double.NegativeInfinity
            : LoudnessMath.EnergyToLoudness(energy);
    }

    public double MomentaryLoudness
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return LoudnessOfEnergy(EnergyInInterval(_samplesIn100ms * 4));
        }
    }

    public double ShortTermLoudness
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return LoudnessOfEnergy(EnergyInInterval(_samplesIn100ms * 30));
        }
    }

    public double GetLoudnessOverWindow(long windowMilliseconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (windowMilliseconds > _window)
        {
            throw new ArgumentOutOfRangeException(nameof(windowMilliseconds),
                windowMilliseconds,
                "The requested window exceeds the configured maximum window.");
        }

        nuint interval = _upstreamOverflow
            ? unchecked((uint)_sampleRate * (uint)windowMilliseconds) / 1000
            : (nuint)((ulong)_sampleRate * (ulong)windowMilliseconds / 1000);

        return LoudnessOfEnergy(EnergyInInterval(interval));
    }

    private void AccumulateRelativeThreshold(ref nuint counter, ref double threshold)
    {
        if (_useHistogram)
        {
            for (int i = 0; i < HistogramTables.BinCount; ++i)
            {
                threshold += _buffers.BlockHistogram[i] * HistogramTables.Energies[i];
                counter += (nuint)_buffers.BlockHistogram[i];
            }
        }
        else
        {
            for (nuint i = 0; i < _blocks.Count; ++i)
            {
                ++counter;
                threshold += _blocks[i];
            }
        }
    }

    public double RelativeThreshold
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            RequireIntegrated();

            nuint counter = 0;
            double threshold = 0.0;
            AccumulateRelativeThreshold(ref counter, ref threshold);

            if (counter == 0)
            {
                return -70.0;
            }

            threshold /= counter;
            threshold *= LoudnessMath.RelativeGateFactor;

            return LoudnessMath.EnergyToLoudness(threshold);
        }
    }

    public double IntegratedLoudness
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return GatedLoudness([this]);
        }
    }

    public static double GatedLoudness(ReadOnlySpan<LoudnessMeter> meters)
    {
        foreach (LoudnessMeter meter in meters)
        {
            meter?.RequireIntegrated();
        }

        nuint counter = 0;
        double threshold = 0.0;

        foreach (LoudnessMeter meter in meters)
        {
            meter?.AccumulateRelativeThreshold(ref counter, ref threshold);
        }

        if (counter == 0)
        {
            return double.NegativeInfinity;
        }

        threshold /= counter;
        threshold *= LoudnessMath.RelativeGateFactor;

        int startIndex = 0;
        if (threshold >= HistogramTables.Boundaries[0])
        {
            startIndex = LoudnessMath.FindHistogramIndex(threshold);
            if (threshold > HistogramTables.Energies[startIndex])
            {
                ++startIndex;
            }
        }

        counter = 0;
        double gated = 0.0;

        foreach (LoudnessMeter meter in meters)
        {
            if (meter is null)
            {
                continue;
            }

            if (meter._useHistogram)
            {
                for (int j = startIndex; j < HistogramTables.BinCount; ++j)
                {
                    gated += meter._buffers.BlockHistogram[j] * HistogramTables.Energies[j];
                    counter += (nuint)meter._buffers.BlockHistogram[j];
                }
            }
            else
            {
                for (nuint i = 0; i < meter._blocks.Count; ++i)
                {
                    double energy = meter._blocks[i];
                    if (energy >= threshold)
                    {
                        ++counter;
                        gated += energy;
                    }
                }
            }
        }

        if (counter == 0)
        {
            return double.NegativeInfinity;
        }

        gated /= counter;
        return LoudnessMath.EnergyToLoudness(gated);
    }

    private void RequireIntegrated()
    {
        if ((_modes & LoudnessModes.Integrated) != LoudnessModes.Integrated)
        {
            throw new InvalidOperationException(
                "The integrated mode was not requested for this meter.");
        }
    }

    private void RequireLoudnessRange()
    {
        if ((_modes & LoudnessModes.LoudnessRange) != LoudnessModes.LoudnessRange)
        {
            throw new InvalidOperationException(
                "The loudness range mode was not requested for this meter.");
        }
    }

    public double LoudnessRange => LoudnessRangeOf([this]);

    public static double LoudnessRangeOf(ReadOnlySpan<LoudnessMeter> meters)
    {
        bool useHistogram = false;
        bool first = true;

        foreach (LoudnessMeter meter in meters)
        {
            if (meter is null)
            {
                continue;
            }

            meter.RequireLoudnessRange();

            if (first)
            {
                useHistogram = meter._useHistogram;
                first = false;
            }
            else if (useHistogram != meter._useHistogram)
            {
                throw new InvalidOperationException(
                    "The meters must agree on whether they use the histogram algorithm.");
            }
        }

        return useHistogram ? HistogramRange(meters) : SortedRange(meters);
    }

    private static double HistogramRange(ReadOnlySpan<LoudnessMeter> meters)
    {
        Span<ulong> histogram = stackalloc ulong[HistogramTables.BinCount];
        histogram.Clear();

        ulong total = 0;
        double power = 0.0;

        foreach (LoudnessMeter meter in meters)
        {
            if (meter is null)
            {
                continue;
            }

            for (int j = 0; j < HistogramTables.BinCount; ++j)
            {
                ulong count = meter._buffers.ShortTermHistogram[j];
                histogram[j] += count;
                total += count;
                power += count * HistogramTables.Energies[j];
            }
        }

        if (total == 0)
        {
            return 0.0;
        }

        power /= total;
        double integrated = LoudnessMath.MinusTwentyDecibels * power;

        int index = 0;
        if (integrated >= HistogramTables.Boundaries[0])
        {
            index = LoudnessMath.FindHistogramIndex(integrated);
            if (integrated > HistogramTables.Energies[index])
            {
                ++index;
            }
        }

        total = 0;
        for (int j = index; j < HistogramTables.BinCount; ++j)
        {
            total += histogram[j];
        }

        if (total == 0)
        {
            return 0.0;
        }

        ulong percentileLow = (ulong)((total - 1) * 0.1 + 0.5);
        ulong percentileHigh = (ulong)((total - 1) * 0.95 + 0.5);

        total = 0;
        int position = index;
        while (total <= percentileLow)
        {
            total += histogram[position++];
        }
        double low = HistogramTables.Energies[position - 1];

        while (total <= percentileHigh)
        {
            total += histogram[position++];
        }
        double high = HistogramTables.Energies[position - 1];

        return LoudnessMath.EnergyToLoudness(high) - LoudnessMath.EnergyToLoudness(low);
    }

    private static double SortedRange(ReadOnlySpan<LoudnessMeter> meters)
    {
        nuint total = 0;
        LoudnessMeter? owner = null;

        foreach (LoudnessMeter meter in meters)
        {
            if (meter is null)
            {
                continue;
            }

            total += meter._shortTermBlocks.Count;
            owner ??= meter;
        }

        if (total == 0 || owner is null)
        {
            return 0.0;
        }

        double* values = owner.RentSortScratch(total);
        nuint written = 0;

        foreach (LoudnessMeter meter in meters)
        {
            if (meter is null)
            {
                continue;
            }

            for (nuint i = 0; i < meter._shortTermBlocks.Count; ++i)
            {
                values[written++] = meter._shortTermBlocks[i];
            }
        }

        Span<double> span = new(values, (int)total);
        span.Sort();

        double power = 0.0;
        for (nuint i = 0; i < total; ++i)
        {
            power += values[i];
        }

        power /= total;
        double integrated = LoudnessMath.MinusTwentyDecibels * power;

        nuint offset = 0;
        nuint remaining = total;
        while (remaining > 0 && values[offset] < integrated)
        {
            ++offset;
            --remaining;
        }

        if (remaining == 0)
        {
            return 0.0;
        }

        double high = values[offset + (nuint)((remaining - 1) * 0.95 + 0.5)];
        double low = values[offset + (nuint)((remaining - 1) * 0.1 + 0.5)];

        return LoudnessMath.EnergyToLoudness(high) - LoudnessMath.EnergyToLoudness(low);
    }

    private double* RentSortScratch(nuint count)
    {
        if (count <= _sortCapacity)
        {
            return _sortScratch;
        }

        if (_sortScratch is not null)
        {
            NativeMemory.AlignedFree(_sortScratch);
        }

        _sortScratch = (double*)NativeMemory.AlignedAlloc(
            checked(count * (nuint)sizeof(double)), StateMemory.AlignmentBytes);
        _sortCapacity = count;
        return _sortScratch;
    }

    private void RequirePeakMode(LoudnessModes required)
    {
        if ((_modes & required) != required)
        {
            throw new InvalidOperationException(
                "The requested peak mode was not enabled for this meter.");
        }
    }

    private int ValidateChannel(int channel)
    {
        if (channel < 0 || channel >= _channels)
        {
            throw new ArgumentOutOfRangeException(nameof(channel), channel,
                "The channel index lies outside the configured channel count.");
        }

        return channel;
    }

    public double GetSamplePeak(int channel)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RequirePeakMode(LoudnessModes.SamplePeak);
        return _buffers.SamplePeak[ValidateChannel(channel)];
    }

    public double GetPreviousSamplePeak(int channel)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RequirePeakMode(LoudnessModes.SamplePeak);
        return _buffers.PreviousSamplePeak[ValidateChannel(channel)];
    }

    public double GetTruePeak(int channel)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RequirePeakMode(LoudnessModes.TruePeak);
        int index = ValidateChannel(channel);
        double truePeak = _buffers.TruePeak[index];
        double samplePeak = _buffers.SamplePeak[index];
        return truePeak > samplePeak ? truePeak : samplePeak;
    }

    public double GetPreviousTruePeak(int channel)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RequirePeakMode(LoudnessModes.TruePeak);
        int index = ValidateChannel(channel);
        double truePeak = _buffers.PreviousTruePeak[index];
        double samplePeak = _buffers.PreviousSamplePeak[index];
        return truePeak > samplePeak ? truePeak : samplePeak;
    }

    public void SetMaxHistory(long milliseconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if ((_modes & LoudnessModes.LoudnessRange) == LoudnessModes.LoudnessRange
            && milliseconds < 3000)
        {
            milliseconds = 3000;
        }
        else if ((_modes & LoudnessModes.Momentary) == LoudnessModes.Momentary
            && milliseconds < 400)
        {
            milliseconds = 400;
        }

        if (milliseconds == _history)
        {
            return;
        }

        _history = milliseconds;
        _blocks.Trim((nuint)(_history / 100));
        _shortTermBlocks.Trim((nuint)(_history / 3000));
    }

    public void Dispose()
    {
        Release();
        GC.SuppressFinalize(this);
    }

    private void Release()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _blocks.Release();
        _shortTermBlocks.Release();

        if (_sortScratch is not null)
        {
            NativeMemory.AlignedFree(_sortScratch);
            _sortScratch = null;
            _sortCapacity = 0;
        }

        _memory?.Dispose();
    }
}
