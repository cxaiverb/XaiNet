using System;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;
using System.Net.NetworkInformation;
using System.Net.Sockets;
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
using ManagedNativeWifi;
using XaiNet2.Helpers;

namespace XaiNet2.Menus
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
            var screen = GetScreenAtCursor();
            PositionWindowNearTray(screen);
            this.Hide();
            updateTimer = new DispatcherTimer();
            updateTimer.Interval = TimeSpan.FromSeconds(1);
            updateTimer.Tick += SpeedChecker;
            updateTimer.Start();

            this.Loaded += OnLoaded;
            this.Closed += OnClosed;

            // VPN auto-connect: react to any adapter address change, not just WiFi-icon ticks
            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;

            bool myrkurModeEnabled = XaiNet2.Properties.Settings.Default.MyrkurMode;
            this.SetMyrkurMode(myrkurModeEnabled);

        }

        private void OnNetworkAddressChanged(object sender, EventArgs e)
        {
            // Fires on a thread-pool thread. Do the VPN auto-connect work here (thread-safe),
            // and marshal the adapter-list refresh onto the UI thread.
            try
            {
                if (OpenVPNManager.IsInstalled)
                {
                    var wifi = NetworkInterface.GetAllNetworkInterfaces()
                        .FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up
                                          && n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
                    string ssid = wifi != null ? GetConnectedWiFiSSID(ParseAdapterId(wifi.Id)) : null;
                    OpenVPNManager.HandleNetworkChange(ssid);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"NetworkAddressChanged handler failed: {ex.Message}");
                Logger.Error("NetworkAddressChanged handler failed", ex);
            }

            // Update the tray icon right away rather than waiting for the next 5s tick.
            try { SelectAndApplyTrayIcon(); }
            catch (Exception ex) { Debug.WriteLine($"Tray refresh on network change failed: {ex.Message}"); }

            var dispatcher = Application.Current?.Dispatcher;
            dispatcher?.BeginInvoke(new Action(() =>
            {
                try { RefreshAdapterMetadata(); }
                catch (Exception ex)
                {
                    Debug.WriteLine($"RefreshAdapterMetadata failed: {ex.Message}");
                    Logger.Error("RefreshAdapterMetadata failed", ex);
                }
            }));
        }

        // NIC IDs are GUID strings on Windows, but parse defensively so a single odd adapter
        // can't take down the whole adapter list.
        private static Guid ParseAdapterId(string id)
            => Guid.TryParse(id, out var g) ? g : Guid.Empty;

        private void OnClosed(object sender, EventArgs e)
        {
            NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
            updateTimer?.Stop();
            updateTimer = null;
            if (iconTimer != null)
            {
                iconTimer.Stop();
                iconTimer.Elapsed -= IconSelector;
                iconTimer.Dispose();
                iconTimer = null;
            }
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                var oldIcon = trayIcon.Icon;
                trayIcon.Icon = null;
                oldIcon?.Dispose();
                trayIcon.Dispose();
                trayIcon = null;
            }
            currentTrayIcon?.Dispose();
            currentTrayIcon = null;
        }

        // Lightweight reachability check: any active adapter with a non-loopback IP.
        // Replaces the previous ICMP ping to google.com which fails on networks blocking ICMP.
        static bool HasNetworkConnectivity()
        {
            try
            {
                if (!NetworkInterface.GetIsNetworkAvailable()) return false;
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    var props = nic.GetIPProperties();
                    if (props.UnicastAddresses.Any(a =>
                        a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HasNetworkConnectivity failed: {ex.Message}");
            }
            return false;
        }

        static void IconSelector(Object source, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                SelectAndApplyTrayIcon();
            }
            catch (Exception ex)
            {
                // System.Timers.Timer swallows handler exceptions, so capture them ourselves.
                Debug.WriteLine($"IconSelector failed: {ex.Message}");
                Logger.Error("IconSelector failed", ex);
            }
        }

        static void SelectAndApplyTrayIcon()
        {
            string iconName = "no-network-w"; // Default to no network

            if (HasNetworkConnectivity())
            {
                NetworkInterface activeEthernet = null;
                NetworkInterface activeWiFi = null;

                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up)
                    {
                        continue;
                    }
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    {
                        activeWiFi = nic;
                    }
                    else if (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                    {
                        activeEthernet = nic;
                    }
                }

                bool vpnUp = IsVpnConnected();

                // Prioritize Wi-Fi over Ethernet if both are available
                if (activeWiFi != null)
                {
                    if (vpnUp)
                    {
                        iconName = "wifi-vpn";
                    }
                    else
                    {
                        // ManagedNativeWifi reports SignalStrength as a 0-100 percentage,
                        // not RSSI in dBm. Pick a bar count from the percentage.
                        int signal = GetWiFiSignalStrength(ParseAdapterId(activeWiFi.Id));
                        Debug.WriteLine($"Active Wi-Fi ID: {activeWiFi.Id} for {activeWiFi.Name}");
                        Debug.WriteLine($"Wi-Fi Signal Strength: {signal}%");

                        if (signal >= 90) iconName = "wi-fi-full";
                        else if (signal >= 70) iconName = "wi-fi-4";
                        else if (signal >= 50) iconName = "wi-fi-3";
                        else if (signal >= 25) iconName = "wi-fi-2";
                        else iconName = "wi-fi-1";
                    }
                }
                else if (activeEthernet != null)
                {
                    iconName = vpnUp ? "wired-vpn" : "wired-network-connection-w";
                }
            }

            UpdateTrayIcon(iconName);
        }

        // Heuristic VPN detection for the tray icon: an OpenVPN connection we started, a tunnel-type
        // adapter that's up, or an up adapter whose name/description looks like a VPN driver.
        private static bool IsVpnConnected()
        {
            try
            {
                if (OpenVPNManager.IsInstalled && OpenVPNManager.HasActiveConnections) return true;
            }
            catch { /* ignore */ }

            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) return true;

                    string id = $"{nic.Description} {nic.Name}".ToLowerInvariant();
                    if (id.Contains("vpn") || id.Contains("openvpn") || id.Contains("wireguard")
                        || id.Contains("tailscale") || id.Contains("tap-windows") || id.Contains("wintun"))
                    {
                        return true;
                    }
                }
            }
            catch { /* ignore */ }

            return false;
        }

        // Tracks the Icon currently held by trayIcon so we can dispose it on swap.
        // NotifyIcon does not own its Icon — assigning a new one leaks the old GDI handle.
        private static Icon currentTrayIcon;

        static void UpdateTrayIcon(string iconName)
        {
            var bytes = XaiNet2.Properties.Resources.ResourceManager.GetObject(iconName) as byte[];
            if (bytes == null) return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            void apply()
            {
                if (trayIcon == null) return;
                Icon newIcon;
                using (var ms = new MemoryStream(bytes))
                {
                    // Load the frame that matches the tray's small-icon size (scales with DPI) so the
                    // icon stays crisp instead of NotifyIcon down-scaling a large frame.
                    newIcon = new Icon(ms, SystemInformation.SmallIconSize);
                }
                var old = currentTrayIcon;
                currentTrayIcon = newIcon;
                trayIcon.Icon = newIcon;
                old?.Dispose();
            }

            if (dispatcher.CheckAccess()) apply();
            else dispatcher.Invoke(apply);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("Loading icons...");
            ApplyNerdStats();

            OptionsButton.Content = ImageLoader.CreateIcon("options");
            WifiButton.Content = ImageLoader.CreateIcon("wi-fi-full");
            PinButton.Content = ImageLoader.CreateIcon("pin-outline");

            if (OpenVPNManager.IsInstalled)
            {
                VPNButton.Content = ImageLoader.CreateIcon("openvpn");
            }
            else
            {
                VPNButton.Visibility = Visibility.Collapsed;
            }

            if (!TailscaleManager.IsInstalled)
            {
                TailscaleButton.Visibility = Visibility.Collapsed;
            }

            // Show the correct tray icon immediately instead of waiting for the first 5s tick,
            // and auto-connect any VPN bound to the network we're already on.
            SelectAndApplyTrayIcon();
            TriggerVpnAutoConnect();
        }

        // Checks the current Wi-Fi SSID and lets OpenVPNManager auto-connect a profile bound to it.
        private static void TriggerVpnAutoConnect()
        {
            if (!OpenVPNManager.IsInstalled) return;
            try
            {
                var wifi = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up
                                      && n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
                string ssid = wifi != null ? GetConnectedWiFiSSID(ParseAdapterId(wifi.Id)) : null;
                OpenVPNManager.HandleNetworkChange(ssid);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TriggerVpnAutoConnect failed: {ex.Message}");
            }
        }

        private List<NetworkAdapterInfo> allAdapters = new List<NetworkAdapterInfo>(); // Store all adapters
        private HashSet<string> visibleAdapters = new HashSet<string>(); // Store user-selected visible adapters
        // Every adapter id we've ever shown this session. Used so a hidden adapter that disappears
        // and comes back isn't force-shown again — only truly first-seen adapters auto-show.
        private readonly HashSet<Guid> knownAdapterIds = new HashSet<Guid>();

        private void LoadNetworkAdapters()
        {
            allAdapters.Clear(); // Reset the list

            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!Guid.TryParse(nic.Id, out var gid)) continue;
                knownAdapterIds.Add(gid);
                allAdapters.Add(CreateAdapterInfo(nic, gid));
            }

            LoadSavedAdapterSettings(); // Ensure visibility settings are applied
        }

        private static NetworkAdapterInfo CreateAdapterInfo(NetworkInterface nic, Guid gid)
        {
            var info = new NetworkAdapterInfo { Name = nic.Name, AdapterId = gid };
            PopulateAdapterInfo(info, nic);
            return info;
        }

        // Fills the live/display fields from a NIC. Safe to call repeatedly — NetworkAdapterInfo
        // raises change notifications, so bound UI updates in place.
        private static void PopulateAdapterInfo(NetworkAdapterInfo info, NetworkInterface nic)
        {
            var ipProps = nic.GetIPProperties();
            var ipv4 = ipProps.UnicastAddresses
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.Address.ToString())
                .ToList();

            string netName = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
                             && nic.OperationalStatus == OperationalStatus.Up
                ? GetConnectedWiFiSSID(ParseAdapterId(nic.Id))
                : nic.Name;
            if (string.IsNullOrEmpty(netName)) netName = nic.Name;

            info.NetName = netName;
            info.Type = $"Type: {nic.NetworkInterfaceType}";
            info.Status = $"Status: {nic.OperationalStatus}";
            info.IPAddress = ipv4.Count > 0 ? $"IP: {string.Join(", ", ipv4)}" : "IP: None";
            info.Speed = nic.Speed;
            info.Description = $"Adapter: {nic.Description}";
            info.Mac = $"MAC: {FormatMac(nic.GetPhysicalAddress())}";

            var gateway = ipProps.GatewayAddresses.FirstOrDefault()?.Address?.ToString();
            info.Gateway = $"Gateway: {(string.IsNullOrEmpty(gateway) ? "None" : gateway)}";

            var dns = ipProps.DnsAddresses.Select(a => a.ToString()).ToList();
            info.Dns = dns.Count > 0 ? $"DNS: {string.Join(", ", dns)}" : "DNS: None";
        }

        private static string FormatMac(PhysicalAddress mac)
        {
            var bytes = mac?.GetAddressBytes();
            if (bytes == null || bytes.Length == 0) return "None";
            return string.Join(":", bytes.Select(b => b.ToString("X2")));
        }

        // Re-reads NIC metadata into the existing adapter objects (in place, so expander/graph
        // state is preserved) and adds/removes adapters that appeared or went away.
        private void RefreshAdapterMetadata()
        {
            var seen = new HashSet<Guid>();
            bool setChanged = false;

            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!Guid.TryParse(nic.Id, out var gid)) continue;
                seen.Add(gid);

                var existing = allAdapters.FirstOrDefault(a => a.AdapterId == gid);
                if (existing == null)
                {
                    var info = CreateAdapterInfo(nic, gid);
                    allAdapters.Add(info);
                    // Show by default only the first time we ever see this adapter; if it was hidden
                    // by the user and later reappears, leave its (hidden) visibility alone.
                    if (knownAdapterIds.Add(gid))
                    {
                        visibleAdapters.Add(info.Name);
                    }
                    setChanged = true;
                }
                else
                {
                    PopulateAdapterInfo(existing, nic);
                }
            }

            if (allAdapters.RemoveAll(a => !seen.Contains(a.AdapterId)) > 0)
            {
                setChanged = true;
            }

            // Only rebuild the bound list when the *set* of adapters changed; in-place field updates
            // flow through INotifyPropertyChanged, so we avoid a rebuild that would collapse expanders.
            if (setChanged)
            {
                ApplyNerdStats();
                RefreshAdapters();
            }
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
            var visible = allAdapters
                .Where(adapter => visibleAdapters.Contains(adapter.Name))
                .ToList();

            // With a single adapter showing, expand it by default for convenience.
            if (visible.Count == 1)
            {
                visible[0].IsExpanded = true;
            }

            NetworkList.ItemsSource = visible;
            Debug.WriteLine($"Adapters displayed: {visible.Count}");
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


        private int speedTick;

        public void SpeedChecker(Object sender, EventArgs e)
        {
            var adapterSpeeds = GetNetworkSpeeds();
            foreach (var item in NetworkList.Items)
            {
                if (item is NetworkAdapterInfo adapter)
                {
                    long sentSpeed = 0;
                    long recvSpeed = 0;

                    if (adapterSpeeds.TryGetValue(adapter.AdapterId, out var speeds))
                    {
                        sentSpeed = speeds.SentSpeed;
                        recvSpeed = speeds.ReceiveSpeed;
                    }
                    adapter.SentSpeed = sentSpeed;
                    adapter.ReceiveSpeed = recvSpeed;

                    // Update Graph
                    if (adapter.DownloadSpeedValues.Count > 30) adapter.DownloadSpeedValues.RemoveAt(0);
                    if (adapter.UploadSpeedValues.Count > 30) adapter.UploadSpeedValues.RemoveAt(0);

                    adapter.UploadSpeedValues.Add(sentSpeed);
                    adapter.DownloadSpeedValues.Add(recvSpeed);
                }
            }

            // Refresh adapter metadata (status / IP / SSID / DNS) every ~5s so the popup isn't frozen
            // at startup state. Runs on the UI thread (DispatcherTimer), so touching the list is safe.
            if (++speedTick % 5 == 0)
            {
                RefreshAdapterMetadata();
            }
        }

        private static int GetWiFiSignalStrength(Guid adapterid)
        {
            try
            {
                string connectedSSID = GetConnectedWiFiSSID(adapterid);
                if (string.IsNullOrEmpty(connectedSSID)) return 0; // No connection

                var wifiNetworks = NativeWifi.EnumerateBssNetworks()
                    .Where(network => network.Interface.Id == adapterid)
                    .FirstOrDefault();

                if (wifiNetworks != null)
                {
                    return wifiNetworks.SignalStrength; // Return signal strength percentage
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting Wi-Fi signal strength: {ex.Message}");
            }

            return 0; // Default to 0 if unable to retrieve
        }


        private static string GetConnectedWiFiSSID(Guid adapterid)
        {
            try
            {
                var activeConnection = NativeWifi.EnumerateInterfaceConnections().FirstOrDefault(x => x.Id == adapterid);

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



        // Keyed by nic.Id (stable GUID) rather than nic.Name so that renamed adapters or
        // duplicate localized names don't poison the previous-byte counters.
        private Dictionary<string, (long PrevSent, long PrevRecv)> previousData = new();
        private Dictionary<Guid, (long SentSpeed, long ReceiveSpeed)> GetNetworkSpeeds()
        {
            Dictionary<Guid, (long SentSpeed, long ReceiveSpeed)> adapterSpeeds = new();
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!Guid.TryParse(nic.Id, out var gid)) continue;
                IPv4InterfaceStatistics stats = nic.GetIPv4Statistics();
                string id = nic.Id;
                if (previousData.TryGetValue(id, out var bits))
                {
                    long sentSpeed = (stats.BytesSent - bits.PrevSent) * 8;
                    long recvSpeed = (stats.BytesReceived - bits.PrevRecv) * 8;
                    adapterSpeeds[gid] = (sentSpeed, recvSpeed);
                }
                previousData[id] = (stats.BytesSent, stats.BytesReceived);
            }
            return adapterSpeeds;
        }


        public class NetworkAdapterInfo : INotifyPropertyChanged
        {
            public string Name { get; set; }
            public Guid AdapterId { get; set; }

            private string netName;
            public string NetName { get => netName; set { if (netName != value) { netName = value; OnPropertyChanged(nameof(NetName)); } } }

            private string type;
            public string Type { get => type; set { if (type != value) { type = value; OnPropertyChanged(nameof(Type)); } } }

            private string status;
            public string Status { get => status; set { if (status != value) { status = value; OnPropertyChanged(nameof(Status)); } } }

            private string ipAddress;
            public string IPAddress { get => ipAddress; set { if (ipAddress != value) { ipAddress = value; OnPropertyChanged(nameof(IPAddress)); } } }

            private long speed;
            public long Speed { get => speed; set { if (speed != value) { speed = value; OnPropertyChanged(nameof(Speed)); } } }

            // Nerd Stats fields.
            private string description;
            public string Description { get => description; set { if (description != value) { description = value; OnPropertyChanged(nameof(Description)); } } }

            private string mac;
            public string Mac { get => mac; set { if (mac != value) { mac = value; OnPropertyChanged(nameof(Mac)); } } }

            private string gateway;
            public string Gateway { get => gateway; set { if (gateway != value) { gateway = value; OnPropertyChanged(nameof(Gateway)); } } }

            private string dns;
            public string Dns { get => dns; set { if (dns != value) { dns = value; OnPropertyChanged(nameof(Dns)); } } }

            private long sentSpeed;
            public long SentSpeed
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

            private long receiveSpeed;
            public long ReceiveSpeed
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

            // Tab widths bound from MainWindow.xaml. Defaults match NerdStats=false.
            // Setting NerdTab to 0 collapses the Nerd Stats tab visually.
            private double speedTab = 140;
            public double SpeedTab
            {
                get => speedTab;
                set { if (speedTab != value) { speedTab = value; OnPropertyChanged(nameof(SpeedTab)); } }
            }

            private double detailsTab = 140;
            public double DetailsTab
            {
                get => detailsTab;
                set { if (detailsTab != value) { detailsTab = value; OnPropertyChanged(nameof(DetailsTab)); } }
            }

            private double nerdTab = 100;
            public double NerdTab
            {
                get => nerdTab;
                set { if (nerdTab != value) { nerdTab = value; OnPropertyChanged(nameof(NerdTab)); } }
            }

            // Bound (two-way) to the adapter's Expander so it can be expanded programmatically
            // (e.g. when it's the only adapter showing) while still tracking manual toggles.
            private bool isExpanded;
            public bool IsExpanded
            {
                get => isExpanded;
                set { if (isExpanded != value) { isExpanded = value; OnPropertyChanged(nameof(IsExpanded)); } }
            }

            // Drives the Nerd Stats tab's Visibility. Defaults to hidden (matches NerdStats=false).
            private bool nerdStatsVisible;
            public bool NerdStatsVisible
            {
                get => nerdStatsVisible;
                set { if (nerdStatsVisible != value) { nerdStatsVisible = value; OnPropertyChanged(nameof(NerdStatsVisible)); } }
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
                        YToolTipLabelFormatter = point => new BitsToHumanConverter().Convert(point.Model, null, null, null).ToString()

                    },
                    new LineSeries<long>
                    {
                        Values = UploadSpeedValues,
                        Fill = new SolidColorPaint(new SKColor(0, 255, 0, 100)),
                        GeometrySize = 0,
                        Stroke = new SolidColorPaint(new SKColor(0, 255, 0)), // Green for upload
                        YToolTipLabelFormatter = point => new BitsToHumanConverter().Convert(point.Model, null, null, null).ToString()
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

        public void ApplyNerdStats()
        {
            bool nerdStatsEnabled = XaiNet2.Properties.Settings.Default.NerdStats;

            // With Nerd Stats off there are two tabs (wider); on, three tabs (narrower).
            double speedTab = nerdStatsEnabled ? 100 : 140;
            double detailsTab = nerdStatsEnabled ? 100 : 140;

            foreach (var adapter in allAdapters)
            {
                adapter.SpeedTab = speedTab;
                adapter.DetailsTab = detailsTab;
                adapter.NerdTab = 100;
                adapter.NerdStatsVisible = nerdStatsEnabled;
            }
        }
        private void SetupTrayIcon()
        {
            using (var ms = new MemoryStream((byte[])XaiNet2.Properties.Resources.no_network_w))
            {
                currentTrayIcon = new Icon(ms, SystemInformation.SmallIconSize);
            }
            trayIcon = new NotifyIcon
            {
                Icon = currentTrayIcon,
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
            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, "ncpa.cpl"),
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to open ncpa.cpl: {ex.Message}");
            }
        }

        static void OpenNetworkSettingsPage(object sender, EventArgs e)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "ms-settings:network",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to open ms-settings:network: {ex.Message}");
            }
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
                    info += $"MAC Address: {nic.GetPhysicalAddress()}\n";

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
                    info += $"Speed: {new BitsToHumanConverter().Convert(nic.Speed, null, null, null).ToString()}\n";

                    info += "\n--------------------------------\n";
                }
            }

            return string.IsNullOrEmpty(info) ? "No active network adapters found." : info;
        }


        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            WindowHelper.ApplyBlurEffect(this);
        }



        private void TrayIcon_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                this.Show();
                this.Activate();
                var screen = GetScreenAtCursor();
                PositionWindowNearTray(screen);
            }
        }
        private bool isPinned = false;
        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            isPinned = !isPinned; // Toggle state
            Topmost = isPinned;   // Keep window on top when pinned
            PinButton.Content = ImageLoader.CreateIcon(isPinned ? "pin-solid" : "pin-outline");
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
            if (!OpenVPNManager.IsInstalled)
            {
                return;
            }
            Debug.WriteLine("VPN button clicked");
            VPNWindow vpnWindow = new VPNWindow(this);
            vpnWindow.ShowDialog();
        }

        private void TailscaleButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TailscaleManager.IsInstalled)
            {
                return;
            }
            Debug.WriteLine("Tailscale button clicked");
            TailscaleWindow tailscaleWindow = new TailscaleWindow(this);
            tailscaleWindow.ShowDialog();
        }

        private void PositionWindowNearTray(Screen screen)
        {
            if (screen == null)
            {
                // If there is no specific screen, use primary
                screen = Screen.PrimaryScreen;
            }

            Left = screen.WorkingArea.Right - Width - 10;
            Top = screen.WorkingArea.Bottom - Height - 10;
        }
        private Screen GetScreenAtCursor()
        {
            foreach(var screen in Screen.AllScreens)
            {
                if (screen.Bounds.Contains(System.Windows.Forms.Control.MousePosition))
                {
                    return screen;
                }
            }
            return Screen.PrimaryScreen;
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
            if (trayIcon != null) trayIcon.Visible = false;
            Application.Current.Shutdown();
        }
    }
}
