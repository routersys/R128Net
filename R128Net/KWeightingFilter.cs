namespace R128Net;

internal static unsafe class KWeightingFilter
{
    public const int StateTaps = 4;

    public static void ProcessScalar<TFormat, TSample>(
        TSample* source,
        double* destination,
        double* state,
        ChannelPosition* channelMap,
        int channels,
        int frames,
        in KWeighting weighting)
        where TFormat : struct, ISampleFormat<TSample>
        where TSample : unmanaged
    {
        double a1 = weighting.Denominator[1];
        double a2 = weighting.Denominator[2];
        double a3 = weighting.Denominator[3];
        double a4 = weighting.Denominator[4];
        double b0 = weighting.Numerator[0];
        double b1 = weighting.Numerator[1];
        double b2 = weighting.Numerator[2];
        double b3 = weighting.Numerator[3];
        double b4 = weighting.Numerator[4];

        for (int c = 0; c < channels; ++c)
        {
            if (channelMap[c] == ChannelPosition.Unused)
            {
                continue;
            }

            double s1 = state[c];
            double s2 = state[channels + c];
            double s3 = state[(2 * channels) + c];
            double s4 = state[(3 * channels) + c];

            for (int i = 0; i < frames; ++i)
            {
                double x = Denormal.Flush(TFormat.ToUnit(source[(i * channels) + c]));

                double accumulator = Denormal.Flush(x - Denormal.Flush(a1 * s1));
                accumulator = Denormal.Flush(accumulator - Denormal.Flush(a2 * s2));
                accumulator = Denormal.Flush(accumulator - Denormal.Flush(a3 * s3));
                double v0 = Denormal.Flush(accumulator - Denormal.Flush(a4 * s4));

                double output = Denormal.Flush(
                    Denormal.Flush(b0 * v0) + Denormal.Flush(b1 * s1));
                output = Denormal.Flush(output + Denormal.Flush(b2 * s2));
                output = Denormal.Flush(output + Denormal.Flush(b3 * s3));
                output = Denormal.Flush(output + Denormal.Flush(b4 * s4));

                destination[(i * channels) + c] = output;

                s4 = s3;
                s3 = s2;
                s2 = s1;
                s1 = v0;
            }

            state[c] = s1;
            state[channels + c] = s2;
            state[(2 * channels) + c] = s3;
            state[(3 * channels) + c] = s4;
        }
    }
}
