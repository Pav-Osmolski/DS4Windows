using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DS4Windows
{
    // Diagnostic-preview tool: authenticated pings only. It never creates a
    // controller, starts a backend, repairs a driver or changes a preference.
    internal static class ViiperConnectionTest
    {
        internal const string ProbeName = "VIIPER-API-PROBE.exe";
        internal const string ProbeSha256 = "69CF2143E18FA1F8060FA32C19AFAF9CCC2EA64BBEDAA22D2BBAA67A8FE96DD1";
        internal const string CollectorSha256 = "9892A076077F3EEBFAC5F41D6342720637F20C3EDD407731A7CB30A04A2E5F16";
        internal const int Attempts = 20;

        internal static async Task<string> RunAsync(string logDirectory, Action<string> record,
            CancellationToken token)
        {
            string probe = Path.Combine(AppContext.BaseDirectory, "extras", ProbeName);
            using (var file = File.OpenRead(probe))
                if ((File.GetAttributes(probe) & FileAttributes.ReparsePoint) != 0 ||
                    !Convert.ToHexString(SHA256.HashData(file)).Equals(ProbeSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The matching connection-test helper is missing or changed. Extract the complete preview ZIP again.");

            record("VIIPER connection test: starting 20 DS4Windows and 20 independent Go authenticated pings; no device or driver changes.");
            int clientSuccess = await Task.Run(() => RunClient(() =>
                ViiperSetupManager.ProbeServer("127.0.0.1", 3242, true, out _,
                    totalTimeoutMilliseconds: 1000, managementDiagnostic: record), token), token);

            var start = new ProcessStartInfo(probe)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            start.ArgumentList.Add("--key-file");
            start.ArgumentList.Add(ViiperAuthentication.DefaultKeyFilePath);
            start.ArgumentList.Add("--count");
            start.ArgumentList.Add(Attempts.ToString());
            using var process = new Process { StartInfo = start };
            if (!process.Start()) throw new IOException("Could not start the matching connection-test helper.");
            try
            {
                Task<string> stdout = process.StandardOutput.ReadToEndAsync(token);
                Task<string> stderr = process.StandardError.ReadToEndAsync(token);
                await process.WaitForExitAsync(token);
                string output = await stdout;
                _ = await stderr; // Never log arbitrary helper/configuration text.
                if (process.ExitCode != 0) throw new IOException("The independent connection test did not complete.");
                List<ProbeAttempt> attempts = ParseProbeResult(output);
                int goSuccess = 0;
                foreach (var attempt in attempts)
                {
                    if (attempt.Success) goSuccess++;
                    record($"VIIPER independent connection test: phase={attempt.Phase} success={attempt.Success} localPort={attempt.LocalPort} socketError={attempt.SocketError} elapsedMs={attempt.ElapsedMs}");
                }
                Directory.CreateDirectory(logDirectory);
                await File.WriteAllTextAsync(Path.Combine(logDirectory, "viiper-connection-test.json"),
                    JsonSerializer.Serialize(new { ds4Windows = new { attempted = Attempts, successful = clientSuccess },
                        independentGo = new { attempted = attempts.Count, successful = goSuccess, attempts } },
                        new JsonSerializerOptions { WriteIndented = true }), token);
                string summary = $"Connection test: DS4Windows {clientSuccess}/{Attempts}; independent Go {goSuccess}/{Attempts}. Use Open log folder to share the results and both logs.";
                record(summary);
                return summary;
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
        }

        internal static int RunClient(Func<bool> probe, CancellationToken token)
        {
            int successful = 0;
            for (int i = 0; i < Attempts; i++)
            {
                token.ThrowIfCancellationRequested();
                if (probe()) successful++;
            }
            return successful;
        }

        internal sealed record ProbeAttempt(string Phase, bool Success, int LocalPort, int SocketError, long ElapsedMs);

        internal static List<ProbeAttempt> ParseProbeResult(string output)
        {
            if (output.Length > 16384) throw new IOException("Unexpected connection-test result size.");
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;
            if (root.GetProperty("implementation").GetString() != "go-independent" ||
                root.GetProperty("setupFailed").GetBoolean() ||
                root.GetProperty("attempted").GetInt32() != Attempts)
                throw new IOException("The independent test could not read its existing authentication key or finish all requests.");
            var results = new List<ProbeAttempt>();
            foreach (var item in root.GetProperty("attempts").EnumerateArray())
            {
                string phase = item.GetProperty("phase").GetString();
                int port = item.GetProperty("localPort").GetInt32();
                int error = item.GetProperty("socketError").GetInt32();
                long elapsed = item.GetProperty("elapsedMs").GetInt64();
                if (phase is not ("Connect" or "Authenticate" or "WritePing" or "ReadReply") ||
                    port < 0 || port > 65535 || error < 0 || elapsed < 0 || elapsed > 60000)
                    throw new IOException("Unexpected connection-test metadata.");
                results.Add(new ProbeAttempt(phase, item.GetProperty("success").GetBoolean(), port, error, elapsed));
            }
            if (results.Count != Attempts) throw new IOException("Incomplete connection-test results.");
            return results;
        }
    }
}
