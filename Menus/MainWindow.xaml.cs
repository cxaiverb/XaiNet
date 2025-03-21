using System;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;
using System.Net.NetworkInformation;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Threading;
using System.Management;
using XaiNet2;
using LiveChartsCore;
using System.Collections.ObjectModel;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView;
using SkiaSharp;
using ManagedNativeWifi;
using XaiNet2.Menus;

namespace NetworkTrayApp
{
    public partial class MainWindow : Window
    {
        static NotifyIcon trayIcon;


        private DispatcherTimer updateTimer;
        public MainWindow()
        {
            InitializeComponent();
            SetupTrayIcon();
            LoadNetworkAdapters();
            LoadSavedAdapterSettings();
            PositionWindowNearTray();
            this.Hide();
            updateTimer = new DispatcherTimer();
            updateTimer.Interval = TimeSpan.FromSeconds(1);
            updateTimer.Tick += SpeedChecker;
            updateTimer.Start();

            this.Loaded += OnLoaded;

            bool myrkurModeEnabled = XaiNet2.Properties.Settings.Default.MyrkurMode;
            this.SetMyrkurMode(myrkurModeEnabled);

        }

        static bool HasInternet()
        {
            try
            {
                using (Ping ping = new Ping())
                {
                    PingReply reply = ping.Send("google.com", 1000); // 1-second timeout
                    if (reply != null && reply.Status == IPStatus.Success)
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HasInternet method failed with {ex.Message}");
            }
            return false;
        }

        static void IconSelector(Object source, System.Timers.ElapsedEventArgs e)
        {
            string iconName = "no-network-w"; // Default to no network
            int ConnectionStrength = 0;

            if (HasInternet())
            {
                NetworkInterface activeEthernet = null;
                NetworkInterface activeWiFi = null;

                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus == OperationalStatus.Up)
                    {
                        if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                        {
                            activeWiFi = nic;
                        }
                        else if (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                        {
                            activeEthernet = nic;
                        }
                    }
                }

                // Prioritize Wi-Fi over Ethernet if both are available
                if (activeWiFi != null)
                {
                    // Get Signal Strength
                    ConnectionStrength = GetWiFiSignalStrength();
                    Debug.WriteLine($"Wi-Fi Signal Strength: {ConnectionStrength}dBm");

                    if (ConnectionStrength <= -100)
                    {
                        iconName = "wi-fi-1";
                        Debug.WriteLine("Set icon to 1 bar");

                    }
                    else if (ConnectionStrength <= -80)
                    {
                        iconName = "wi-fi-2";
                        Debug.WriteLine("Set icon to 2 bars");
                    }
                    else if (ConnectionStrength <= -70)
                    {
                        iconName = "wi-fi-3";
                        Debug.WriteLine("Set icon to 3 bars");

                    }
                    else if (ConnectionStrength <= -60)
                    {
                        iconName = "wi-fi-4";
                        Debug.WriteLine("Set icon to 4 bars");
                    }
                    else if (ConnectionStrength <= -50)
                    {
                        iconName = "wi-fi-full";
                        Debug.WriteLine("Set icon to full");
                    }
                }
                else if (activeEthernet != null)
                {
                    iconName = "wired-network-connection-w";
                }
            }



            trayIcon.Icon = new Icon(new MemoryStream((byte[])XaiNet2.Properties.Resources.ResourceManager.GetObject(iconName)));
            
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("Loading icons...");

            string optionsIcon = "options";

            var optionsIco = ImageLoader.LoadImageFromResources(optionsIcon);

            if (optionsIco != null)
            {
                OptionsButton.Content = new System.Windows.Controls.Image
                {
                    Source = optionsIco,
                    Width = 16,
                    Height = 16
                };
            }

            string wifiIcon = "wi-fi-full";

            var wifiIco = ImageLoader.LoadImageFromResources(wifiIcon);

            if (wifiIco != null)
            {
                WifiButton.Content = new System.Windows.Controls.Image
                {
                    Source = wifiIco,
                    Width = 16,
                    Height = 16
                };
            }
            string vpnIcon = "openvpn";

            var vpnIco = ImageLoader.LoadImageFromResources(vpnIcon);

            if (vpnIco != null)
            {
                VPNButton.Content = new System.Windows.Controls.Image
                {
                    Source = vpnIco,
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
        }

        private List<NetworkAdapterInfo> allAdapters = new List<NetworkAdapterInfo>(); // Store all adapters
        private HashSet<string> visibleAdapters = new HashSet<string>(); // Store user-selected visible adapters

        private void LoadNetworkAdapters()
        {
            allAdapters.Clear(); // Reset the list

            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                var ipProps = nic.GetIPProperties();
                var ipAddresses = ipProps.UnicastAddresses
                    .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Select(a => a.Address.ToString());

                allAdapters.Add(new NetworkAdapterInfo
                {
                    Name = nic.Name,
                    Type = $"Type: {nic.NetworkInterfaceType}",
                    Status = $"Status: {nic.OperationalStatus}",
                    IPAddress = ipAddresses.Any() ? $"IP: {string.Join(", ", ipAddresses)}" : "IP: None",
                    Speed = $"Speed: {BitsToHumanReadable(nic.Speed)}",
                    SentSpeed = "S: 0 bps",
                    ReceiveSpeed = "R: 0 bps",
                    AdapterId = nic.Id
                });
            }

            LoadSavedAdapterSettings(); // Ensure visibility settings are applied
        }

        public void LoadSavedAdapterSettings()
        {
            string savedAdapters = XaiNet2.Properties.Settings.Default.VisibleAdapters;
            Debug.WriteLine($"Saved Adapters: {savedAdapters}");

            if (!string.IsNullOrEmpty(savedAdapters))
            {
                visibleAdapters = new HashSet<string>(savedAdapters.Split(',')); // Load stored adapters
            }
            else
            {
                // If no saved settings, default to showing all adapters
                visibleAdapters = new HashSet<string>(allAdapters.Select(a => a.Name));
            }

            RefreshAdapters();
        }

        private void RefreshAdapters()
        {
            Debug.WriteLine("Refreshing Adapters...");
            NetworkList.ItemsSource = allAdapters
                .Where(adapter => visibleAdapters.Contains(adapter.Name))
                .ToList();

            Debug.WriteLine($"Adapters displayed: {NetworkList.Items.Count}");
        }

        public List<NetworkAdapterInfo> GetNetworkAdapters()
        {
            return allAdapters; // Return the full list
        }

        public bool IsAdapterVisible(string adapterName)
        {
            return visibleAdapters.Contains(adapterName);
        }

        public void UpdateAdapterVisibility(List<string> enabledAdapters)
        {
            visibleAdapters.Clear();
            visibleAdapters.UnionWith(enabledAdapters);
            RefreshAdapters();
        }


        public void SpeedChecker(Object sender, EventArgs e)
        {
            //Debug.WriteLine("Checking speed...");
            {
                Dictionary<string, (long SentSpeed, long ReceiveSpeed)> adapterSpeeds = GetNetworkSpeeds();
                foreach (var item in NetworkList.Items)
                {
                    if (item is NetworkAdapterInfo adapter)
                    {
                        long sentSpeed = 0;
                        long recvSpeed = 0;

                        if (adapterSpeeds.TryGetValue(adapter.Name, out var speeds))
                        {
                            sentSpeed = speeds.SentSpeed;
                            recvSpeed = speeds.ReceiveSpeed;
                        }
                        adapter.SentSpeed = $"S: {BitsToHumanReadable(sentSpeed)}/s";
                        adapter.ReceiveSpeed = $"R: {BitsToHumanReadable(recvSpeed)}/s";
                        //Debug.WriteLine($"Adapter: {adapter.Name} - Send: {adapter.SentSpeed}, Receive: {adapter.ReceiveSpeed}");
                        // Update Graph
                        if (adapter.DownloadSpeedValues.Count > 30) adapter.DownloadSpeedValues.RemoveAt(0);
                        if (adapter.UploadSpeedValues.Count > 30) adapter.UploadSpeedValues.RemoveAt(0);
                        
                        adapter.UploadSpeedValues.Add(sentSpeed);
                        adapter.DownloadSpeedValues.Add(recvSpeed);
                    }

                }
            }
        }
        private static int GetWiFiSignalStrength()
        {
            try
            {
                string connectedSSID = GetConnectedWiFiSSID();
                if (string.IsNullOrEmpty(connectedSSID)) return 0; // No connection

                var wifiNetworks = NativeWifi.EnumerateBssNetworks()
                    .Where(network => network.Ssid.ToString() == connectedSSID)
                    .ToList();

                if (wifiNetworks.Any())
                {
                    return wifiNetworks.First().SignalStrength; // Return signal strength percentage
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting Wi-Fi signal strength: {ex.Message}");
            }

            return 0; // Default to 0 if unable to retrieve
        }


        private static string GetConnectedWiFiSSID()
        {
            try
            {
                var activeConnection = NativeWifi.EnumerateInterfaceConnections()
                    .FirstOrDefault(); // Get the first active connection

                if (activeConnection != null)
                {
                    return activeConnection.ProfileName; // SSID of the connected network
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting connected Wi-Fi SSID: {ex.Message}");
            }

            return string.Empty; // Return empty if not connected
        }



        private Dictionary<string, (long PrevSent, long PrevRecv)> previousData = new();
        private Dictionary<string, (long SentSpeed, long ReceiveSpeed)> GetNetworkSpeeds()
        {
            Dictionary<string, (long SentSpeed, long ReceiveSpeed)> adapterSpeeds = new();
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                IPv4InterfaceStatistics stats = nic.GetIPv4Statistics();
                string name = nic.Name;
                if (name.Length >= 3)
                {
                    previousData.TryGetValue(nic.Name, out var bits);
                    //calculate speed
                    long sentSpeed = (stats.BytesSent - bits.PrevSent);
                    long recvSpeed = (stats.BytesReceived - bits.PrevRecv);
                    //bytes to bits
                    sentSpeed = sentSpeed * 8;
                    recvSpeed = recvSpeed * 8;

                    adapterSpeeds[name] = (sentSpeed, recvSpeed);
                }
                previousData[name] = (stats.BytesSent, stats.BytesReceived);
            }
            return adapterSpeeds;
        }


        public class NetworkAdapterInfo : INotifyPropertyChanged
        {
            public string Name { get; set; }
            public string Type { get; set; }
            public string Status { get; set; }
            public string IPAddress { get; set; }
            public string Speed { get; set; }
            public string AdapterId { get; set; }

            private string sentSpeed;
            public string SentSpeed
            {
                get => sentSpeed;
                set
                {
                    if (sentSpeed != value)
                    {
                        sentSpeed = value;
                        OnPropertyChanged(nameof(SentSpeed));
                    }
                }
            }

            private string receiveSpeed;
            public string ReceiveSpeed
            {
                get => receiveSpeed;
                set
                {
                    if (receiveSpeed != value)
                    {
                        receiveSpeed = value;
                        OnPropertyChanged(nameof(ReceiveSpeed));
                    }
                }
            }

            // Graph Data for Speeds
            public ObservableCollection<long> DownloadSpeedValues { get; set; } = new ObservableCollection<long> { };
            public ObservableCollection<long> UploadSpeedValues { get; set; } = new ObservableCollection<long> { };

            public ISeries[] Series { get; set; }

            public NetworkAdapterInfo()
            {
                // Initialize the graph series
                Series = new ISeries[]
                {
                    new LineSeries<long>
                    {
                        Values = DownloadSpeedValues,
                        Fill = new SolidColorPaint(new SKColor(0, 200, 255, 100)),
                        GeometrySize = 0,
                        Stroke = new SolidColorPaint(new SKColor(0, 200, 255)), // Light blue for download
                        YToolTipLabelFormatter = point => BitsToHumanReadable(point.Model)

                    },
                    new LineSeries<long>
                    {
                        Values = UploadSpeedValues,
                        Fill = new SolidColorPaint(new SKColor(0, 255, 0, 100)),
                        GeometrySize = 0,
                        Stroke = new SolidColorPaint(new SKColor(0, 255, 0)), // Green for upload
                        YToolTipLabelFormatter = point => BitsToHumanReadable(point.Model)
                    }
                };
            }
            public Axis[] YAxes { get; set; } = new Axis[]
            {
                new Axis
                {
                    TextSize = 0,
                }
            };
            public Axis[] XAxes { get; set; } = new Axis[]
            {
                new Axis
                {
                    LabelsPaint = new SolidColorPaint(new SKColor(200, 200, 200))
                }
            };
            public SolidColorPaint TooltipBackgroundPaint { get; set; } = new SolidColorPaint(new SKColor(0, 0, 0, 0));
            public SolidColorPaint TooltipTextPaint { get; set; } = new SolidColorPaint(new SKColor(255, 255, 255));


            public event PropertyChangedEventHandler PropertyChanged;
            protected void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
        private void SetupTrayIcon()
        {
            trayIcon = new NotifyIcon
            {
                Icon = new Icon(new MemoryStream((byte[])XaiNet2.Properties.Resources.no_network_w)),
                Text = "XaiNet",
                Visible = true
            };

            // Create the context menu
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Change Adapter Options", null, OpenNetworkSettings);
            menu.Items.Add("Open Network & Internet Settings", null, OpenNetworkSettingsPage);
            menu.Items.Add("Show Network Info", null, ShowNetworkInfo);
            menu.Items.Add("Exit", null, ExitApp);

            trayIcon.ContextMenuStrip = menu;

            iconTimer = new System.Timers.Timer();
            iconTimer.Interval = 5000;
            iconTimer.Elapsed += IconSelector;
            iconTimer.AutoReset = true;
            iconTimer.Enabled = true;

            trayIcon.MouseClick += TrayIcon_MouseClick;
        }


        private static System.Timers.Timer iconTimer;

        static void OpenNetworkSettings(object sender, EventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "ncpa.cpl"),
                UseShellExecute = true
            });
        }

        static void OpenNetworkSettingsPage(object sender, EventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:network",
                UseShellExecute = true
            });
        }

        static void ShowNetworkInfo(object sender, EventArgs e)
        {
            string networkInfo = GetNetworkInfo();
            System.Windows.Forms.MessageBox.Show(networkInfo, "Network Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        static string GetNetworkInfo()
        {
            string info = "";
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus == OperationalStatus.Up) // Only show active adapters
                {
                    info += $"Adapter: {nic.Name}\n";
                    info += $"Status: {nic.OperationalStatus}\n";
                    info += $"Type: {nic.NetworkInterfaceType}\n";
                    info += $"Description: {nic.Description}\n";

                    // Get IP addresses
                    var ipProps = nic.GetIPProperties();
                    var ipAddresses = ipProps.UnicastAddresses
                        .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        .Select(a => a.Address.ToString());

                    if (ipAddresses.Any())
                        info += $"IP Address: {string.Join(", ", ipAddresses)}\n";

                    // Get Gateway
                    var gateway = ipProps.GatewayAddresses
                        .FirstOrDefault()?.Address.ToString() ?? "None";
                    info += $"Gateway: {gateway}\n";

                    // Get DNS Servers
                    var dnsServers = ipProps.DnsAddresses
                        .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        .Select(a => a.ToString());

                    if (dnsServers.Any())
                        info += $"DNS Servers: {string.Join(", ", dnsServers)}\n";

                    // Get Speeeeed
                    info += $"Speed: {BitsToHumanReadable(nic.Speed)}\n";

                    info += "\n--------------------------------\n";
                }
            }

            return string.IsNullOrEmpty(info) ? "No active network adapters found." : info;
        }


        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            WindowHelper.ApplyBlurEffect(this);
        }








        static string BitsToHumanReadable(long bits)
        {
            string[] units = { "b", "Kb", "Mb", "Gb", "Tb", "Pb", "Eb" };
            double size = bits;
            int unitIndex = 0;

            while (size >= 1000 && unitIndex < units.Length - 1)
            {
                size /= 1000;
                unitIndex++;
            }

            return $"{size:0.##} {units[unitIndex]}";
        }

        private void TrayIcon_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                this.Show();
                this.Activate();
                PositionWindowNearTray();
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

        private void OptionsButton_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("Options button has been pressed");
            OptionsWindow optionsWindow = new OptionsWindow(this);
            optionsWindow.ShowDialog();
        }

        private void WifiButton_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("Wireless button clicked");
            WirelessWindow wirelessWindow = new WirelessWindow(this);
            wirelessWindow.ShowDialog();
        }

        private void VPNButton_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("VPN button clicked");
            VPNWindow vpnWindow = new VPNWindow(this);
            vpnWindow.ShowDialog();
        }

        private void PositionWindowNearTray()
        {
            var screen = System.Windows.SystemParameters.WorkArea;
            this.Left = screen.Right - this.Width - 10;
            this.Top = screen.Bottom - this.Height - 10;
        }

        public void SetMyrkurMode(bool enable)
        {
            if (enable)
            {
                this.FontFamily = new System.Windows.Media.FontFamily("Comic Sans MS");
                Debug.WriteLine("Enabled MyrkurMode");
            }
            else
            {
                this.FontFamily = new System.Windows.Media.FontFamily("Segoe UI"); // Default
                Debug.WriteLine("Disabled MyrkurMode");
            }
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            if (!isPinned)
            {
                Hide(); // Hides the window when clicking outside unless pinned
            }
        }

        private void ExitApp(object sender, EventArgs e)
        {
            trayIcon.Visible = false;
            Application.Current.Shutdown();
        }
    }
}
