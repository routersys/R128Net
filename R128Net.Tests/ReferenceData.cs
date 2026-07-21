namespace R128Net.Tests;

internal sealed class ReferenceArray
{
    public ReferenceArray(int[] dimensions, double[] values)
    {
        Dimensions = dimensions;
        Values = values;
    }

    public int[] Dimensions { get; }

    public double[] Values { get; }

    public int Rows => Dimensions[0];

    public int Columns => Dimensions.Length > 1 ? Dimensions[1] : 1;
}

internal static class ReferenceData
{
    private static readonly Lazy<string?> DirectoryPath = new(FindDirectory);

    public static bool IsAvailable => DirectoryPath.Value is not null;

    public static string DataDirectory => DirectoryPath.Value
        ?? throw new InvalidOperationException(
            "Reference data was not found. Run reference/build.bat to generate it.");

    public static ReferenceArray Load(string name)
    {
        string path = Path.Combine(DataDirectory, name + ".bin");
        using FileStream stream = File.OpenRead(path);
        using BinaryReader reader = new(stream);

        int rank = reader.ReadInt32();
        int[] dimensions = new int[rank];
        long total = 1;
        for (int i = 0; i < rank; ++i)
        {
            dimensions[i] = reader.ReadInt32();
            total *= dimensions[i];
        }

        double[] values = new double[total];
        for (long i = 0; i < total; ++i)
        {
            values[i] = reader.ReadDouble();
        }

        return new ReferenceArray(dimensions, values);
    }

    private static string? FindDirectory()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(current.FullName, "reference", "data");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "meta.bin")))
            {
                return candidate;
            }
            current = current.Parent;
        }

        return null;
    }
}
