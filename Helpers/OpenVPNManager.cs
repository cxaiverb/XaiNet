using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

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
        private static readonly string OpenVpnConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OpenVPN Connect", "profiles");
        private static readonly List<OpenVpnProfile> profiles = LoadProfilesInternal();
        private static readonly Dictionary<string, Process> activeConnections = new();
        private static readonly string? openVpnExecutable = LocateOpenVpn();

        private static string? LocateOpenVpn()
        {
            string[] exeNames = Environment.OSVersion.Platform == PlatformID.Win32NT
                ? new[] { "openvpn.exe", "openvpnconnect.exe", "OpenVPNConnect.exe" }
                : new[] { "openvpn" };

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
                    Path.Combine(programFiles, "OpenVPN", "bin", "openvpn.exe"),
                    Path.Combine(programFiles, "OpenVPN", "OpenVPN", "bin", "openvpn.exe"),
                    Path.Combine(programFilesX86, "OpenVPN", "bin", "openvpn.exe"),
                    Path.Combine(programFilesX86, "OpenVPN", "OpenVPN", "bin", "openvpn.exe"),
                    Path.Combine(programFiles, "OpenVPN Connect", "OpenVPNConnect.exe"),
                    Path.Combine(programFilesX86, "OpenVPN Connect", "OpenVPNConnect.exe"),
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
                if (Directory.Exists(OpenVpnConfigDir))
                {
                    foreach (var file in Directory.GetFiles(OpenVpnConfigDir, "*.ovpn"))
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
                Directory.CreateDirectory(OpenVpnConfigDir);
                string fileName = Path.GetFileName(configPath);
                string baseName = Path.GetFileNameWithoutExtension(fileName);
                string destPath = Path.Combine(OpenVpnConfigDir, fileName);
                int suffix = 1;
                while (File.Exists(destPath))
                {
                    destPath = Path.Combine(OpenVpnConfigDir, $"{baseName}_{suffix++}.ovpn");
                }
                File.Copy(configPath, destPath, overwrite: false);

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
            if (profile == null || activeConnections.ContainsKey(name) || !File.Exists(profile.ConfigPath))
            {
                return false;
            }
            if (string.IsNullOrEmpty(openVpnExecutable))
            {
                Debug.WriteLine("OpenVPN executable not found.");
                return false;
            }
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = openVpnExecutable,
                    Arguments = $"--config \"{profile.ConfigPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                var process = Process.Start(psi);
                if (process != null)
                {
                    activeConnections[name] = process;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error connecting VPN '{name}': {ex.Message}");
            }
            return false;
        }

        public static void Disconnect(string name)
        {
            if (activeConnections.TryGetValue(name, out var process))
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                    }
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
                    if (!activeConnections.ContainsKey(profile.Name))
                    {
                        Connect(profile.Name);
                    }
                }
            }
        }
    }
}