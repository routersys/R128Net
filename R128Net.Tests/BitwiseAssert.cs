namespace R128Net.Tests;

internal static class BitwiseAssert
{
    public static void Equal(double expected, double actual, string context)
    {
        long a = BitConverter.DoubleToInt64Bits(expected);
        long b = BitConverter.DoubleToInt64Bits(actual);
        if (a != b)
        {
            Assert.Fail(
                $"{context}: expected {expected:E17} but was {actual:E17} "
                + $"({Math.Abs(a - b)} ulp)");
        }
    }

    public static void Equal(double expected, double actual, string context,
        long foreignMathUlps)
    {
        if (ReferenceMath.IsReferenceMath)
        {
            Equal(expected, actual, context);
            return;
        }

        long distance = UlpDistance(expected, actual);
        if (distance > foreignMathUlps)
        {
            Assert.Fail(
                $"{context}: expected {expected:E17} but was {actual:E17} "
                + $"({distance} ulp, more than the {foreignMathUlps} ulp allowed for a math library "
                + "other than the reference one)");
        }
    }

    public static void Equal(ReadOnlySpan<double> expected, ReadOnlySpan<double> actual,
        string context)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; ++i)
        {
            Equal(expected[i], actual[i], $"{context} index {i}");
        }
    }

    public static long UlpDistance(double expected, double actual)
    {
        long a = BitConverter.DoubleToInt64Bits(expected);
        long b = BitConverter.DoubleToInt64Bits(actual);
        return Math.Abs(a - b);
    }
}
