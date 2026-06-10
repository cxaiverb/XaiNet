#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using XaiNet2.Properties;

namespace XaiNet2.Helpers
{
    // Simple opt-in file logger for troubleshooting. Gated by Settings.EnableLogging so nothing is
    // written unless the user turns it on. All writes are best-effort — the logger never throws.
    public static class Logger
    {
        private static readonly object gate = new();
        private const long MaxBytes = 5 * 1024 * 1024; // roll over past ~5 MB

        public static string LogDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XaiNet2", "logs");

        public static string LogFilePath { get; } = Path.Combine(LogDirectory, "xainet2.log");

        public static bool IsEnabled => Settings.Default.EnableLogging;

        public static void Info(string message) => Write("INFO", message, null);
        public static void Warn(string message) => Write("WARN", message, null);
        public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

        private static void Write(string level, string message, Exception? ex)
        {
            if (!IsEnabled) return;
            try
            {
                lock (gate)
                {
                    Directory.CreateDirectory(LogDirectory);
                    RollIfLarge();
                    using var writer = new StreamWriter(LogFilePath, append: true);
                    writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}");
                    if (ex != null)
                    {
                        writer.WriteLine(ex.ToString());
                    }
                }
            }
            catch
            {
                // Logging must never be the cause of a failure.
            }
        }

        // Writes a header with environment info — handy at the top of a troubleshooting session.
        public static void LogStartupBanner()
        {
            if (!IsEnabled) return;
            var version = Diagnostics.AppVersion();
            Info("──────────────────────────────────────────────");
            Info($"XaiNet2 {version} starting");
            Info($"OS: {Environment.OSVersion} ({(Environment.Is64BitProcess ? "x64" : "x86")} process)");
            Info($".NET: {Environment.Version}");
        }

        public static void OpenLogFolder()
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                Process.Start(new ProcessStartInfo { FileName = LogDirectory, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to open log folder: {ex.Message}");
            }
        }

        private static void RollIfLarge()
        {
            try
            {
                var info = new FileInfo(LogFilePath);
                if (info.Exists && info.Length > MaxBytes)
                {
                    var old = LogFilePath + ".old";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(LogFilePath, old);
                }
            }
            catch
            {
                // best effort
            }
        }

        private static class Diagnostics
        {
            public static string AppVersion()
            {
                try
                {
                    var asm = System.Reflection.Assembly.GetEntryAssembly();
                    return asm?.GetName().Version?.ToString() ?? "unknown";
                }
                catch
                {
                    return "unknown";
                }
            }
        }
    }
}
