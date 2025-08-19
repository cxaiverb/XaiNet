using ManagedNativeWifi;
using Microsoft.Windows.Themes;
using NetworkTrayApp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection.Metadata;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Windows.Networking.Connectivity;
using XaiNet2.Helpers;
using XaiNet2.Menus;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Window;

namespace XaiNet2
{
    public partial class WirelessWindow : Window
    {
        public WirelessWindow(MainWindow owner)
        {
            InitializeComponent();
            Owner = owner;
            PositionNearMainWindow(owner);
            if (!HasWiFiAdapter())
            {
                NoWiFiTextBlock.Visibility = Visibility.Visible;
                WiFiDisabledTextBlock.Visibility = Visibility.Collapsed;
                Debug.WriteLine("Wireless adapter not found :(");
                Loaded += OnLoaded;
                return;
            }
            if (WiFiDisabled())
            {
                WiFiDisabledTextBlock.Visibility = Visibility.Visible;
                NoWiFiTextBlock.Visibility = Visibility.Collapsed;
                Debug.WriteLine("WiFi was turned off, no wifis to show");
                Loaded += OnLoaded;
                return;
            }

            NoWiFiTextBlock.Visibility = Visibility.Collapsed;
            WiFiDisabledTextBlock.Visibility = Visibility.Collapsed;

            LoadWiFiNetworks();
            Loaded += OnLoaded;
            bool myrkurModeEnabled = Properties.Settings.Default.MyrkurMode;
            this.SetMyrkurMode(myrkurModeEnabled);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            string homeIcon = "home";
            var homeIco = ImageLoader.LoadImageFromResources(homeIcon);

            if (homeIco != null)
            {
                HomeButton.Content = new Image
                {
                    Source = homeIco,
                    Width = 16,
                    Height = 16
                };
            }
            string refreshIcon = "refresh";
            var refreshIco = ImageLoader.LoadImageFromResources(refreshIcon);

            if (refreshIco != null)
            {
                RefreshButton.Content = new Image
                {
                    Source = refreshIco,
                    Width = 16,
                    Height = 16
                };
            }
            string profilesIcon = "options";
            var profileIco = ImageLoader.LoadImageFromResources(profilesIcon);

            if (profileIco != null)
            {
                ProfileButton.Content = new Image
                {
                    Source = profileIco,
                    Width = 16,
                    Height = 16
                };
            }
            string defaultIcon = "pin-outline";
            var defaultImage = ImageLoader.LoadImageFromResources(defaultIcon);

            if (defaultImage != null)
            {
                PinButton.Content = new System.Windows.Controls.Image
                {
                    Source = defaultImage,
                    Width = 16,
                    Height = 16
                };
            }

            string powerIcon = "power";
            var powerIco = ImageLoader.LoadImageFromResources(powerIcon);

            if (powerIco != null)
            {
                ToggleButton.Content = new Image
                {
                    Source = powerIco,
                    Width = 16,
                    Height = 16
                };
            }
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
            public string SSID { get; set; }
            public string Authentication { get; set; }
            public string SignalStrength { get; set; }
        }

        public void LoadWiFiNetworks()
        {
            List<WiFiNetwork> networks = new List<WiFiNetwork>();

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
                    //string authentication = network.AuthenticationAlgorithm.ToString();
                    string ssidRaw = network.Ssid.ToString();
                    bool isHidden = string.IsNullOrWhiteSpace(ssidRaw);
                    string displaySsid = isHidden
                        ? (network.IsSecurityEnabled ? "🔒 Hidden Network" : "Hidden Network")
                        : (network.IsSecurityEnabled ? $"🔒 {ssidRaw}" : ssidRaw);

                    networks.Add(new WiFiNetwork
                    {
                        SSID = displaySsid,
                        SignalStrength = $"{network.SignalQuality}%",
                    });
                }

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error scanning Wi-Fi: {ex.Message}");
            }

            // Update UI safely
            Dispatcher.Invoke(() => WiFiNetworkList.ItemsSource = networks);
        }
        public static Task RefreshAsync()
        {
            return NativeWifi.ScanNetworksAsync(timeout: TimeSpan.FromSeconds(10));
        }

        private void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            var iface = NativeWifi.EnumerateInterfaceConnections().FirstOrDefault();

            bool wifiDisabled = iface != null && !iface.IsRadioOn;

            if (wifiDisabled == true)
            {
                NativeWifi.TurnOnInterfaceRadio(iface.Id);
            }
            else
            {
                NativeWifi.TurnOffInterfaceRadio(iface.Id);
            }

        }

        public void ConnectToWiFi_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string ssid)
            {
                return;
            }
            // Get the raw SSID from the button tag without the lock
            string rawSSID = ssid.Replace("🔒 ", "");

            var selectedNetwork = NativeWifi.EnumerateAvailableNetworks()
                .FirstOrDefault(n => n.Ssid.ToString() == rawSSID);

            Debug.WriteLine($"Attempting to connect to {rawSSID}");
            InputWindow inputWindow;
            string password = "";
            if (selectedNetwork.IsSecurityEnabled != false)
            {
                Debug.WriteLine("Popup user input window");
                inputWindow = new InputWindow(this)
                {
                    SSID = rawSSID
                };
                inputWindow.ShowDialog();

                password = inputWindow.GetPassword();
            }
            else
            {
                Debug.WriteLine($"connect to open network");
            }

            var wifiAdapter = NativeWifi.EnumerateInterfaces().FirstOrDefault();
            Debug.WriteLine($"Password used is: {password}");
            string authentication = selectedNetwork.AuthenticationAlgorithm.ToString();
            if (authentication == "Open") authentication = "open";
            if (authentication == "RSNA_PSK") authentication = "WPA2PSK";
            string encryption = selectedNetwork.CipherAlgorithm.ToString();
            if (encryption == "None") encryption = "none";
            if (encryption == "CCMP") encryption = "AES";
            string profileName = rawSSID;
            Debug.WriteLine($"Profile name: {profileName}");
            byte[] bytes = Encoding.UTF8.GetBytes(rawSSID);
            Debug.WriteLine($"SSID bytes: {BitConverter.ToString(bytes)}");
            string hexSSID = Convert.ToHexString(bytes);
            Debug.WriteLine($"Hex SSID: {hexSSID}");
            string profileTemplate = "<?xml version=\"1.0\"?>\r\n" +
                "<WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\">\r\n    " +
                $"<name>{rawSSID}</name>\r\n    " +
                "<SSIDConfig>\r\n        " +
                "<SSID>\r\n            " +
                $"<hex>{hexSSID}</hex>\r\n            " +
                $"<name>{rawSSID}</name>\r\n        " +
                "</SSID>\r\n    " +
                "</SSIDConfig>\r\n    " +
                "<connectionType>ESS</connectionType>\r\n    " +
                "<connectionMode>auto</connectionMode>\r\n    " +
                "<MSM>\r\n        " +
                "<security>\r\n            " +
                "<authEncryption>\r\n                " +
                $"<authentication>{authentication}</authentication>\r\n                " +
                $"<encryption>{encryption}</encryption>\r\n                " +
                "<useOneX>false</useOneX>\r\n            " +
                "</authEncryption>\r\n            " +
                "<sharedKey>\r\n                " +
                "<keyType>passPhrase</keyType>\r\n                " +
                "<protected>false</protected>\r\n                " +
                $"<keyMaterial>{password}</keyMaterial>\r\n            " +
                "</sharedKey>\r\n        " +
                "</security>\r\n    " +
                "</MSM>\r\n    " +
                "<MacRandomization \r\nxmlns=\"http://www.microsoft.com/networking/WLAN/profile/v3\">\r\n        " +
                "<enableRandomization>false</enableRandomization>\r\n    " +
                "</MacRandomization>\r\n</WLANProfile>";

            Debug.WriteLine($"Profile XML: " +
                $"{profileTemplate}");

            Debug.WriteLine("Profile modified");


            try
            {
                NativeWifi.SetProfile(
                    interfaceId: wifiAdapter.Id,
                    profileType: ProfileType.AllUser, 
                    profileXml: profileTemplate, 
                    profileSecurity: null, 
                    overwrite: true);
                NativeWifi.ConnectNetwork(
                    interfaceId: wifiAdapter.Id,
                    profileName: profileName,
                    bssType: BssType.Infrastructure);

                NotificationHelper.ShowToast($"{rawSSID}", $"Connected to {rawSSID}");
            }
            catch (Exception ex)
            {
                string pattern = @"ErrorMessage:\s([a-zA-Z\s:]+)[a-zA-Z\s.,;:\d]+ReasonMessage:\s([a-zA-Z\s:.]+)";
                var match = Regex.Match(ex.Message, pattern);
                NotificationHelper.ShowToast("Error", $"{match.Groups[1].Value}\n{match.Groups[2].Value}");
            }
        }
        private bool isPinned = false;
        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            isPinned = !isPinned; // Toggle state
            Topmost = isPinned;   // Keep window on top when pinned

            string iconName = isPinned ? "pin-solid" : "pin-outline";

            var newIcon = ImageLoader.LoadImageFromResources(iconName);

            if (newIcon != null)
            {
                PinButton.Content = new System.Windows.Controls.Image
                {
                    Source = newIcon,
                    Width = 16,
                    Height = 16
                };
            }
        }
        private void ProfileButton_Click(object sender, RoutedEventArgs e)
        {
            var profilesWindow = new ProfilesWindow(this);
            profilesWindow.Show();
            Hide();
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            if( !isPinned)
            {
                Hide();
            }
        }
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            WindowHelper.ApplyBlurEffect(this);
        }


        private void RefreshWiFi_Click(object sender, RoutedEventArgs e)
        {
            LoadWiFiNetworks();
        }
        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
            Owner.Show();
        }

    }
}
