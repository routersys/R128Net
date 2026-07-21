namespace R128Net;

internal interface ISampleFormat<TSample>
    where TSample : unmanaged
{
    static abstract double ScalingFactor { get; }

    static abstract double ToRaw(TSample value);

    static abstract double ToUnit(TSample value);
}

internal readonly struct Int16Format : ISampleFormat<short>
{
    public static double ScalingFactor => 32768.0;

    public static double ToRaw(short value) => value;

    public static double ToUnit(short value) => value * (1.0 / 32768.0);
}

internal readonly struct Int32Format : ISampleFormat<int>
{
    public static double ScalingFactor => 2147483648.0;

    public static double ToRaw(int value) => value;

    public static double ToUnit(int value) => value * (1.0 / 2147483648.0);
}

internal readonly struct SingleFormat : ISampleFormat<float>
{
    public static double ScalingFactor => 1.0;

    public static double ToRaw(float value) => value;

    public static double ToUnit(float value) => value;
}

internal readonly struct DoubleFormat : ISampleFormat<double>
{
    public static double ScalingFactor => 1.0;

    public static double ToRaw(double value) => value;

    public static double ToUnit(double value) => value;
}
