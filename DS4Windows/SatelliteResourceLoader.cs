using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Threading;

namespace DS4Windows;

internal static class SatelliteResourceLoader
{
    private static int initialized;
    [ModuleInitializer]
    internal static void Initialize()
    {
        if (Interlocked.Exchange(ref initialized, 1) == 0)
            AssemblyLoadContext.Default.Resolving += Resolve;
    }

    private static Assembly Resolve(AssemblyLoadContext context, AssemblyName requested)
    {
        string path = GetSatellitePath(AppContext.BaseDirectory, requested);
        if (path == null || !File.Exists(path)) return null;
        try
        {
            AssemblyName actual = AssemblyName.GetAssemblyName(path);
            if (!string.Equals(actual.Name, requested.Name, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(actual.CultureName, requested.CultureName, StringComparison.OrdinalIgnoreCase) ||
                (requested.Version != null && actual.Version != requested.Version) ||
                !(actual.GetPublicKeyToken() ?? Array.Empty<byte>()).SequenceEqual(
                    requested.GetPublicKeyToken() ?? Array.Empty<byte>())) return null;
            return context.LoadFromAssemblyPath(path);
        }
        catch (IOException) { return null; }
        catch (BadImageFormatException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    internal static string GetSatellitePath(string applicationDirectory, AssemblyName requested)
    {
        const string suffix = ".resources";
        string name = requested.Name;
        string culture = requested.CultureName;
        if (string.IsNullOrEmpty(name) || !name.EndsWith(suffix, StringComparison.Ordinal) ||
            name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || string.IsNullOrEmpty(culture) ||
            culture.IndexOfAny(new[] { '/', '\\', ':' }) >= 0) return null;
        try { culture = CultureInfo.GetCultureInfo(culture).Name; }
        catch (CultureNotFoundException) { return null; }
        if (culture.Length == 0) return null;
        string parent = name.Substring(0, name.Length - suffix.Length);
        // Only resolve satellites for assemblies belonging to this application.
        if (!File.Exists(Path.Combine(applicationDirectory, parent + ".dll"))) return null;
        return Path.GetFullPath(Path.Combine(applicationDirectory, "Lang", culture, name + ".dll"));
    }
}
