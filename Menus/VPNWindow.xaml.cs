using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using XaiNet2.Helpers;
using Microsoft.Win32;
using ManagedNativeWifi;

namespace XaiNet2.Menus
{

    public partial class VPNWindow : Window
    {
        // Suppresses the deactivate-to-hide behaviour while a modal dialog (file picker, prompt)
        // is open, so the window doesn't disappear behind the dialog.
        private bool _suppressHide;

        public VPNWindow(MainWindow owner)
        {
            InitializeComponent();
            this.Owner = owner;
            PositionNearMainWindow(owner);

            bool myrkurModeEnabled = Properties.Settings.Default.MyrkurMode;
            this.SetMyrkurMode(myrkurModeEnabled);

            LoadProfiles();
        }

        // View model surfaced to the list: carries live connection state and the auto-connect target.
        public class VpnProfileView
        {
            public string Name { get; set; } = string.Empty;
            public bool IsConnected { get; set; }
            public string AutoConnectNetwork { get; set; } = string.Empty;
            public bool HasAutoConnect { get; set; }
            public string AutoConnectText { get; set; } = string.Empty;
        }

        private void LoadProfiles()
        {
            var views = OpenVPNManager.GetProfiles()
                .Select(p => new VpnProfileView
                {
                    Name = p.Name,
                    IsConnected = OpenVPNManager.IsActive(p.Name),
                    AutoConnectNetwork = p.AutoConnectNetwork ?? string.Empty,
                    HasAutoConnect = !string.IsNullOrEmpty(p.AutoConnectNetwork),
                    AutoConnectText = string.IsNullOrEmpty(p.AutoConnectNetwork)
                        ? string.Empty
                        : $"Auto-connects on “{p.AutoConnectNetwork}”",
                })
                .ToList();

            VpnProfilesList.ItemsSource = null;
            VpnProfilesList.ItemsSource = views;

            NoVpnProfilesTextBlock.Visibility = views.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AddProfileButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "OpenVPN config (*.ovpn)|*.ovpn"
            };

            _suppressHide = true;
            bool? picked;
            try { picked = dialog.ShowDialog(); }
            finally { _suppressHide = false; }

            if (picked == true)
            {
                var added = OpenVPNManager.AddProfile(dialog.FileName);
                if (added == null)
                {
                    NotificationHelper.ShowToast("VPN", "Could not import that .ovpn file.");
                }
                LoadProfiles();
            }
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is VpnProfileView profile)
            {
                if (OpenVPNManager.Connect(profile.Name))
                {
                    NotificationHelper.ShowToast("VPN", $"Connecting “{profile.Name}”…");
                }
                else
                {
                    NotificationHelper.ShowToast("VPN",
                        $"Couldn't start “{profile.Name}”. Make sure OpenVPN GUI is running and the config is valid.");
                }
                LoadProfiles();
            }
        }

        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is VpnProfileView profile)
            {
                OpenVPNManager.Disconnect(profile.Name);
                NotificationHelper.ShowToast("VPN", $"Disconnecting “{profile.Name}”");
                LoadProfiles();
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is VpnProfileView profile)
            {
                OpenVPNManager.RemoveProfile(profile.Name);
                LoadProfiles();
            }
        }

        private void LogButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is VpnProfileView profile)
            {
                if (!OpenVPNManager.OpenLog(profile.Name))
                {
                    NotificationHelper.ShowToast("VPN", $"No log for “{profile.Name}” yet — connect first.");
                }
            }
        }

        private void AutoButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not VpnProfileView profile)
            {
                return;
            }

            var picker = new NetworkChoiceWindow(
                this,
                "Auto-connect",
                "Connect this VPN automatically when you join the selected Wi-Fi network.",
                GetKnownNetworkNames(),
                profile.AutoConnectNetwork);

            _suppressHide = true;
            bool? result;
            try { result = picker.ShowDialog(); }
            finally { _suppressHide = false; }

            if (result == true)
            {
                OpenVPNManager.SetAutoConnect(profile.Name, string.IsNullOrWhiteSpace(picker.Value) ? null : picker.Value);
                LoadProfiles();
            }
        }

        // Known Wi-Fi profile names plus currently-visible SSIDs, for the auto-connect picker.
        private static IEnumerable<string> GetKnownNetworkNames()
        {
            var names = new List<string>();
            try { names.AddRange(NativeWifi.EnumerateProfiles().Select(p => p.Name)); }
            catch { /* ignore */ }
            try { names.AddRange(NativeWifi.EnumerateAvailableNetworks().Select(n => n.Ssid.ToString())); }
            catch { /* ignore */ }
            return names.Where(n => !string.IsNullOrWhiteSpace(n));
        }

        private void PositionNearMainWindow(MainWindow owner)
        {
            Left = Owner.Left;
            Top = Owner.Top;
            Width = Owner.Width;
            Height = Owner.Height;
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            if (_suppressHide || isPinned) return;
            this.Hide();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            WindowHelper.ApplyBlurEffect(this);
            ImageLoader.SetIcon(HomeButton, "home");
            ImageLoader.SetIcon(SettingsButton, "options");
            ImageLoader.SetIcon(PinButton, isPinned ? "pin-solid" : "pin-outline");
        }

        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
            Owner?.Show();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (Owner is not MainWindow main) return;
            _suppressHide = true;
            try { new OptionsWindow(main).ShowDialog(); }
            finally { _suppressHide = false; }
        }

        private bool isPinned;
        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            isPinned = !isPinned;
            Topmost = isPinned;
            ImageLoader.SetIcon(PinButton, isPinned ? "pin-solid" : "pin-outline");
        }
    }
}
