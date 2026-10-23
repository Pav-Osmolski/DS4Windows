using System;
using System.Diagnostics;
using System.IO;
using DS4WinWPF;

namespace DS4Windows;

internal static class ViiperDiagnostics
{
    internal const string FileName = "viiper-debug.log";
    internal const string PreviousFileName = "viiper-debug-previous.log";
    internal static string LogPath => Path.Combine(LoggerHolder.ResolveLogDirectory(), FileName);

    internal static void AddArguments(ProcessStartInfo start, bool enabled, string logPath)
    {
        start.ArgumentList.Add(enabled ? "--log.level=debug" : "--log.level=warn");
        start.ArgumentList.Add(enabled ? "--log.file=" + Path.GetFullPath(logPath) : "--log.file=");
        // Diagnostic text is sufficient; never enable raw controller packet capture.
        start.ArgumentList.Add("--log.raw-file=");
    }

    internal static void PrepareLogFile(string logPath)
    {
        string full = Path.GetFullPath(logPath);
        string directory = Path.GetDirectoryName(full);
        string previous = Path.Combine(directory, PreviousFileName);
        PortableLabContext.ValidateNoReparsePoints(full);
        PortableLabContext.ValidateNoReparsePoints(previous);
        Directory.CreateDirectory(directory);
        // VIIPER truncates its destination at startup. Preserve the preceding
        // capture, but only when starting a new, verified backend ourselves.
        if (File.Exists(full)) File.Move(full, previous, overwrite: true);
    }
}
