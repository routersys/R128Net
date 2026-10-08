using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;

public static partial class Demo
{
    private static readonly Session Current = new();
    private static double s_milliseconds;

    [JSExport]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    public static double[] LoadWave(byte[] wave)
    {
        long start = Stopwatch.GetTimestamp();
        double[] info = Current.LoadWave(wave);
        Stamp(start);
        return info;
    }

    [JSExport]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    public static double[] LoadSamples(byte[] interleaved, int channels, int rate)
    {
        long start = Stopwatch.GetTimestamp();
        double[] info = Current.LoadSamples(interleaved, channels, rate);
        Stamp(start);
        return info;
    }

    [JSExport]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    public static double[] Generate(int kind)
    {
        long start = Stopwatch.GetTimestamp();
        double[] info = Current.Generate(kind);
        Stamp(start);
        return info;
    }

    [JSExport]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    public static double[] Measure()
    {
        long start = Stopwatch.GetTimestamp();
        double[] report = Current.Measure();
        Stamp(start);
        return report;
    }

    [JSExport]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    public static double[] GetTimeline()
    {
        return Current.GetTimeline();
    }

    [JSExport]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    public static double[] TimeModes()
    {
        long start = Stopwatch.GetTimestamp();
        double[] times = Current.TimeModes();
        Stamp(start);
        return times;
    }

    [JSExport]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    public static double[] CheckAllocation()
    {
        long start = Stopwatch.GetTimestamp();
        double[] bytes = Current.CheckAllocation();
        Stamp(start);
        return bytes;
    }

    [JSExport]
    public static byte[] RenderOriginal()
    {
        long start = Stopwatch.GetTimestamp();
        byte[] wave = Current.RenderOriginal();
        Stamp(start);
        return wave;
    }

    [JSExport]
    public static byte[] Render(double gainDb)
    {
        long start = Stopwatch.GetTimestamp();
        byte[] wave = Current.Render(gainDb);
        Stamp(start);
        return wave;
    }

    [JSExport]
    [return: JSMarshalAs<JSType.Array<JSType.Number>>]
    public static double[] GetRenderReport()
    {
        return Current.GetRenderReport();
    }

    [JSExport]
    public static double GetMilliseconds()
    {
        return s_milliseconds;
    }

    public static void Main()
    {
    }

    private static void Stamp(long start)
    {
        s_milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
}
