using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DS4Windows;

// Preview-only, read-only authenticated pings. Dispatched before normal settings,
// single-instance signalling, controller discovery or any maintenance helpers.
internal static class ViiperTransportProbeHelper
{
    internal static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (Array.IndexOf(args, "--issue66-transport-probe") < 0) return false;
        try
        {
            if (args.Length != 3 || args[0] != "--issue66-transport-probe" ||
                !int.TryParse(args[1], out int port) || port < 1 || port > 65535)
                throw new ArgumentException("Invalid diagnostic invocation.");
            PortableLabContext.Initialize(new[] { "--portable-lab", ViiperSetupManager.SupportedViiperSha256 }, args[2]);
            var rows = new List<object>();
            for (int i = 0; i < 20; i++)
            {
                string trace = null;
                var started = DateTimeOffset.UtcNow;
                bool ok = ViiperSetupManager.ProbeServer("127.0.0.1", port, true, out _, 1000,
                    line => trace = line);
                rows.Add(new { index = i + 1, startedUtc = started, finishedUtc = DateTimeOffset.UtcNow,
                    processId = Environment.ProcessId, success = ok, trace });
            }
            File.WriteAllText(Path.Combine(PortableLabContext.Current.DataPath, "probe-results.json"),
                JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { exitCode = 1; }
        finally { PortableLabContext.Current?.Dispose(); }
        return true;
    }
}
