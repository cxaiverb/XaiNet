using ManagedNativeWifi;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using XaiNet2.Helpers;

namespace XaiNet2.Menus
{
    public partial class WirelessWindow : Window
    {
        public WirelessWindow(MainWindow owner)
        {
            InitializeComponent();
            Owner = owner;
            PositionNearMainWindow(owner);
            Loaded += OnLoaded;

            bool myrkurModeEnabled = Properties.Settings.Default.MyrkurMode;
            this.SetMyrkurMode(myrkurModeEnabled);

            RefreshWiFiState();
        }

        // Evaluates adapter presence / radio state and shows the matching placeholder, or loads
        // the network list. Called on open and after toggling the radio.
        private void RefreshWiFiState()
        {
            if (!HasWiFiAdapter())
            {
                NoWiFiTextBlock.Visibility = Visibility.Visible;
                WiFiDisabledTextBlock.Visibility = Visibility.Collapsed;
                NoNetworksTextBlock.Visibility = Visibility.Collapsed;
                WiFiNetworkList.ItemsSource = null;
                Debug.WriteLine("Wireless adapter not found :(");
                return;
            }
            if (WiFiDisabled())
            {
                WiFiDisabledTextBlock.Visibility = Visibility.Visible;
                NoWiFiTextBlock.Visibility = Visibility.Collapsed;
                NoNetworksTextBlock.Visibility = Visibility.Collapsed;
                WiFiNetworkList.ItemsSource = null;
                Debug.WriteLine("WiFi was turned off, no wifis to show");
                return;
            }

            NoWiFiTextBlock.Visibility = Visibility.Collapsed;
            WiFiDisabledTextBlock.Visibility = Visibility.Collapsed;
            LoadWiFiNetworks();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            HomeButton.Content = ImageLoader.CreateIcon("home");
            RefreshButton.Content = ImageLoader.CreateIcon("refresh");
            ProfileButton.Content = ImageLoader.CreateIcon("options");
            PinButton.Content = ImageLoader.CreateIcon("pin-outline");
            ToggleButton.Content = ImageLoader.CreateIcon("power");
        }

        private bool HasWiFiAdapter()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Any(nic => nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
        }
        private bool WiFiDisabled()
        {
            var wifi = NativeWifi.EnumerateInterfaceConnections()
                .FirstOrDefault();
            bool wifiDisabled = wifi != null && !wifi.IsRadioOn;
            return wifiDisabled;
        }
        private void PositionNearMainWindow(MainWindow owner)
        {
            // Get position from owner
            Left = Owner.Left;
            Top = Owner.Top;
            Width = Owner.Width;
            Height = Owner.Height;
        }

        public class WiFiNetwork
        {
            // Raw SSID — used as the connect-target and as the Tag passed to handlers.
            public string SSID { get; set; }
            // Display name — shown in the list. Equals SSID, or "Hidden Network" for empty SSIDs.
            public string DisplayName { get; set; }
            public string Authentication { get; set; }
            public string SignalStrength { get; set; }
            public bool IsSecured { get; set; }
            public bool IsConnected { get; set; }
        }

        public void LoadWiFiNetworks()
        {
            List<WiFiNetwork> networks = new List<WiFiNetwork>();
            string currentSsid = GetCurrentSsid();

            try
            {
                // Get all available Wi-Fi networks
                var availableNetworks = NativeWifi.EnumerateAvailableNetworks()
                    .GroupBy(n => n.Ssid.ToString()) // Group by SSID to remove duplicates
                    .Select(g => g.First()) // Pick the first from each group
                    .OrderByDescending(x => x.SignalQuality)
                    .ToList();

                foreach (var network in availableNetworks)
                {
                    string ssidRaw = network.Ssid.ToString();
                    bool isHidden = string.IsNullOrWhiteSpace(ssidRaw);
                    networks.Add(new WiFiNetwork
                    {
                        SSID = ssidRaw,
                        DisplayName = isHidden ? "Hidden Network" : ssidRaw,
                        SignalStrength = $"{network.SignalQuality}%",
                        IsSecured = network.IsSecurityEnabled,
                        IsConnected = !isHidden
                                      && !string.IsNullOrEmpty(currentSsid)
                                      && string.Equals(currentSsid, ssidRaw, StringComparison.OrdinalIgnoreCase),
                    });
                }

                // Float the connected network to the top.
                networks = networks
                    .OrderByDescending(n => n.IsConnected)
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error scanning Wi-Fi: {ex.Message}");
            }

            Dispatcher.Invoke(() =>
            {
                WiFiNetworkList.ItemsSource = networks;
                NoNetworksTextBlock.Visibility = networks.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            });
        }

        private static string GetCurrentSsid()
        {
            try
            {
                var conn = NativeWifi.EnumerateInterfaceConnections()
                    .FirstOrDefault(c => c.IsRadioOn && c.IsConnected);
                return conn?.ProfileName ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            var iface = NativeWifi.EnumerateInterfaceConnections().FirstOrDefault();
            if (iface == null)
            {
                NotificationHelper.ShowToast("Wi-Fi", "No Wi-Fi adapter available");
                return;
            }

            try
            {
                if (!iface.IsRadioOn)
                {
                    NativeWifi.TurnOnInterfaceRadio(iface.Id);
                }
                else
                {
                    NativeWifi.TurnOffInterfaceRadio(iface.Id);
                }
            }
            catch (Exception ex)
            {
                NotificationHelper.ShowToast("Wi-Fi", ex.Message);
                return;
            }

            // Reflect the new radio state in the UI.
            RefreshWiFiState();
        }

        public async void ConnectToWiFi_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string rawSSID || string.IsNullOrEmpty(rawSSID))
            {
                return;
            }

            var selectedNetwork = NativeWifi.EnumerateAvailableNetworks()
                .FirstOrDefault(n => n.Ssid.ToString() == rawSSID);

            if (selectedNetwork == null)
            {
                NotificationHelper.ShowToast("Error", $"Network '{rawSSID}' is no longer available");
                return;
            }

            var (authentication, encryption, keyType, enterprise) = WlanProfileHelper.MapSecurity(
                selectedNetwork.AuthenticationAlgorithm.ToString(),
                selectedNetwork.CipherAlgorithm.ToString());

            // Enterprise (802.1X): Windows prompts for credentials at association, so don't ask for a key.
            if (enterprise)
            {
                var entError = WlanProfileHelper.CreateAndConnect(rawSSID, authentication, encryption,
                    password: string.Empty, nonBroadcast: false, keyType: keyType, enterprise: true);
                NotificationHelper.ShowToast(entError == null ? rawSSID : "Error",
                    entError ?? $"Connecting to {rawSSID}… (sign-in may be required)");
                return;
            }

            string password = string.Empty;
            if (selectedNetwork.IsSecurityEnabled)
            {
                var inputWindow = new InputWindow(this) { SSID = rawSSID };
                if (inputWindow.ShowDialog() != true) return;
                password = inputWindow.GetPassword();
                if (string.IsNullOrEmpty(password))
                {
                    NotificationHelper.ShowToast("Error", "Password cannot be empty");
                    return;
                }
            }

            NotificationHelper.ShowToast(rawSSID, $"Connecting to {rawSSID}…");
            var error = await WlanProfileHelper.CreateAndConnectAsync(rawSSID, authentication, encryption,
                password, nonBroadcast: false, keyType: keyType);
            NotificationHelper.ShowToast(error == null ? rawSSID : "Error", error ?? $"Connected to {rawSSID}");
            LoadWiFiNetworks();
        }

        private bool isPinned = false;
        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            isPinned = !isPinned; // Toggle state
            Topmost = isPinned;   // Keep window on top when pinned
            PinButton.Content = ImageLoader.CreateIcon(isPinned ? "pin-solid" : "pin-outline");
        }
        private void ProfileButton_Click(object sender, RoutedEventArgs e)
        {
            var profilesWindow = new ProfilesWindow(this);
            profilesWindow.Show();
            Hide();
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            // Don't auto-hide while a child dialog (e.g. password prompt) is open,
            // otherwise the parent disappears under the dialog and never reappears.
            if (!isPinned && OwnedWindows.Count == 0)
            {
                Hide();
            }
        }
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            WindowHelper.ApplyBlurEffect(this);
        }


        private void HiddenNetworkButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new HiddenNetworkWindow(this);
            if (dialog.ShowDialog() != true) return;

            var (authentication, encryption, keyType, enterprise) = dialog.GetWlanParameters();
            var error = WlanProfileHelper.CreateAndConnect(
                ssid: dialog.NetworkSsid,
                authentication: authentication,
                encryption: encryption,
                password: dialog.NetworkPassword,
                nonBroadcast: dialog.NonBroadcast,
                autoConnect: dialog.AutoConnect,
                keyType: keyType,
                enterprise: enterprise);
            NotificationHelper.ShowToast(error == null ? dialog.NetworkSsid : "Error",
                error ?? $"Connecting to {dialog.NetworkSsid}…");
        }

        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            var wifiAdapter = NativeWifi.EnumerateInterfaces().FirstOrDefault();
            if (wifiAdapter == null)
            {
                NotificationHelper.ShowToast("Error", "No WiFi adapter available");
                return;
            }
            try
            {
                NativeWifi.DisconnectNetwork(wifiAdapter.Id);
                NotificationHelper.ShowToast("WiFi", "Disconnected");
                LoadWiFiNetworks();
            }
            catch (Exception ex)
            {
                NotificationHelper.ShowToast("Error", ex.Message);
            }
        }

        private async void RefreshWiFi_Click(object sender, RoutedEventArgs e)
        {
            RefreshButton.IsEnabled = false;
            try
            {
                // Real scan, not just a re-read of cached results.
                await NativeWifi.ScanNetworksAsync(timeout: TimeSpan.FromSeconds(8));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WiFi scan failed: {ex.Message}");
            }
            finally
            {
                LoadWiFiNetworks();
                RefreshButton.IsEnabled = true;
            }
        }
        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
            Owner.Show();
        }

    }
}
