namespace R128Net.Tests;

internal sealed unsafe class FlushingOracle
{
    private const int Factor = 4;

    private readonly int _channels;
    private readonly int _delay;
    private readonly int[] _counts;
    private readonly int[] _indices;
    private readonly double[] _coefficients;
    private readonly double[][] _history;
    private int _position;

    public FlushingOracle(Interpolator interpolator, int channels)
    {
        _channels = channels;
        _delay = interpolator.Delay;
        _counts = new int[Factor];
        _indices = new int[Factor * _delay];
        _coefficients = new double[Factor * _delay];
        _history = new double[channels][];

        for (int f = 0; f < Factor; ++f)
        {
            _counts[f] = interpolator.Counts[f];
        }

        for (int i = 0; i < Factor * _delay; ++i)
        {
            _indices[i] = interpolator.Indices[i];
            _coefficients[i] = interpolator.Coefficients[i];
        }

        for (int c = 0; c < channels; ++c)
        {
            _history[c] = new double[_delay];
        }
    }

    public void Accumulate(double[] input, int offset, int frames, double[] peaks)
    {
        for (int frame = 0; frame < frames; ++frame)
        {
            for (int channel = 0; channel < _channels; ++channel)
            {
                double[] line = _history[channel];
                line[_position] = Denormal.Flush((float)Denormal.Flush(
                    input[((offset + frame) * _channels) + channel]));

                double peak = peaks[channel];

                for (int f = 0; f < Factor; ++f)
                {
                    double accumulator = 0.0;

                    for (int t = 0; t < _counts[f]; ++t)
                    {
                        int i = _position - _indices[(f * _delay) + t];
                        if (i < 0)
                        {
                            i += _delay;
                        }

                        accumulator = Denormal.Flush(accumulator
                            + Denormal.Flush(line[i] * _coefficients[(f * _delay) + t]));
                    }

                    double value = Denormal.Flush((float)accumulator);
                    double magnitude = value > -value ? value : -value;
                    if (magnitude > peak)
                    {
                        peak = magnitude;
                    }
                }

                peaks[channel] = peak;
            }

            ++_position;
            if (_position == _delay)
            {
                _position = 0;
            }
        }
    }
}
