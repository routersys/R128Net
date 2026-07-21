namespace R128Net;

internal struct KWeighting
{
    public FilterTaps Numerator;
    public FilterTaps Denominator;
    public bool IsStable;

    private static bool IsSectionStable(double c1, double c2)
    {
        return 1.0 + c1 + c2 > 0.0 && 1.0 - c1 + c2 > 0.0 && c2 < 1.0;
    }

    public static KWeighting Create(double sampleRate)
    {
        double f0 = 1681.974450955533;
        double g = 3.999843853973347;
        double q = 0.7071752369554196;

        double k = Math.Tan(Math.PI * f0 / sampleRate);
        double vh = Math.Pow(10.0, g / 20.0);
        double vb = Math.Pow(vh, 0.4996667741545416);

        Span<double> pb = stackalloc double[3] { 0.0, 0.0, 0.0 };
        Span<double> pa = stackalloc double[3] { 1.0, 0.0, 0.0 };
        ReadOnlySpan<double> rb = [1.0, -2.0, 1.0];
        Span<double> ra = stackalloc double[3] { 1.0, 0.0, 0.0 };

        double a0 = 1.0 + (k / q) + (k * k);
        pb[0] = (vh + (vb * k / q) + (k * k)) / a0;
        pb[1] = 2.0 * ((k * k) - vh) / a0;
        pb[2] = (vh - (vb * k / q) + (k * k)) / a0;
        pa[1] = 2.0 * ((k * k) - 1.0) / a0;
        pa[2] = (1.0 - (k / q) + (k * k)) / a0;

        f0 = 38.13547087602444;
        q = 0.5003270373238773;
        k = Math.Tan(Math.PI * f0 / sampleRate);

        ra[1] = 2.0 * ((k * k) - 1.0) / (1.0 + (k / q) + (k * k));
        ra[2] = (1.0 - (k / q) + (k * k)) / (1.0 + (k / q) + (k * k));

        KWeighting result = default;
        result.IsStable = IsSectionStable(pa[1], pa[2]) && IsSectionStable(ra[1], ra[2]);

        result.Numerator[0] = pb[0] * rb[0];
        result.Numerator[1] = (pb[0] * rb[1]) + (pb[1] * rb[0]);
        result.Numerator[2] = (pb[0] * rb[2]) + (pb[1] * rb[1]) + (pb[2] * rb[0]);
        result.Numerator[3] = (pb[1] * rb[2]) + (pb[2] * rb[1]);
        result.Numerator[4] = pb[2] * rb[2];

        result.Denominator[0] = pa[0] * ra[0];
        result.Denominator[1] = (pa[0] * ra[1]) + (pa[1] * ra[0]);
        result.Denominator[2] = (pa[0] * ra[2]) + (pa[1] * ra[1]) + (pa[2] * ra[0]);
        result.Denominator[3] = (pa[1] * ra[2]) + (pa[2] * ra[1]);
        result.Denominator[4] = pa[2] * ra[2];

        return result;
    }
}
