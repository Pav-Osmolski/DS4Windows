using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace DS4Windows;

internal static class PortableRuntimeCleanup
{
    // Versioned DAC aliases from the Microsoft.NETCore.App.Runtime.win-x64
    // packages used before and during the move to Runtime. The filename
    // changes with each runtime build, so these may have no same-name copy.
    private static readonly Dictionary<string, string> LegacyDiagnosticsHashes = new(StringComparer.OrdinalIgnoreCase)
    {
        // .NET 8.0.31 and 8.0.32
        ["mscordaccore_amd64_amd64_8.0.3126.42015.dll"] = "A277F63F5722E1ED59A791FC5D30C53ED263ABEF4ADF12D0BF494897D796C3E0",
        ["mscordaccore_amd64_amd64_8.0.3226.50405.dll"] = "6298650DD58F53623B9DEE84FFE939A4CF7C927AD6EC61D3F734F187B4D9D4D3",
        // .NET 10.0.13, 10.0.14 and 10.0.15
        ["mscordaccore_amd64_amd64_10.0.1326.50504.dll"] = "4E2109C88298F0A614AAC82B23310B13835673A8E192A6A8B75C25459E591169",
        ["mscordaccore_amd64_amd64_10.0.1426.52326.dll"] = "4A9B277D882A52F19B89047748E6AB1043C1582F834B20423C8C7F16A76B6EA3",
        ["mscordaccore_amd64_amd64_10.0.1526.53008.dll"] = "3EB536B8BD054C0B4A19332D25FFC90787F307B0367BAE4DC222417D8107D247",
    };

    internal static int RemoveLeftovers(string packageRoot, string assemblyDirectory)
    {
        string runtime = Path.Combine(packageRoot, "Runtime");
        if (!string.Equals(Path.TrimEndingDirectorySeparator(assemblyDirectory), runtime,
                StringComparison.OrdinalIgnoreCase))
            return 0;

        string manifest = Path.Combine(packageRoot, ".ds4windows-managed-files.txt");
        PortableLabContext.ValidateNoReparsePoints(manifest);
        PortableLabContext.ValidateNoReparsePoints(runtime);
        if (!File.Exists(manifest) || new FileInfo(manifest).Length > 1024 * 1024)
            return 0;

        var owned = new HashSet<string>(File.ReadLines(manifest).Select(line => line.Replace('\\', '/')),
            StringComparer.OrdinalIgnoreCase);
        string diagnosticsDll = Path.Combine(runtime, "mscordaccore.dll");
        int removed = 0;
        foreach (string candidate in Directory.EnumerateFiles(packageRoot))
        {
            string name = Path.GetFileName(candidate);
            if ((!name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals("createdump.exe", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals("DS4Windows.deps.json", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals("DS4Windows.runtimeconfig.json", StringComparison.OrdinalIgnoreCase)) ||
                owned.Contains(name))
                continue;

            string packagedFile;
            string expectedSha256 = null;
            if (owned.Contains("Runtime/" + name))
                packagedFile = Path.Combine(runtime, name);
            else if (owned.Contains("Runtime/mscordaccore.dll") &&
                LegacyDiagnosticsHashes.TryGetValue(name, out expectedSha256))
                packagedFile = diagnosticsDll;
            else
                continue;

            try
            {
                if (TryRemoveFile(candidate, packagedFile, expectedSha256))
                    removed++;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Another program can hold a file open. A later launch can retry it.
            }
        }
        return removed;
    }

    private static bool TryRemoveFile(string candidate, string packagedFile, string expectedSha256)
    {
        PortableLabContext.ValidateNoReparsePoints(packagedFile);
        using var packaged = File.Open(packagedFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var handle = VerifiedFileDeletion.OpenExclusive(candidate, readContents: true);
        if (!VerifiedFileDeletion.TryGetInfo(handle, out long length, out _) ||
            (expectedSha256 == null && length != packaged.Length))
            return false;
        using var leftover = new FileStream(handle, FileAccess.Read);
        byte[] expectedHash = expectedSha256 == null
            ? SHA256.HashData(packaged)
            : Convert.FromHexString(expectedSha256);
        return CryptographicOperations.FixedTimeEquals(expectedHash, SHA256.HashData(leftover)) &&
            VerifiedFileDeletion.MarkForDeletion(handle);
    }
}
