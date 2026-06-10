#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using XaiNet2.Properties;

namespace XaiNet2.Helpers
{
    public class OpenVpnProfile
    {
        public string Name { get; set; } = string.Empty;
        public string ConfigPath { get; set; } = string.Empty;
        public string? AutoConnectNetwork { get; set; }
    }

    public static class OpenVPNManager
    {
        private static readonly string ProfileStorePath = Path.Combine(AppContext.BaseDirectory, "openvpn_profiles.json");
        private static string configDirectory;
        private static string logDirectory;
        private static readonly List<OpenVpnProfile> profiles;
        private static readonly HashSet<string> activeConnections = new();
        private static readonly string? openVpnGuiExecutable = LocateOpenVpnGui();
        // Guards `profiles` and `activeConnections`; both are read/written from UI handlers
        // and from the network-change callback on a background thread.
        private static readonly object stateLock = new();
        static OpenVPNManager()
        {
            if (openVpnGuiExecutable != null)
            {
                configDirectory = string.IsNullOrWhiteSpace(Settings.Default.OpenVpnConfigDir)
                    ? GetDefaultConfigDir()
                    : Settings.Default.OpenVpnConfigDir;
                logDirectory = string.IsNullOrWhiteSpace(Settings.Default.OpenVpnLogDir)
                    ? GetDefaultLogDir()
                    : Settings.Default.OpenVpnLogDir;

                try
                {
                    Directory.CreateDirectory(configDirectory);
                    Directory.CreateDirectory(logDirectory);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error creating OpenVPN directories: {ex.Message}");
                }

                profiles = LoadProfilesInternal();
            }
            else
            {
                configDirectory = Path.Combine(AppContext.BaseDirectory, "openvpn_configs");
                logDirectory = Path.Combine(AppContext.BaseDirectory, "openvpn_logs");
                profiles = new List<OpenVpnProfile>();
            }
        }

        public static bool IsInstalled => !string.IsNullOrEmpty(openVpnGuiExecutable);
        private static string? LocateOpenVpnGui()
        {
            string[] exeNames = Environment.OSVersion.Platform == PlatformID.Win32NT
                ? new[] { "openvpn-gui.exe" }
                : new[] { "openvpn-gui" };

            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (var dir in pathEnv.Split(Path.PathSeparator))
                {
                    foreach (var exe in exeNames)
                    {
                        try
                        {
                            var fullPath = Path.Combine(dir.Trim(), exe);
                            if (File.Exists(fullPath))
                            {
                                return fullPath;
                            }
                        }
                        catch
                        {
                            // ignore invalid path entries
                        }
                    }
                }
            }

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                string[] additionalPaths =
                {
                    Path.Combine(programFiles, "OpenVPN", "bin", "openvpn-gui.exe"),
                    Path.Combine(programFiles, "OpenVPN", "OpenVPN", "bin", "openvpn-gui.exe"),
                    Path.Combine(programFilesX86, "OpenVPN", "bin", "openvpn-gui.exe"),
                    Path.Combine(programFilesX86, "OpenVPN", "OpenVPN", "bin", "openvpn-gui.exe"),
                };
                foreach (var p in additionalPaths)
                {
                    if (File.Exists(p))
                    {
                        return p;
                    }
                }
            }

            return null;
        }

        private static List<OpenVpnProfile> LoadProfilesInternal()
        {
            var loadedProfiles = new List<OpenVpnProfile>();
            try
            {
                if (File.Exists(ProfileStorePath))
                {
                    var json = File.ReadAllText(ProfileStorePath);
                    var loaded = JsonSerializer.Deserialize<List<OpenVpnProfile>>(json);
                    if (loaded != null)
                    {
                        loadedProfiles = loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading VPN profiles: {ex.Message}");
            }

            // Migrate legacy entries whose ConfigPath pointed at a directory (an older build stored the
            // per-config folder, not the .ovpn file) to the actual file, so Connect's File.Exists works
            // and the disk scan below dedupes against it instead of adding a duplicate.
            foreach (var p in loadedProfiles)
            {
                if (!string.IsNullOrEmpty(p.ConfigPath) && !File.Exists(p.ConfigPath) && Directory.Exists(p.ConfigPath))
                {
                    try
                    {
                        var ovpn = Directory.GetFiles(p.ConfigPath, "*.ovpn", SearchOption.AllDirectories).FirstOrDefault();
                        if (ovpn != null) p.ConfigPath = ovpn;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Profile path migration failed: {ex.Message}");
                    }
                }
            }

            try
            {
                if (Directory.Exists(configDirectory))
                {
                    foreach (var file in Directory.GetFiles(configDirectory, "*.ovpn", SearchOption.AllDirectories))
                    {
                        if (!loadedProfiles.Any(p => string.Equals(p.ConfigPath, file, StringComparison.OrdinalIgnoreCase)))
                        {
                            loadedProfiles.Add(new OpenVpnProfile
                            {
                                Name = Path.GetFileNameWithoutExtension(file),
                                ConfigPath = file
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading existing OpenVPN configs: {ex.Message}");
            }

            return loadedProfiles;
        }

        // Caller must hold stateLock.
        private static void SaveProfilesNoLock()
        {
            try
            {
                var json = JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ProfileStorePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving VPN profiles: {ex.Message}");
            }
        }

        public static IEnumerable<OpenVpnProfile> GetProfiles()
        {
            lock (stateLock)
            {
                return profiles.ToArray();
            }
        }

        // Copies an .ovpn into the config directory (flat — no per-config subfolder) so the
        // openvpn-gui "config name" is simply the file name without extension. Returns the new
        // profile, or null on failure.
        public static OpenVpnProfile? AddProfile(string sourcePath)
        {
            try
            {
                if (!File.Exists(sourcePath))
                {
                    Debug.WriteLine($"AddProfile: source file not found: {sourcePath}");
                    return null;
                }

                Directory.CreateDirectory(configDirectory);
                string baseName = Path.GetFileNameWithoutExtension(sourcePath);
                if (string.IsNullOrWhiteSpace(baseName)) baseName = "profile";

                lock (stateLock)
                {
                    // Pick a name not already taken by a profile or by a file on disk.
                    string uniqueName = baseName;
                    int nameSuffix = 1;
                    while (profiles.Any(p => string.Equals(p.Name, uniqueName, StringComparison.OrdinalIgnoreCase))
                           || File.Exists(Path.Combine(configDirectory, uniqueName + ".ovpn")))
                    {
                        uniqueName = $"{baseName}_{nameSuffix++}";
                    }

                    string destFile = Path.Combine(configDirectory, uniqueName + ".ovpn");
                    File.Copy(sourcePath, destFile, overwrite: false);

                    var profile = new OpenVpnProfile { Name = uniqueName, ConfigPath = destFile };
                    profiles.Add(profile);
                    SaveProfilesNoLock();
                    return profile;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding VPN profile: {ex.Message}");
                return null;
            }
        }

        // openvpn-gui identifies a config by its path relative to the config directory, without the
        // .ovpn extension. New profiles are stored flat (name.ovpn -> "name"); profiles discovered
        // from a legacy subfolder layout resolve to "subfolder\name". Connect and Disconnect MUST
        // pass the same identifier, so both route through here.
        private static string GetConfigName(OpenVpnProfile profile)
        {
            try
            {
                var relative = Path.GetRelativePath(configDirectory, profile.ConfigPath);
                return Path.ChangeExtension(relative, null);
            }
            catch
            {
                return profile.Name;
            }
        }

        private static bool PathsEqual(string a, string b)
        {
            try
            {
                return string.Equals(
                    Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static void RemoveProfile(string name)
        {
            OpenVpnProfile? profile;
            lock (stateLock)
            {
                profile = profiles.FirstOrDefault(p => p.Name == name);
            }
            if (profile == null) return;

            // Best-effort: always tell openvpn-gui to disconnect before deleting the config, even if we
            // didn't start it (it may be up from a direct openvpn-gui session, not tracked here).
            IssueDisconnect(profile);
            lock (stateLock)
            {
                activeConnections.Remove(name);
            }

            lock (stateLock)
            {
                profiles.Remove(profile);
                try
                {
                    if (File.Exists(profile.ConfigPath))
                    {
                        File.Delete(profile.ConfigPath);
                        var dir = Path.GetDirectoryName(profile.ConfigPath);
                        // Clean up an empty *sub*folder (legacy layout) only — never the config root.
                        if (!string.IsNullOrEmpty(dir)
                            && !PathsEqual(dir, configDirectory)
                            && Directory.Exists(dir)
                            && !Directory.EnumerateFileSystemEntries(dir).Any())
                        {
                            Directory.Delete(dir);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error deleting VPN config: {ex.Message}");
                }
                SaveProfilesNoLock();
            }
        }

        public static void SetAutoConnect(string name, string? network)
        {
            lock (stateLock)
            {
                var profile = profiles.FirstOrDefault(p => p.Name == name);
                if (profile != null)
                {
                    profile.AutoConnectNetwork = string.IsNullOrWhiteSpace(network) ? null : network;
                    SaveProfilesNoLock();
                }
            }
        }

        public static bool Connect(string name)
        {
            OpenVpnProfile? profile;
            lock (stateLock)
            {
                profile = profiles.FirstOrDefault(p => p.Name == name);
                // Note: we deliberately don't bail when activeConnections already contains the name —
                // our state is best-effort, and re-issuing connect lets the user retry after a failed
                // attempt (openvpn-gui treats a connect for an already-up tunnel as a no-op).
                if (profile == null || !File.Exists(profile.ConfigPath))
                {
                    return false;
                }
            }
            if (string.IsNullOrEmpty(openVpnGuiExecutable))
            {
                Debug.WriteLine("OpenVPN GUI executable not found.");
                return false;
            }
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = openVpnGuiExecutable,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                psi.ArgumentList.Add("--command");
                psi.ArgumentList.Add("connect");
                psi.ArgumentList.Add(GetConfigName(profile));
                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    Debug.WriteLine($"Process.Start returned null for VPN '{name}'.");
                    return false;
                }
                lock (stateLock)
                {
                    activeConnections.Add(name);
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error connecting VPN '{name}': {ex.Message}");
            }
            return false;
        }

        public static void Disconnect(string name)
        {
            OpenVpnProfile? profile;
            lock (stateLock)
            {
                if (!activeConnections.Contains(name))
                {
                    return;
                }
                profile = profiles.FirstOrDefault(p => p.Name == name);
                if (profile == null)
                {
                    activeConnections.Remove(name);
                    return;
                }
            }

            IssueDisconnect(profile);
            lock (stateLock)
            {
                activeConnections.Remove(name);
            }
        }

        // Issues the openvpn-gui disconnect command for a profile. Best-effort; safe to call even when
        // we never tracked the connection (used by both Disconnect and RemoveProfile).
        private static void IssueDisconnect(OpenVpnProfile profile)
        {
            if (string.IsNullOrEmpty(openVpnGuiExecutable)) return;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = openVpnGuiExecutable,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                psi.ArgumentList.Add("--command");
                psi.ArgumentList.Add("disconnect");
                psi.ArgumentList.Add(GetConfigName(profile));
                using var proc = Process.Start(psi);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error disconnecting VPN '{profile.Name}': {ex.Message}");
            }
        }
        
        public static string GetLogPath(string name)
        {
            Directory.CreateDirectory(LogDirectory);
            return Path.Combine(LogDirectory, $"{name}.log");
        }

        // Returns false (without throwing) when no log file exists yet, so callers can surface that.
        public static bool OpenLog(string name)
        {
            try
            {
                var logPath = GetLogPath(name);
                if (!File.Exists(logPath)) return false;

                var psi = new ProcessStartInfo
                {
                    FileName = logPath,
                    UseShellExecute = true
                };
                using var p = Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening log for VPN '{name}': {ex.Message}");
                return false;
            }
        }

        // Best-effort: reflects whether we issued a connect for this profile and haven't disconnected.
        // It is not a live tunnel-status probe (openvpn-gui offers no simple status query).
        public static bool IsActive(string name)
        {
            lock (stateLock)
            {
                return activeConnections.Contains(name);
            }
        }

        public static bool HasActiveConnections
        {
            get
            {
                lock (stateLock)
                {
                    return activeConnections.Count > 0;
                }
            }
        }
        public static void HandleNetworkChange(string? currentNetwork)
        {
            if (string.IsNullOrWhiteSpace(currentNetwork))
            {
                return;
            }
            // Snapshot under lock; Connect() takes its own lock.
            List<string> toConnect;
            lock (stateLock)
            {
                toConnect = profiles
                    .Where(p => string.Equals(p.AutoConnectNetwork, currentNetwork, StringComparison.OrdinalIgnoreCase)
                                && !activeConnections.Contains(p.Name))
                    .Select(p => p.Name)
                    .ToList();
            }
            foreach (var name in toConnect)
            {
                Connect(name);
            }
        }
        public static string ConfigDirectory => configDirectory;
        public static string LogDirectory => logDirectory;

        public static void SetDirectories(string? configDir, string? logDir)
        {
            lock (stateLock)
            {
                bool reload = false;
                if (!string.IsNullOrWhiteSpace(configDir) && configDir != configDirectory)
                {
                    configDirectory = configDir;
                    Directory.CreateDirectory(configDirectory);
                    Settings.Default.OpenVpnConfigDir = configDirectory;
                    reload = true;
                }
                if (!string.IsNullOrWhiteSpace(logDir) && logDir != logDirectory)
                {
                    logDirectory = logDir;
                    Directory.CreateDirectory(logDirectory);
                    Settings.Default.OpenVpnLogDir = logDirectory;
                }
                if (reload)
                {
                    profiles.Clear();
                    profiles.AddRange(LoadProfilesInternal());
                }
                Settings.Default.Save();
            }
        }

        private static string GetDefaultConfigDir()
        {
            var userDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OpenVPN", "config");
            var programDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OpenVPN", "config");
            if (Directory.Exists(userDir))
            {
                return userDir;
            }
            if (Directory.Exists(programDir))
            {
                return programDir;
            }
            return userDir;
        }

        private static string GetDefaultLogDir()
        {
            var userLog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OpenVPN", "log");
            var programLog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OpenVPN", "log");
            if (Directory.Exists(userLog))
            {
                return userLog;
            }
            if (Directory.Exists(programLog))
            {
                return programLog;
            }
            return Path.Combine(AppContext.BaseDirectory, "openvpn_logs");
        }
    }
}