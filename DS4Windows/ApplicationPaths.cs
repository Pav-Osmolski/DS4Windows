using System;
using System.IO;

namespace DS4Windows;

internal static class ApplicationPaths
{
    internal static string PackageRoot { get; } = ResolvePackageRoot(
        AppContext.BaseDirectory, Environment.ProcessPath);

    internal static bool UsesPackagedRuntime => !string.Equals(
        AppContext.BaseDirectory, PackageRoot, StringComparison.OrdinalIgnoreCase);

    internal static string ResolvePackageRoot(string baseDirectory, string executablePath)
    {
        if (string.IsNullOrEmpty(executablePath))
            return baseDirectory;

        string executableDirectory = Path.GetDirectoryName(executablePath);
        return string.Equals(Path.TrimEndingDirectorySeparator(baseDirectory),
            Path.Combine(executableDirectory, "Runtime"), StringComparison.OrdinalIgnoreCase)
            ? executableDirectory
            : baseDirectory;
    }
}
