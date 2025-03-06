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
using XaiNet2;
using LiveChartsCore;
using System.Collections.ObjectModel;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView;
using SkiaSharp;

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
                    iconName = "wi-fi-w";
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

            var optionsIco = LoadImageFromResources(optionsIcon);

            if (optionsIco != null)
            {
                OptionsButton.Content = new System.Windows.Controls.Image
                {
                    Source = optionsIco,
                    Width = 16,
                    Height = 16
                };
            }

            string defaultIcon = "pin-outline";
            
            var defaultImage = LoadImageFromResources(defaultIcon);

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


        private void SpeedChecker(Object sender, EventArgs e)
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
                        string sentReadable = BitsToHumanReadable(sentSpeed);
                        string recvReadable = BitsToHumanReadable(recvSpeed);

                        adapter.SentSpeed = $"S: {sentReadable}/s";
                        adapter.ReceiveSpeed = $"R: {recvReadable}/s";
                        Debug.WriteLine($"Adapter: {adapter.Name} - Send: {adapter.SentSpeed}, Receive: {adapter.ReceiveSpeed}");
                        // Update Graph
                        if (adapter.DownloadSpeedValues.Count > 30) adapter.DownloadSpeedValues.RemoveAt(0);
                        if (adapter.UploadSpeedValues.Count > 30) adapter.UploadSpeedValues.RemoveAt(0);

                        adapter.DownloadSpeedValues.Add(recvReadable);
                        adapter.UploadSpeedValues.Add(sentReadable);
                    }

                }
            }
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
            public ObservableCollection<string> DownloadSpeedValues { get; set; } = new ObservableCollection<string> { };
            public ObservableCollection<string> UploadSpeedValues { get; set; } = new ObservableCollection<string> { };

            public ISeries[] Series { get; set; }

            public NetworkAdapterInfo()
            {
                // Initialize the graph series
                Series = new ISeries[]
                {
                    new LineSeries<string>
                    {
                        Values = DownloadSpeedValues,
                        Fill = new SolidColorPaint(new SKColor(0, 200, 255, 100)),
                        GeometrySize = 0,
                        Stroke = new SolidColorPaint(new SKColor(0, 200, 255)) // Light blue for download
                    },
                    new LineSeries<string>
                    {
                        Values = UploadSpeedValues,
                        Fill = new SolidColorPaint(new SKColor(0, 255, 0, 100)),
                        GeometrySize = 0,
                        Stroke = new SolidColorPaint(new SKColor(0, 255, 0)) // Green for upload
                    }
                };
            }
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

        private void TrayIcon_MouseClick(object sender, System.Windows.Forms.MouseEventArgs e)
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

            var newIcon = LoadImageFromResources(iconName);

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


        private void PositionWindowNearTray()
        {
            var screen = System.Windows.SystemParameters.WorkArea;
            this.Left = screen.Right - this.Width - 10;
            this.Top = screen.Bottom - this.Height - 10;
        }
        private static System.Windows.Media.Imaging.BitmapImage LoadImageFromResources(string resourceName)
        {
            object resource = XaiNet2.Properties.Resources.ResourceManager.GetObject(resourceName);

            if (resource == null)
            {
                return null;
            }

            try
            {
                if (resource is byte[] imageBytes) // WPF stores .ico as byte[]
                {
                    using (MemoryStream memory = new MemoryStream(imageBytes))
                    {
                        var bitmapImage = new System.Windows.Media.Imaging.BitmapImage();
                        bitmapImage.BeginInit();
                        bitmapImage.StreamSource = memory;
                        bitmapImage.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bitmapImage.EndInit();

                        return bitmapImage;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading image '{resourceName}': {ex.Message}");
            }

            return null;
        }


        private void Window_Deactivated(object sender, EventArgs e)
        {
            if (!isPinned)
            {
                this.Hide(); // Hides the window when clicking outside unless pinned
            }
        }

        private void ExitApp(object sender, EventArgs e)
        {
            trayIcon.Visible = false;
            Application.Current.Shutdown();
        }
    }
}
