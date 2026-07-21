namespace R128Net.Tests;

public class MultipleMeterTests
{
    private const int SampleRate = 48000;
    private const int Channels = 2;
    private const int Seconds = 12;
    private const int ChunkFrames = 4801;
    private const int Parts = 3;

    private static readonly double[] Amplitudes =
    [
        1.0, 0.5, 0.25, 0.1, 0.03, 0.008, 0.0, 0.7, 0.002, 0.35
    ];

    private static double[] BuildInput()
    {
        int frames = SampleRate * Seconds;
        double[] input = new double[frames * Channels];
        uint seed = 2463534242u;

        for (int i = 0; i < frames; ++i)
        {
            double amplitude = Amplitudes[(i / SampleRate) % Amplitudes.Length];
            for (int c = 0; c < Channels; ++c)
            {
                seed ^= seed << 13;
                seed ^= seed >> 17;
                seed ^= seed << 5;
                input[(i * Channels) + c] =
                    (((seed / 4294967296.0) * 2.0) - 1.0) * amplitude;
            }
        }

        return input;
    }

    private static LoudnessMeter[] BuildMeters()
    {
        double[] input = BuildInput();
        int frames = SampleRate * Seconds;
        int span = frames / Parts;

        LoudnessMeter[] meters = new LoudnessMeter[Parts];

        for (int p = 0; p < Parts; ++p)
        {
            meters[p] = new LoudnessMeter(Channels, SampleRate,
                LoudnessModes.Integrated | LoudnessModes.LoudnessRange);

            int begin = p * span;
            int end = p + 1 == Parts ? frames : begin + span;

            for (int offset = begin; offset < end; offset += ChunkFrames)
            {
                int take = Math.Min(ChunkFrames, end - offset);
                meters[p].AddFrames(input.AsSpan(offset * Channels, take * Channels));
            }
        }

        return meters;
    }

    private static void Dispose(LoudnessMeter[] meters)
    {
        foreach (LoudnessMeter meter in meters)
        {
            meter.Dispose();
        }
    }

    [Fact]
    public void AggregatedResultsMatchTheReferenceExactly()
    {
        LoudnessMeter[] meters = BuildMeters();

        try
        {
            double[] expected = ReferenceData.Load("multiple_results").Values;

            Assert.Equal(0.0, expected[0]);
            Assert.Equal(0.0, expected[2]);

            BitwiseAssert.Equal(expected[1], LoudnessMeter.GatedLoudness(meters),
                "aggregated integrated loudness");
            BitwiseAssert.Equal(expected[3], LoudnessMeter.LoudnessRangeOf(meters),
                "aggregated loudness range");
        }
        finally
        {
            Dispose(meters);
        }
    }

    [Fact]
    public void IndividualResultsMatchTheReferenceExactly()
    {
        LoudnessMeter[] meters = BuildMeters();

        try
        {
            ReferenceArray expected = ReferenceData.Load("multiple_singles");
            double[] counts = ReferenceData.Load("multiple_counts").Values;

            for (int p = 0; p < Parts; ++p)
            {
                BitwiseAssert.Equal(expected.Values[p * 2], meters[p].IntegratedLoudness,
                    $"part {p} integrated");
                BitwiseAssert.Equal(expected.Values[(p * 2) + 1], meters[p].LoudnessRange,
                    $"part {p} loudness range");
                Assert.Equal((nuint)counts[p], meters[p].BlockCount);
            }
        }
        finally
        {
            Dispose(meters);
        }
    }

    [Fact]
    public void AggregateOfOneEqualsTheSingleMeter()
    {
        LoudnessMeter[] meters = BuildMeters();

        try
        {
            foreach (LoudnessMeter meter in meters)
            {
                BitwiseAssert.Equal(meter.IntegratedLoudness,
                    LoudnessMeter.GatedLoudness([meter]), "single meter aggregate");
                BitwiseAssert.Equal(meter.LoudnessRange,
                    LoudnessMeter.LoudnessRangeOf([meter]), "single meter range");
            }
        }
        finally
        {
            Dispose(meters);
        }
    }

    [Fact]
    public void MixingHistogramAndListMetersIsRejected()
    {
        using LoudnessMeter list = new(Channels, SampleRate,
            LoudnessModes.Integrated | LoudnessModes.LoudnessRange);
        using LoudnessMeter histogram = new(Channels, SampleRate,
            LoudnessModes.Integrated | LoudnessModes.LoudnessRange | LoudnessModes.Histogram);

        Assert.Throws<InvalidOperationException>(
            () => LoudnessMeter.LoudnessRangeOf([list, histogram]));
    }

    [Fact]
    public void AggregatingWithoutIntegratedModeIsRejected()
    {
        using LoudnessMeter momentary = new(Channels, SampleRate, LoudnessModes.Momentary);

        Assert.Throws<InvalidOperationException>(
            () => LoudnessMeter.GatedLoudness([momentary]));
    }

    [Fact]
    public void AggregatingSilentMetersYieldsNegativeInfinity()
    {
        using LoudnessMeter first = new(Channels, SampleRate, LoudnessModes.Integrated);
        using LoudnessMeter second = new(Channels, SampleRate, LoudnessModes.Integrated);

        double[] silence = new double[SampleRate * Channels];
        first.AddFrames(silence);
        second.AddFrames(silence);

        Assert.Equal(double.NegativeInfinity, LoudnessMeter.GatedLoudness([first, second]));
    }
}
