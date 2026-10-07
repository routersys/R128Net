using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace R128Net;

internal static class Denormal
{
    private const long MagnitudeMask = 0x7FFFFFFFFFFFFFFFL;
    private const long SmallestNormal = 0x0010000000000000L;

    private const int SingleMagnitudeMask = 0x7FFFFFFF;
    private const int SingleSmallestNormal = 0x00800000;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Flush(double value)
    {
        long bits = BitConverter.DoubleToInt64Bits(value);
        return (bits & MagnitudeMask) < SmallestNormal
            ? BitConverter.Int64BitsToDouble(bits & long.MinValue)
            : value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Flush(float value)
    {
        int bits = BitConverter.SingleToInt32Bits(value);
        return (bits & SingleMagnitudeMask) < SingleSmallestNormal
            ? BitConverter.Int32BitsToSingle(bits & int.MinValue)
            : value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<float> Flush(Vector256<float> value)
    {
        Vector256<int> bits = value.AsInt32();
        Vector256<int> tiny = Vector256.LessThan(
            bits & Vector256.Create(SingleMagnitudeMask), Vector256.Create(SingleSmallestNormal));
        return Vector256.ConditionalSelect(
            tiny, bits & Vector256.Create(int.MinValue), bits).AsSingle();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<double> Flush(Vector128<double> value)
    {
        Vector128<long> bits = value.AsInt64();
        Vector128<long> tiny = Vector128.LessThan(
            bits & Vector128.Create(MagnitudeMask), Vector128.Create(SmallestNormal));
        return Vector128.ConditionalSelect(
            tiny, bits & Vector128.Create(long.MinValue), bits).AsDouble();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<double> Flush(Vector256<double> value)
    {
        Vector256<long> bits = value.AsInt64();
        Vector256<long> tiny = Vector256.LessThan(
            bits & Vector256.Create(MagnitudeMask), Vector256.Create(SmallestNormal));
        return Vector256.ConditionalSelect(
            tiny, bits & Vector256.Create(long.MinValue), bits).AsDouble();
    }
}
