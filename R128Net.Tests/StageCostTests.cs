using System.Diagnostics;
using System.Globalization;
using Xunit.Abstractions;

namespace R128Net.Tests;

public class StageCostTests
{
    private readonly ITestOutputHelper _output;

    public StageCostTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static double Measure(LoudnessModes modes, int channels, double[] input,
        int frames, int repeats)
    {
        double best = double.MaxValue;

        for (int round = 0; round < 5; ++round)
        {
            using LoudnessMeter meter = new(channels, 48000, modes);

            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < repeats; ++i)
            {
                meter.AddFrames(input.AsSpan(0, frames * channels));
            }
            stopwatch.Stop();

            double elapsed = stopwatch.Elapsed.TotalMilliseconds * 1e6
                / ((double)frames * channels * repeats);
            if (round > 0 && elapsed < best)
            {
                best = elapsed;
            }
        }

        return best;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(6)]
    public void ReportCostOfEachStage(int channels)
    {
        const int Frames = 4800;
        const int Repeats = 100;

        double[] input = new double[Frames * channels];
        uint seed = 2463534242u;
        for (int i = 0; i < input.Length; ++i)
        {
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            input[i] = ((seed / 4294967296.0) * 2.0) - 1.0;
        }

        double momentary = Measure(LoudnessModes.Momentary, channels, input, Frames, Repeats);
        double integrated = Measure(LoudnessModes.Integrated, channels, input, Frames, Repeats);
        double withPeak = Measure(
            LoudnessModes.Integrated | LoudnessModes.SamplePeak, channels, input, Frames, Repeats);
        double all = Measure(LoudnessModes.All, channels, input, Frames, Repeats);

        CultureInfo culture = CultureInfo.InvariantCulture;
        _output.WriteLine(
            "channels {0}: filter {1}  gating {2}  samplePeak {3}  truePeak {4}  total {5} ns/sample",
            channels.ToString(culture),
            momentary.ToString("F2", culture),
            (integrated - momentary).ToString("F2", culture),
            (withPeak - integrated).ToString("F2", culture),
            (all - withPeak).ToString("F2", culture),
            all.ToString("F2", culture));

        Assert.True(all > 0.0);
    }
}
