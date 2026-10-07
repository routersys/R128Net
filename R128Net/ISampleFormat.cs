using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace R128Net;

internal unsafe interface ISampleFormat<TSample>
    where TSample : unmanaged
{
    static abstract double ScalingFactor { get; }

    static abstract double ToRaw(TSample value);

    static abstract double ToUnit(TSample value);

    static abstract Vector256<float> LoadSingles(TSample* source);
}

internal readonly unsafe struct Int16Format : ISampleFormat<short>
{
    public static double ScalingFactor => 32768.0;

    public static double ToRaw(short value) => value;

    public static double ToUnit(short value) => value * (1.0 / 32768.0);

    public static Vector256<float> LoadSingles(short* source)
    {
        return Avx.ConvertToVector256Single(Avx2.ConvertToVector256Int32(source))
            * Vector256.Create(1.0f / 32768.0f);
    }
}

internal readonly unsafe struct Int32Format : ISampleFormat<int>
{
    public static double ScalingFactor => 2147483648.0;

    public static double ToRaw(int value) => value;

    public static double ToUnit(int value) => value * (1.0 / 2147483648.0);

    public static Vector256<float> LoadSingles(int* source)
    {
        return Avx.ConvertToVector256Single(Avx.LoadVector256(source))
            * Vector256.Create(1.0f / 2147483648.0f);
    }
}

internal readonly unsafe struct SingleFormat : ISampleFormat<float>
{
    public static double ScalingFactor => 1.0;

    public static double ToRaw(float value) => value;

    public static double ToUnit(float value) => value;

    public static Vector256<float> LoadSingles(float* source) => Avx.LoadVector256(source);
}

internal readonly unsafe struct DoubleFormat : ISampleFormat<double>
{
    public static double ScalingFactor => 1.0;

    public static double ToRaw(double value) => value;

    public static double ToUnit(double value) => value;

    public static Vector256<float> LoadSingles(double* source)
    {
        return Vector256.Create(
            Avx.ConvertToVector128Single(Avx.LoadVector256(source)),
            Avx.ConvertToVector128Single(Avx.LoadVector256(source + 4)));
    }
}
