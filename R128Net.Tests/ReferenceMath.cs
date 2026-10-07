using System.Runtime.InteropServices;

namespace R128Net.Tests;

internal static class ReferenceMath
{
    public static bool IsReferenceMath =>
        OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64;
}
