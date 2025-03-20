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
        private void Window_Deactivated(object sender, EventArgs e)
        {
            this.Hide();
        }
        public class WiFiNetwork
        {
            public string SSID { get; set; }
            public string BSSID { get; set; }
            public string NetworkType { get; set; }
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
                var availableNetworks = NativeWifi.EnumerateBssNetworks()
                    .GroupBy(n => n.Ssid.ToString()) // Group by SSID to remove duplicates
                    .Select(g => g.First()) // Pick the first from each group
                    .ToList();

                foreach (var network in availableNetworks)
                {
                    networks.Add(new WiFiNetwork
                    {
                        SSID = network.Ssid.ToString(),
                        SignalStrength = $"{network.SignalStrength}%", // Signal strength
                        Authentication = network.Interface.ToString(),
                        Encryption = network.PhyType.ToString()
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
            if (sender is System.Windows.Forms.Button button && button.Tag is string ssid)
            {
                Debug.WriteLine($"Attempting to connect to {ssid}");

                try
                {
                    Debug.WriteLine($"Connected to {ssid}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error connecting to Wi-Fi: {ex.Message}");
                }
            }
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
