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
        static OpenVPNManager()
        {
            configDirectory = string.IsNullOrWhiteSpace(Settings.Default.OpenVpnConfigDir)
                ? GetDefaultConfigDir()
                : Settings.Default.OpenVpnConfigDir;
            logDirectory = string.IsNullOrWhiteSpace(Settings.Default.OpenVpnLogDir)
                ? GetDefaultLogDir()
                : Settings.Default.OpenVpnLogDir;

            Directory.CreateDirectory(configDirectory);
            Directory.CreateDirectory(logDirectory);

            profiles = LoadProfilesInternal();
        }

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

        private static void SaveProfiles()
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
            return profiles;
        }

        public static void AddProfile(string configPath)
        {
            try
            {
                Directory.CreateDirectory(configDirectory);
                string fileName = Path.GetFileName(configPath);
                string baseName = Path.GetFileNameWithoutExtension(fileName);
                string destPath = Path.Combine(configDirectory, baseName);
                Directory.CreateDirectory(destPath);
                string fileDest = Path.Combine(destPath, fileName);



                File.Copy(configPath, fileDest, overwrite: false);

                string profileName = Path.GetFileNameWithoutExtension(destPath);
                string uniqueName = profileName;
                int nameSuffix = 1;
                while (profiles.Any(p => p.Name == uniqueName))
                {
                    uniqueName = $"{profileName}_{nameSuffix++}";
                }

                profiles.Add(new OpenVpnProfile { Name = uniqueName, ConfigPath = destPath });
                SaveProfiles();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding VPN profile: {ex.Message}");
            }
        }

        public static void RemoveProfile(string name)
        {
            var profile = profiles.FirstOrDefault(p => p.Name == name);
            if (profile != null)
            {
                Disconnect(name);
                profiles.Remove(profile);
                try
                {
                    if (File.Exists(profile.ConfigPath))
                    {
                        File.Delete(profile.ConfigPath);
                        var dir = Path.GetDirectoryName(profile.ConfigPath);
                        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                        {
                            Directory.Delete(dir);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error deleting VPN config: {ex.Message}");
                }
                SaveProfiles();
            }
        }

        public static void SetAutoConnect(string name, string? network)
        {
            var profile = profiles.FirstOrDefault(p => p.Name == name);
            if (profile != null)
            {
                profile.AutoConnectNetwork = string.IsNullOrWhiteSpace(network) ? null : network;
                SaveProfiles();
            }
        }

        public static bool Connect(string name)
        {
            var profile = profiles.FirstOrDefault(p => p.Name == name);
            if (profile == null || activeConnections.Contains(name) || !File.Exists(profile.ConfigPath))
            {
                return false;
            }
            if (string.IsNullOrEmpty(openVpnGuiExecutable))
            {
                Debug.WriteLine("OpenVPN GUI executable not found.");
                return false;
            }
            try
            {
                var relative = Path.GetRelativePath(configDirectory, profile.ConfigPath);
                var configName = Path.ChangeExtension(relative, null);
                var logPath = GetLogPath(profile.Name);
                var psi = new ProcessStartInfo
                {
                    FileName = openVpnGuiExecutable,
                    Arguments = $"--command connect \"{profile.Name}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                Process.Start(psi);
                activeConnections.Add(name);
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
            if (!activeConnections.Contains(name) || string.IsNullOrEmpty(openVpnGuiExecutable))
            {
                return;
            }
            try
            {
                var profile = profiles.FirstOrDefault(p => p.Name == name);
                if (profile == null)
                {
                    return;
                }
                var relative = Path.GetRelativePath(configDirectory, profile.ConfigPath);
                var configName = Path.ChangeExtension(relative, null);
                var psi = new ProcessStartInfo
                {
                    FileName = openVpnGuiExecutable,
                    Arguments = $"--command disconnect \"{configName}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error disconnecting VPN '{name}': {ex.Message}");
            }
            finally
            {
                activeConnections.Remove(name);
            }
        }
        
        public static string GetLogPath(string name)
        {
            Directory.CreateDirectory(LogDirectory);
            return Path.Combine(LogDirectory, $"{name}.log");
        }

        public static void OpenLog(string name)
        {
            try
            {
                var logPath = GetLogPath(name);
                if (File.Exists(logPath))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = logPath,
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening log for VPN '{name}': {ex.Message}");
            }
        }
        public static void HandleNetworkChange(string? currentNetwork)
        {
            if (string.IsNullOrWhiteSpace(currentNetwork))
            {
                return;
            }
            foreach (var profile in profiles)
            {
                if (string.Equals(profile.AutoConnectNetwork, currentNetwork, StringComparison.OrdinalIgnoreCase))
                {
                    if (!activeConnections.Contains(profile.Name))
                    {
                        Connect(profile.Name);
                    }
                }
            }
        }
        public static string ConfigDirectory => configDirectory;
        public static string LogDirectory => logDirectory;

        public static void SetDirectories(string? configDir, string? logDir)
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