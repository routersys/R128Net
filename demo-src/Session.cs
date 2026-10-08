using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using R128Net;

public sealed class Session
{
    public const int MaximumSeconds = 120;
    public const int ModeSetCount = 6;
    private const int MaximumValues = 16_000_000;
    private const int GeneratedRate = 48000;
    private const int StepsPerSecond = 10;
    private const double MomentarySeconds = 0.4;
    private const double ShortTermSeconds = 3.0;
    private const int WaveHeaderLength = 44;
    private const int WarmUpQueries = 300;

    private static readonly LoudnessModes[] ModeSets =
    [
        LoudnessModes.Momentary,
        LoudnessModes.Integrated,
        LoudnessModes.LoudnessRange,
        LoudnessModes.Integrated | LoudnessModes.SamplePeak,
        LoudnessModes.Integrated | LoudnessModes.TruePeak,
        LoudnessModes.All,
    ];

    private static double s_sink;

    private float[] _samples = [];
    private int _channels;
    private int _rate;
    private double[] _timeline = [];
    private double[] _renderReport = [];

    public int Channels => _channels;

    public int Rate => _rate;

    public int Frames => _channels == 0 ? 0 : _samples.Length / _channels;

    public double[] LoadWave(byte[] wave)
    {
        ReadOnlySpan<byte> data = wave;
        if (data.Length < 12 || !data[..4].SequenceEqual("RIFF"u8) || !data.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException("The file is not a RIFF WAVE file.");
        }

        int tag = 0;
        int channels = 0;
        int rate = 0;
        int bits = 0;
        bool hasFormat = false;
        ReadOnlySpan<byte> payload = default;
        bool hasData = false;

        int position = 12;
        while (position + 8 <= data.Length)
        {
            ReadOnlySpan<byte> id = data.Slice(position, 4);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(data[(position + 4)..]);
            int body = position + 8;
            int length = (int)Math.Min(size, (uint)(data.Length - body));

            if (id.SequenceEqual("fmt "u8) && length >= 16)
            {
                ReadOnlySpan<byte> format = data.Slice(body, length);
                tag = BinaryPrimitives.ReadUInt16LittleEndian(format);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(format[2..]);
                rate = (int)BinaryPrimitives.ReadUInt32LittleEndian(format[4..]);
                bits = BinaryPrimitives.ReadUInt16LittleEndian(format[14..]);
                if (tag == 0xFFFE && length >= 26)
                {
                    tag = BinaryPrimitives.ReadUInt16LittleEndian(format[24..]);
                }

                hasFormat = true;
            }
            else if (id.SequenceEqual("data"u8))
            {
                payload = data.Slice(body, length);
                hasData = true;
            }

            position = body + length + (length & 1);
        }

        if (!hasFormat || !hasData)
        {
            throw new InvalidDataException("The WAVE file has no format or no audio data.");
        }

        if (channels < 1 || channels > LoudnessMeter.MaximumChannels)
        {
            throw new NotSupportedException("The WAVE file has " + channels + " channels.");
        }

        bool isFloat = tag == 3;
        if (!(tag == 1 && (bits == 8 || bits == 16 || bits == 24 || bits == 32))
            && !(isFloat && (bits == 32 || bits == 64)))
        {
            throw new NotSupportedException("The WAVE encoding (tag " + tag + ", " + bits + " bits) is not supported.");
        }

        int width = bits / 8;
        int available = payload.Length / (width * channels);
        int frames = Math.Min(available, Math.Min(rate * MaximumSeconds, MaximumValues / channels));
        float[] samples = new float[frames * channels];
        for (int i = 0; i < samples.Length; ++i)
        {
            ReadOnlySpan<byte> at = payload[(i * width)..];
            samples[i] = tag == 1
                ? bits switch
                {
                    8 => (at[0] - 128) / 128.0f,
                    16 => BinaryPrimitives.ReadInt16LittleEndian(at) / 32768.0f,
                    24 => (((at[0] << 8) | (at[1] << 16) | (at[2] << 24)) >> 8) / 8388608.0f,
                    _ => (float)(BinaryPrimitives.ReadInt32LittleEndian(at) / 2147483648.0),
                }
                : bits == 32
                    ? BinaryPrimitives.ReadSingleLittleEndian(at)
                    : (float)BinaryPrimitives.ReadDoubleLittleEndian(at);
        }

        return Accept(samples, channels, rate, available, bits);
    }

    public double[] LoadSamples(byte[] interleaved, int channels, int rate)
    {
        if (channels < 1 || channels > LoudnessMeter.MaximumChannels)
        {
            throw new NotSupportedException("The audio has " + channels + " channels.");
        }

        int available = interleaved.Length / (sizeof(float) * channels);
        int frames = Math.Min(available, Math.Min(rate * MaximumSeconds, MaximumValues / channels));
        float[] samples = new float[frames * channels];
        Buffer.BlockCopy(interleaved, 0, samples, 0, samples.Length * sizeof(float));
        return Accept(samples, channels, rate, available, 0);
    }

    public double[] Generate(int kind)
    {
        switch (kind)
        {
            case 0:
                return Accept(Programme(), 2, GeneratedRate, 40 * GeneratedRate, 0);
            case 1:
                return Accept(Tone(1000.0, -23.0, 0.0, 20), 2, GeneratedRate, 20 * GeneratedRate, 0);
            case 2:
                return Accept(Tone(12000.0, -6.0, Math.PI / 4.0, 10), 2, GeneratedRate, 10 * GeneratedRate, 0);
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    public double[] Measure()
    {
        int stepFrames = _rate / StepsPerSecond;
        int frames = Frames;
        int steps = (frames + stepFrames - 1) / stepFrames;
        double[] timeline = new double[steps * 2];
        double highestMomentary = double.NaN;
        double highestShortTerm = double.NaN;

        using LoudnessMeter meter = new(_channels, _rate, LoudnessModes.All);
        Stopwatch watch = Stopwatch.StartNew();
        for (int step = 0; step < steps; ++step)
        {
            int offset = step * stepFrames;
            int take = Math.Min(stepFrames, frames - offset);
            meter.AddFrames(_samples.AsSpan(offset * _channels, take * _channels));

            double fed = (offset + take) / (double)_rate;
            double momentary = fed >= MomentarySeconds ? meter.MomentaryLoudness : double.NaN;
            double shortTerm = fed >= ShortTermSeconds ? meter.ShortTermLoudness : double.NaN;
            timeline[step * 2] = momentary;
            timeline[(step * 2) + 1] = shortTerm;
            if (!double.IsNaN(momentary) && !(highestMomentary >= momentary))
            {
                highestMomentary = momentary;
            }

            if (!double.IsNaN(shortTerm) && !(highestShortTerm >= shortTerm))
            {
                highestShortTerm = shortTerm;
            }
        }

        double elapsed = watch.Elapsed.TotalMilliseconds;
        _timeline = timeline;

        double[] report = new double[8 + (2 * _channels)];
        report[0] = meter.IntegratedLoudness;
        report[1] = meter.LoudnessRange;
        report[2] = meter.RelativeThreshold;
        report[3] = highestMomentary;
        report[4] = highestShortTerm;
        report[5] = elapsed;
        report[6] = steps;
        report[7] = stepFrames;
        for (int c = 0; c < _channels; ++c)
        {
            report[8 + c] = meter.GetSamplePeak(c);
            report[8 + _channels + c] = meter.GetTruePeak(c);
        }

        return report;
    }

    public double[] GetTimeline()
    {
        return _timeline;
    }

    public double[] TimeModes()
    {
        double[] best = new double[ModeSets.Length];
        for (int m = 0; m < ModeSets.Length; ++m)
        {
            double fastest = double.MaxValue;
            double total = 0.0;
            for (int run = 0; run < 20; ++run)
            {
                double milliseconds = FeedAll(ModeSets[m]);
                fastest = Math.Min(fastest, milliseconds);
                total += milliseconds;
                if ((run >= 2 && total >= 400.0) || total >= 2000.0)
                {
                    break;
                }
            }

            best[m] = fastest;
        }

        return best;
    }

    public double[] CheckAllocation()
    {
        long start = GC.GetAllocatedBytesForCurrentThread();
        byte[] probe = new byte[1 << 20];
        GC.KeepAlive(probe);
        long calibration = GC.GetAllocatedBytesForCurrentThread() - start;

        CountedPass(true);
        long measured = CountedPass(false);
        return [calibration, measured, Frames];
    }

    private long CountedPass(bool warmUp)
    {
        int stepFrames = _rate / StepsPerSecond;
        int frames = Frames;
        using LoudnessMeter meter = new(_channels, _rate, LoudnessModes.All);
        meter.AddFrames(_samples.AsSpan(0, Math.Min(stepFrames, frames) * _channels));
        meter.Reset();

        double sink = 0.0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int offset = 0; offset < frames; offset += stepFrames)
        {
            int take = Math.Min(stepFrames, frames - offset);
            meter.AddFrames(_samples.AsSpan(offset * _channels, take * _channels));
            sink += meter.MomentaryLoudness + meter.ShortTermLoudness;
        }

        sink += meter.IntegratedLoudness + meter.RelativeThreshold;
        for (int i = 0; i < (warmUp ? WarmUpQueries : 1); ++i)
        {
            sink += meter.LoudnessRange;
        }

        for (int c = 0; c < _channels; ++c)
        {
            sink += meter.GetSamplePeak(c) + meter.GetTruePeak(c);
        }

        long measured = GC.GetAllocatedBytesForCurrentThread() - before;
        s_sink = sink;
        return measured;
    }

    public byte[] RenderOriginal()
    {
        return Wave(Quantize(0.0, out _));
    }

    public byte[] Render(double gainDb)
    {
        short[] pcm = Quantize(gainDb, out int clipped);
        int stepFrames = _rate / StepsPerSecond;
        int frames = Frames;

        using LoudnessMeter meter = new(_channels, _rate, LoudnessModes.Integrated | LoudnessModes.TruePeak);
        for (int offset = 0; offset < frames; offset += stepFrames)
        {
            int take = Math.Min(stepFrames, frames - offset);
            meter.AddFrames(pcm.AsSpan(offset * _channels, take * _channels));
        }

        double samplePeak = 0.0;
        double truePeak = 0.0;
        for (int c = 0; c < _channels; ++c)
        {
            samplePeak = Math.Max(samplePeak, meter.GetSamplePeak(c));
            truePeak = Math.Max(truePeak, meter.GetTruePeak(c));
        }

        _renderReport = [meter.IntegratedLoudness, samplePeak, truePeak, clipped];
        return Wave(pcm);
    }

    public double[] GetRenderReport()
    {
        return _renderReport;
    }

    private double[] Accept(float[] samples, int channels, int rate, int originalFrames, int bits)
    {
        using (LoudnessMeter probe = new(channels, rate, LoudnessModes.Momentary))
        {
        }

        _samples = samples;
        _channels = channels;
        _rate = rate;
        _timeline = [];
        _renderReport = [];
        return [channels, rate, samples.Length / channels, bits, originalFrames];
    }

    private double FeedAll(LoudnessModes modes)
    {
        int stepFrames = _rate / StepsPerSecond;
        int frames = Frames;
        using LoudnessMeter meter = new(_channels, _rate, modes);
        Stopwatch watch = Stopwatch.StartNew();
        for (int offset = 0; offset < frames; offset += stepFrames)
        {
            int take = Math.Min(stepFrames, frames - offset);
            meter.AddFrames(_samples.AsSpan(offset * _channels, take * _channels));
        }

        double milliseconds = watch.Elapsed.TotalMilliseconds;
        s_sink = meter.MomentaryLoudness;
        return milliseconds;
    }

    private short[] Quantize(double gainDb, out int clipped)
    {
        double gain = Math.Pow(10.0, gainDb / 20.0) * 32768.0;
        short[] pcm = new short[_samples.Length];
        clipped = 0;
        for (int i = 0; i < pcm.Length; ++i)
        {
            double value = Math.Round(_samples[i] * gain);
            if (value > short.MaxValue)
            {
                value = short.MaxValue;
                ++clipped;
            }
            else if (value < short.MinValue)
            {
                value = short.MinValue;
                ++clipped;
            }

            pcm[i] = (short)value;
        }

        return pcm;
    }

    private byte[] Wave(short[] pcm)
    {
        int bytes = pcm.Length * sizeof(short);
        byte[] wave = new byte[WaveHeaderLength + bytes];
        Span<byte> header = wave;
        "RIFF"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], 36 + bytes);
        "WAVEfmt "u8.CopyTo(header[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(header[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(header[22..], (short)_channels);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..], _rate);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..], _rate * _channels * sizeof(short));
        BinaryPrimitives.WriteInt16LittleEndian(header[32..], (short)(_channels * sizeof(short)));
        BinaryPrimitives.WriteInt16LittleEndian(header[34..], 16);
        "data"u8.CopyTo(header[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[40..], bytes);
        MemoryMarshal.AsBytes(pcm.AsSpan()).CopyTo(header[WaveHeaderLength..]);
        return wave;
    }

    private static float[] Tone(double frequency, double levelDb, double phase, int seconds)
    {
        double amplitude = Math.Pow(10.0, levelDb / 20.0);
        float[] samples = new float[GeneratedRate * seconds * 2];
        for (int n = 0; n < GeneratedRate * seconds; ++n)
        {
            float value = (float)(amplitude * Math.Sin((2.0 * Math.PI * frequency * n / GeneratedRate) + phase));
            samples[2 * n] = value;
            samples[(2 * n) + 1] = value;
        }

        return samples;
    }

    private static float[] Programme()
    {
        const int Seconds = 40;
        const double SectionSeconds = 10.0;
        const double Edge = 0.05;
        double[] quiet = [220.0, 277.18, 329.63];
        double[] bright = [440.0, 554.37, 659.25];
        double[] levels = [-26.5, -7.0, -76.0, -18.5];

        float[] samples = new float[GeneratedRate * Seconds * 2];
        uint state = 2463534242u;
        for (int n = 0; n < GeneratedRate * Seconds; ++n)
        {
            double t = (double)n / GeneratedRate;
            int section = Math.Min((int)(t / SectionSeconds), levels.Length - 1);
            double into = t - (section * SectionSeconds);
            double fade = Math.Min(1.0, Math.Min(into, SectionSeconds - into) / Edge);
            double amplitude = Math.Pow(10.0, levels[section] / 20.0) * fade;

            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            double noise = ((state / 4294967296.0) * 2.0) - 1.0;

            double[] chord = section == 1 ? bright : quiet;
            double pulse = section == 1 ? 0.55 + (0.45 * Math.Exp(-5.0 * ((t * 2.0) % 1.0))) : 1.0;
            double hat = section == 1 ? 0.25 * noise * Math.Exp(-40.0 * ((t * 4.0) % 1.0)) : 0.0;

            for (int c = 0; c < 2; ++c)
            {
                double sum = 0.0;
                for (int v = 0; v < chord.Length; ++v)
                {
                    sum += Math.Sin((2.0 * Math.PI * chord[v] * t) + (c * 0.8 * (v + 1)));
                }

                samples[(2 * n) + c] = (float)(amplitude * ((sum / 3.0 * pulse) + hat));
            }
        }

        return samples;
    }
}
