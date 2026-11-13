using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Threading.Tasks;

namespace DS4Windows;

public static partial class ViiperSetupManager
{
    private const string UsbipComponentHostArgument = "--manage-usbip-component";

    internal static async Task<int> RunUsbipComponentActionAsync(string action)
    {
        string correlationId = Guid.NewGuid().ToString("N");
        var start = new ProcessStartInfo(Global.exelocation)
        {
            UseShellExecute = true,
            Verb = "runas",
        };
        start.ArgumentList.Add(UsbipComponentHostArgument);
        start.ArgumentList.Add(action);
        start.ArgumentList.Add(correlationId);
        using Process process = Process.Start(start) ??
            throw new InvalidOperationException("Windows could not start USB/IP management.");
        await process.WaitForExitAsync();
        if (process.ExitCode is not (0 or 3010))
        {
            string detail = ReadUsbipComponentFailure(correlationId);
            if (detail != null) throw new IOException(detail);
        }
        return process.ExitCode;
    }

    private static string ReadUsbipComponentFailure(string correlationId)
    {
        const string failurePrefix = "Setup could not finish: ";
        string marker = $"[{correlationId}] {failurePrefix}";
        try
        {
            using var stream = new FileStream(GetInfrastructureActionsLogPath(), FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                int index = line.IndexOf(marker, StringComparison.Ordinal);
                if (index >= 0) return line[(index + marker.Length)..];
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }

    internal static bool TryRunUsbipComponentHost(string[] args, out int exitCode)
    {
        exitCode = 1;
        if (args.Length == 0 || args[0] != UsbipComponentHostArgument) return false;

        string directory = null;
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            if (PortableLabContext.IsActive || !Environment.Is64BitProcess ||
                args.Length != 3 || args[1] is not ("Remove" or "Verify" or "Restore") ||
                !Guid.TryParseExact(args[2], "N", out _) ||
                !new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                throw new InvalidOperationException("USB/IP management requires an elevated x64 DS4Windows process and a valid component action.");

            string sid = identity.User.Value;
            string root = Installation.StartupSetupRecovery.EnsureProtectedStagingRoot(sid);
            directory = Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            EnsurePathDoesNotTraverseReparsePoints(directory, requireExisting: true);
            Installation.StartupSetupRecovery.ProtectSnapshot(directory, sid);
            string script = Path.Combine(directory, InstallerScriptName);
            using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(InstallerResourceName) ??
                throw new InvalidOperationException("The verified USB/IP setup script is missing."))
            using (FileStream output = new FileStream(script, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                resource.CopyTo(output);

            if (args[1] == "Restore")
            {
                string source = Path.Combine(AppContext.BaseDirectory, "extras", BundledUsbipName);
                EnsurePathDoesNotTraverseReparsePoints(source, requireExisting: true);
                File.Copy(source, Path.Combine(directory, BundledUsbipName));
            }

            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,
                "WindowsPowerShell", "v1.0", "powershell.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string argument in new[]
            {
                "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                "-File", script, "-NoPause", "-Yes", "-PackageExtrasRoot", directory,
                "-UsbipComponentAction", args[1], "-CorrelationId", args[2],
            })
                start.ArgumentList.Add(argument);
            using Process process = Process.Start(start) ??
                throw new InvalidOperationException("Windows could not start the USB/IP setup script.");
            process.WaitForExit();
            exitCode = process.ExitCode;
        }
        catch (Exception error)
        {
            WriteInstallerHostLog("USB/IP component action failed: " + error);
        }
        finally
        {
            if (directory != null)
            {
                try { Directory.Delete(directory, recursive: true); }
                catch (Exception error) { WriteInstallerHostLog("USB/IP staging cleanup failed: " + error.Message); }
            }
        }
        return true;
    }
}
