using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace DS4WinWPF.DS4Forms.ViewModels;

internal sealed record ProgramScanProgress(string Location, int Checked, int Found);
internal sealed record ProgramScanResult(IReadOnlyList<string> Programs, int Checked, int Skipped, int Added = 0);

/// <summary>Discovers paths only. Never extracts executable icons or modifies UI collections.</summary>
internal static class ProgramScanner
{
    internal static ProgramScanResult Directory(string root, CancellationToken token,
        IProgress<ProgramScanProgress> progress = null) => Scan(new[] { root }, false, null, token, progress);

    internal static Task<ProgramScanResult> StartMenuAsync(CancellationToken token, IProgress<ProgramScanProgress> progress)
    {
        var completion = new TaskCompletionSource<ProgramScanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            object shell = null;
            try
            {
                token.ThrowIfCancellationRequested();
                shell = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("72C24DD5-D70A-438B-8A42-98424B88AFB8")));
                string Resolve(string path)
                {
                    object link = null;
                    try
                    {
                        link = ((dynamic)shell).CreateShortcut(path);
                        // Reading TargetPath does not invoke MSI resolution, repair or shortcut activation.
                        return (string)((dynamic)link).TargetPath;
                    }
                    finally { if (link != null && Marshal.IsComObject(link)) Marshal.FinalReleaseComObject(link); }
                }
                completion.TrySetResult(Scan(new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
                }, true, Resolve, token, progress));
            }
            catch (OperationCanceledException) { completion.TrySetCanceled(token); }
            catch (Exception error) { completion.TrySetException(error); }
            finally { if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell); }
        }) { IsBackground = true, Name = "Auto Profiles Start Menu discovery" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
        return completion.Task;
    }

    internal static ProgramScanResult Scan(IEnumerable<string> roots, bool shortcuts, Func<string, string> resolve,
        CancellationToken token, IProgress<ProgramScanProgress> progress = null)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int count = 0, skipped = 0;
        var clock = Stopwatch.StartNew();
        long lastReport = -100;
        string location = "";
        void Report(bool force = false)
        {
            if (force || clock.ElapsedMilliseconds - lastReport >= 100)
            {
                progress?.Report(new(location, count, paths.Count));
                lastReport = clock.ElapsedMilliseconds;
            }
        }
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true, RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            MatchCasing = MatchCasing.CaseInsensitive
        };
        foreach (string root in roots)
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(root) || !visited.Add(root)) continue;
            location = root;
            Report(true);
            try
            {
                foreach (string file in System.IO.Directory.EnumerateFiles(root, shortcuts ? "*.lnk" : "*.exe", options))
                {
                    token.ThrowIfCancellationRequested();
                    location = file;
                    count++;
                    try
                    {
                        string target = shortcuts ? resolve(file) : file;
                        if (string.IsNullOrWhiteSpace(target) || !Path.GetExtension(target).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                            skipped++;
                        // Avoid probing unavailable network shares while discovering Start Menu entries.
                        else if (shortcuts && (target.StartsWith(@"\\", StringComparison.Ordinal) || !File.Exists(target)))
                            skipped++;
                        else paths.Add(target);
                    }
                    catch (Exception error) when (error is COMException or IOException or UnauthorizedAccessException or ArgumentException)
                    { skipped++; }
                    Report();
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { skipped++; }
        }
        token.ThrowIfCancellationRequested();
        Report(true);
        var result = new List<string>(paths);
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return new(result, count, skipped);
    }
}
