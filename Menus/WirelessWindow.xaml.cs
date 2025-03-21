using ManagedNativeWifi;
using NetworkTrayApp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using XaiNet2.Menus;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Window;

namespace XaiNet2
{
    public partial class WirelessWindow : Window
    {
        public WirelessWindow(MainWindow owner)
        {
            InitializeComponent();
            this.Owner = owner;
            PositionNearMainWindow(owner);
            if (!HasWiFiAdapter())
            {
                NoWiFiTextBlock.Visibility = Visibility.Visible;
                Debug.WriteLine("Wireless adapter not found :(");
                return;
            }

            NoWiFiTextBlock.Visibility = Visibility.Collapsed;

            LoadWiFiNetworks();
            this.Loaded += OnLoaded;
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
        }

        private bool HasWiFiAdapter()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Any(nic => nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
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
            public string Encryption { get; set; }
            public string SignalStrength { get; set; }
        }

        private void LoadWiFiNetworks()
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

        private void ConnectToWiFi_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string ssid)
            {
                Debug.WriteLine($"Attempting to connect to {ssid}");

                try
                {
                    string encryption = "None";
                    /* if (AuthenticationAlgorithm = "Open") // fix later
                    {
                        string encryption = "None";
                    }
                    else
                    {
                        string encryption = "AES";
                    } */
                    byte[] bytes = Encoding.UTF8.GetBytes(ssid);
                    string hexSSID = Convert.ToHexString(bytes);
                    string password = ""; // TODO: Get password from user input
                    string passwordRequired = password.Length > 0 ? "true" : "false";
                    string profileTemplate = "<?xml version=\"1.0\"?>\r\n" +
                        "<WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\">\r\n    " +
                        $"<name>{ssid}</name>\r\n    " +
                        "<SSIDConfig>\r\n        " +
                        "<SSID>\r\n            " +
                        $"<hex>{hexSSID}</hex>\r\n            " +
                        $"<name>{ssid}</name>\r\n        " +
                        "</SSID>\r\n    " +
                        "</SSIDConfig>\r\n    " +
                        "<connectionType>ESS</connectionType>\r\n    " +
                        "<connectionMode>auto</connectionMode>\r\n    " +
                        "<MSM>\r\n        " +
                        "<security>\r\n            " +
                        "<authEncryption>\r\n                " +
                        "<authentication>WPA2</authentication>\r\n                " +
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
                    Debug.WriteLine($"Connected to {ssid}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error connecting to Wi-Fi: {ex.Message}");
                }
            }
        }

        // temp pin while testing shit

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
        private void Window_Deactivated(object sender, EventArgs e)
        {
            if (!isPinned)
            {
                Hide(); // Hides the window when clicking outside unless pinned
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
            this.Close();
            Owner.Show();
        }

    }
}
