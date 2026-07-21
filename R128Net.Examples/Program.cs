using System.Diagnostics;
using System.Globalization;
using R128Net;

const int SampleRate = 48000;
const int Channels = 2;
const int Seconds = 30;

double[] input = new double[SampleRate * Seconds * Channels];
uint seed = 2463534242u;
for (int i = 0; i < input.Length; ++i)
{
    seed ^= seed << 13;
    seed ^= seed >> 17;
    seed ^= seed << 5;
    input[i] = (((seed / 4294967296.0) * 2.0) - 1.0) * 0.25;
}

using LoudnessMeter meter = new(Channels, SampleRate, LoudnessModes.All);

Stopwatch stopwatch = Stopwatch.StartNew();
const int ChunkFrames = 4800;
for (int offset = 0; offset < SampleRate * Seconds; offset += ChunkFrames)
{
    meter.AddFrames(input.AsSpan(offset * Channels, ChunkFrames * Channels));
}
stopwatch.Stop();

CultureInfo culture = CultureInfo.InvariantCulture;

Console.WriteLine("integrated        {0} LUFS",
    meter.IntegratedLoudness.ToString("F4", culture));
Console.WriteLine("momentary         {0} LUFS",
    meter.MomentaryLoudness.ToString("F4", culture));
Console.WriteLine("short term        {0} LUFS",
    meter.ShortTermLoudness.ToString("F4", culture));
Console.WriteLine("loudness range    {0} LU",
    meter.LoudnessRange.ToString("F4", culture));
Console.WriteLine("relative gate     {0} LUFS",
    meter.RelativeThreshold.ToString("F4", culture));

for (int c = 0; c < Channels; ++c)
{
    Console.WriteLine("channel {0} peaks   sample {1}  true {2}",
        c.ToString(culture),
        meter.GetSamplePeak(c).ToString("F9", culture),
        meter.GetTruePeak(c).ToString("F9", culture));
}

double realtime = Seconds / stopwatch.Elapsed.TotalSeconds;
Console.WriteLine("analysed {0} seconds in {1} ms, {2} times real time",
    Seconds.ToString(culture),
    stopwatch.Elapsed.TotalMilliseconds.ToString("F1", culture),
    realtime.ToString("F0", culture));
