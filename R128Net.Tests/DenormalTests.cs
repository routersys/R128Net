namespace R128Net.Tests;

public class DenormalTests
{
    private static float ReferenceFlush(float value)
    {
        int bits = BitConverter.SingleToInt32Bits(value);
        return (bits & 0x7FFFFFFF) < 0x00800000
            ? BitConverter.Int32BitsToSingle(bits & int.MinValue)
            : value;
    }

    private static double ReferenceFlush(double value)
    {
        long bits = BitConverter.DoubleToInt64Bits(value);
        return (bits & 0x7FFFFFFFFFFFFFFFL) < 0x0010000000000000L
            ? BitConverter.Int64BitsToDouble(bits & long.MinValue)
            : value;
    }

    [Fact]
    public void SingleFlushMatchesTheDefinitionOnEveryBitPattern()
    {
        Parallel.For(0, 256, block =>
        {
            uint begin = (uint)block << 24;
            for (uint offset = 0; offset < (1u << 24); ++offset)
            {
                float value = BitConverter.UInt32BitsToSingle(begin + offset);
                int expected = BitConverter.SingleToInt32Bits(ReferenceFlush(value));
                int actual = BitConverter.SingleToInt32Bits(Denormal.Flush(value));
                if (expected != actual)
                {
                    Assert.Fail($"bits {begin + offset:X8}: expected {expected:X8} but was {actual:X8}");
                }
            }
        });
    }

    [Fact]
    public void DoubleFlushMatchesTheDefinitionAtTheBoundariesAndOnRandomPatterns()
    {
        List<long> patterns = [];
        foreach (long sign in new[] { 0L, long.MinValue })
        {
            foreach (long magnitude in new[]
            {
                0L, 1L, 2L, 0x000FFFFFFFFFFFFEL, 0x000FFFFFFFFFFFFFL, 0x0010000000000000L,
                0x0010000000000001L, 0x7FEFFFFFFFFFFFFFL, 0x7FF0000000000000L,
                0x7FF0000000000001L, 0x7FF8000000000000L, 0x7FFFFFFFFFFFFFFFL,
            })
            {
                patterns.Add(sign | magnitude);
            }
        }

        ulong state = 0x9E3779B97F4A7C15UL;
        for (int i = 0; i < 20000000; ++i)
        {
            state ^= state << 13;
            state ^= state >> 7;
            state ^= state << 17;
            long bits = (long)state;
            patterns.Add(i % 3 == 0 ? bits & unchecked((long)0x800FFFFFFFFFFFFFUL) : bits);
        }

        foreach (long bits in patterns)
        {
            double value = BitConverter.Int64BitsToDouble(bits);
            long expected = BitConverter.DoubleToInt64Bits(ReferenceFlush(value));
            long actual = BitConverter.DoubleToInt64Bits(Denormal.Flush(value));
            if (expected != actual)
            {
                Assert.Fail($"bits {bits:X16}: expected {expected:X16} but was {actual:X16}");
            }
        }
    }
}
