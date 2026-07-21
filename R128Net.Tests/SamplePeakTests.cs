namespace R128Net.Tests;

public class SamplePeakTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(8)]
    public unsafe void VectorisedPathAgreesWithTheScalarPathBitForBit(int channels)
    {
        foreach (int frames in new[] { 1, 2, 3, 7, 64, 1000, 4801 })
        {
            double[] input = new double[frames * channels];
            uint seed = 88675123u;
            for (int i = 0; i < input.Length; ++i)
            {
                seed ^= seed << 13;
                seed ^= seed >> 17;
                seed ^= seed << 5;
                input[i] = ((seed / 4294967296.0) * 2.0) - 1.0;
            }

            input[0] = 0.0;
            if (input.Length > 1)
            {
                input[1] = -0.0;
            }

            double[] vectorised = new double[channels];
            double[] scalar = new double[channels];

            fixed (double* source = input)
            fixed (double* first = vectorised)
            fixed (double* second = scalar)
            {
                SamplePeak.Accumulate<DoubleFormat, double>(source, first, channels, frames);
                SamplePeak.AccumulateScalar<DoubleFormat, double>(source, second, channels, frames);
            }

            BitwiseAssert.Equal(scalar, vectorised, $"{channels} channels, {frames} frames");
        }
    }

    [Fact]
    public unsafe void NotANumberIsIgnoredExactlyAsTheReferenceDoes()
    {
        const int Channels = 2;
        double[] input = [double.NaN, 0.5, 0.25, double.NaN, -0.75, 0.125];

        double[] vectorised = new double[Channels];
        double[] scalar = new double[Channels];

        fixed (double* source = input)
        fixed (double* first = vectorised)
        fixed (double* second = scalar)
        {
            SamplePeak.Accumulate<DoubleFormat, double>(source, first, Channels, 3);
            SamplePeak.AccumulateScalar<DoubleFormat, double>(source, second, Channels, 3);
        }

        BitwiseAssert.Equal(scalar, vectorised, "peaks with not a number present");
        BitwiseAssert.Equal(0.75, vectorised[0], "channel 0 peak");
        BitwiseAssert.Equal(0.5, vectorised[1], "channel 1 peak");
    }
}
