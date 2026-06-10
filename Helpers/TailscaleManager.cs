#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace XaiNet2.Helpers
{
    // Parsed subset of `tailscale status --json`.
    public class TailscaleStatus
    {
        public string? BackendState { get; set; }
        public string[]? TailscaleIPs { get; set; }
        public TailscaleNode? Self { get; set; }
        public Dictionary<string, TailscaleNode>? Peer { get; set; }
    }

    public class TailscaleNode
    {
        public string? HostName { get; set; }
        public string? DNSName { get; set; }
        public string? OS { get; set; }
        public string[]? TailscaleIPs { get; set; }
        public bool Online { get; set; }
        public bool ExitNode { get; set; }        // currently the active exit node
        public bool ExitNodeOption { get; set; }  // offers to be an exit node
    }

    // Thin wrapper around the tailscale.exe CLI. All calls are async so the UI never blocks on a
    // subprocess. State lives in tailscaled, not here — we just query/drive it.
    public static class TailscaleManager
    {
        private static readonly string? exePath = LocateTailscale();
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };

        public static bool IsInstalled => !string.IsNullOrEmpty(exePath);

        private static string? LocateTailscale()
        {
            const string exe = "tailscale.exe";

            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (var dir in pathEnv.Split(Path.PathSeparator))
                {
                    try
                    {
                        var p = Path.Combine(dir.Trim(), exe);
                        if (File.Exists(p)) return p;
                    }
                    catch { /* ignore malformed PATH entries */ }
                }
            }

            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pfx = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string[] candidates =
            {
                Path.Combine(pf, "Tailscale", exe),
                Path.Combine(pfx, "Tailscale", exe),
                Path.Combine(pf, "Tailscale IPN", exe),
            };
            foreach (var c in candidates)
            {
                if (File.Exists(c)) return c;
            }

            return null;
        }

        private readonly struct CliResult
        {
            public CliResult(int exitCode, string stdOut, string stdErr)
            {
                ExitCode = exitCode; StdOut = stdOut; StdErr = stdErr;
            }
            public int ExitCode { get; }   // >=0 actual exit code; -2 timed-out-and-killed; -3 timed-out-left-running
            public string StdOut { get; }
            public string StdErr { get; }

            public string FailureMessage(string fallback)
            {
                if (!string.IsNullOrWhiteSpace(StdErr)) return StdErr.Trim();
                if (!string.IsNullOrWhiteSpace(StdOut)) return StdOut.Trim();
                return fallback;
            }
        }

        private static async Task<CliResult> RunAsync(int timeoutMs, bool killOnTimeout, params string[] args)
        {
            if (string.IsNullOrEmpty(exePath)) return new CliResult(-1, "", "Tailscale is not installed.");

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                foreach (var a in args) psi.ArgumentList.Add(a);

                using var proc = Process.Start(psi);
                if (proc == null) return new CliResult(-1, "", "Failed to start tailscale.");

                // Read both streams concurrently to avoid a full-buffer deadlock.
                var outTask = proc.StandardOutput.ReadToEndAsync();
                var errTask = proc.StandardError.ReadToEndAsync();

                using var cts = new CancellationTokenSource(timeoutMs);
                try
                {
                    await proc.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    if (killOnTimeout)
                    {
                        try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
                        return new CliResult(-2, "", "The command timed out.");
                    }
                    // Leave it running (e.g. `up` waiting on browser login).
                    return new CliResult(-3, "", "");
                }

                return new CliResult(proc.ExitCode, await outTask, await errTask);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Tailscale CLI failed: {ex.Message}");
                return new CliResult(-1, "", ex.Message);
            }
        }

        public static async Task<TailscaleStatus?> GetStatusAsync()
        {
            var result = await RunAsync(10_000, killOnTimeout: true, "status", "--json");
            if (string.IsNullOrWhiteSpace(result.StdOut)) return null;
            try
            {
                return JsonSerializer.Deserialize<TailscaleStatus>(result.StdOut, JsonOpts);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Tailscale status parse failed: {ex.Message}");
                return null;
            }
        }

        // The "did it work?" convention for the verbs below: return null on success, or a
        // human-readable error string on failure.

        public static async Task<string?> UpAsync()
        {
            var result = await RunAsync(25_000, killOnTimeout: false, "up");
            if (result.ExitCode == 0) return null;
            if (result.ExitCode == -3)
            {
                return "Login may be required — finish signing in via your browser, then Refresh.";
            }
            return result.FailureMessage("Failed to connect.");
        }

        public static async Task<string?> DownAsync()
        {
            var result = await RunAsync(15_000, killOnTimeout: true, "down");
            return result.ExitCode == 0 ? null : result.FailureMessage("Failed to disconnect.");
        }

        public static async Task<string?> LogoutAsync()
        {
            var result = await RunAsync(15_000, killOnTimeout: true, "logout");
            return result.ExitCode == 0 ? null : result.FailureMessage("Failed to log out.");
        }

        // Pass an empty string to clear the exit node.
        public static async Task<string?> SetExitNodeAsync(string exitNodeIp)
        {
            var result = await RunAsync(15_000, killOnTimeout: true, "set", $"--exit-node={exitNodeIp}");
            return result.ExitCode == 0 ? null : result.FailureMessage("Failed to set exit node.");
        }

        public static string FirstIPv4(string[]? ips)
            => ips?.FirstOrDefault(ip => ip.Contains('.')) ?? string.Empty;
    }
}
