namespace R128Net;

internal static unsafe class SamplePeak
{
    public static void Accumulate<TFormat, TSample>(
        TSample* source, double* peaks, int channels, int frames)
        where TFormat : struct, ISampleFormat<TSample>
        where TSample : unmanaged
    {
        for (int channel = 0; channel < channels; ++channel)
        {
            double max = 0.0;

            for (int i = 0; i < frames; ++i)
            {
                double current = TFormat.ToRaw(source[(i * channels) + channel]);
                double magnitude = current > -current ? current : -current;
                if (magnitude > max)
                {
                    max = magnitude;
                }
            }

            max = Denormal.Flush(max / TFormat.ScalingFactor);

            if (max > peaks[channel])
            {
                peaks[channel] = max;
            }
        }
    }
}
