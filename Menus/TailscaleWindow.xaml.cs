using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using XaiNet2.Helpers;

namespace XaiNet2.Menus
{
    public partial class TailscaleWindow : Window
    {
        private static readonly Brush OnlineBrush = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99));
        private static readonly Brush AmberBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0xC8, 0x42));
        private bool isPinned;

        public TailscaleWindow(MainWindow owner)
        {
            InitializeComponent();
            Owner = owner;
            PositionNearMainWindow(owner);

            bool myrkurModeEnabled = Properties.Settings.Default.MyrkurMode;
            this.SetMyrkurMode(myrkurModeEnabled);
        }

        // Row in the device list.
        public class DeviceView
        {
            public string Name { get; set; } = string.Empty;
            public string Subtitle { get; set; } = string.Empty;
            public bool Online { get; set; }
            public bool IsExitNode { get; set; }
            public Brush StatusBrush => Online ? OnlineBrush : Brushes.Gray;
        }

        // Item in the exit-node combo. Empty Ip == "None".
        public class ExitNodeChoice
        {
            public string Label { get; set; } = string.Empty;
            public string Ip { get; set; } = string.Empty;

            // The custom ComboBox template's selection box falls back to ToString(), so make that
            // the label rather than the type name.
            public override string ToString() => Label;
        }

        private async Task RefreshAsync()
        {
            SetBusy(true);

            var status = await TailscaleManager.GetStatusAsync();
            if (status == null)
            {
                StatusText.Text = "Unavailable";
                StatusDot.Foreground = Brushes.Gray;
                SelfText.Text = "Couldn't query Tailscale. Is the Tailscale service running?";
                DevicesList.ItemsSource = null;
                NoDevicesText.Visibility = Visibility.Visible;
                ExitNodeCombo.ItemsSource = null;
                SetBusy(false);
                ConnectButton.IsEnabled = true;
                DisconnectButton.IsEnabled = false;
                return;
            }

            string state = status.BackendState ?? "Unknown";
            bool running = string.Equals(state, "Running", StringComparison.OrdinalIgnoreCase);
            ApplyStateLabel(state);

            var self = status.Self;
            string selfIp = TailscaleManager.FirstIPv4(self?.TailscaleIPs ?? status.TailscaleIPs);
            string host = !string.IsNullOrEmpty(self?.HostName) ? self!.HostName : "this device";
            SelfText.Text = string.IsNullOrEmpty(selfIp)
                ? $"This device: {host}"
                : $"This device: {host}  ·  {selfIp}";

            var peers = (status.Peer?.Values ?? Enumerable.Empty<TailscaleNode>()).ToList();

            var devices = peers
                .OrderByDescending(p => p.Online)
                .ThenBy(p => p.HostName, StringComparer.OrdinalIgnoreCase)
                .Select(p => new DeviceView
                {
                    Name = DisplayName(p),
                    Online = p.Online,
                    IsExitNode = p.ExitNode,
                    Subtitle = BuildSubtitle(p),
                })
                .ToList();
            DevicesList.ItemsSource = devices;
            NoDevicesText.Visibility = devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var choices = new List<ExitNodeChoice> { new ExitNodeChoice { Label = "None", Ip = string.Empty } };
            foreach (var p in peers.Where(p => p.ExitNodeOption)
                                   .OrderBy(p => p.HostName, StringComparer.OrdinalIgnoreCase))
            {
                string ip = TailscaleManager.FirstIPv4(p.TailscaleIPs);
                choices.Add(new ExitNodeChoice { Label = $"{DisplayName(p)} ({ip})", Ip = ip });
            }
            ExitNodeCombo.ItemsSource = choices;

            string activeIp = TailscaleManager.FirstIPv4(peers.FirstOrDefault(p => p.ExitNode)?.TailscaleIPs);
            ExitNodeCombo.SelectedItem = choices.FirstOrDefault(c => c.Ip == activeIp) ?? choices[0];

            SetBusy(false);
            // These two reflect connection state, so set them after SetBusy re-enables everything.
            ConnectButton.IsEnabled = !running;
            DisconnectButton.IsEnabled = running;
        }

        private void ApplyStateLabel(string state)
        {
            switch (state)
            {
                case "Running":
                    StatusText.Text = "Connected"; StatusDot.Foreground = OnlineBrush; break;
                case "Stopped":
                    StatusText.Text = "Stopped"; StatusDot.Foreground = Brushes.Gray; break;
                case "NeedsLogin":
                    StatusText.Text = "Needs login"; StatusDot.Foreground = AmberBrush; break;
                case "Starting":
                    StatusText.Text = "Starting…"; StatusDot.Foreground = AmberBrush; break;
                default:
                    StatusText.Text = state; StatusDot.Foreground = Brushes.Gray; break;
            }
        }

        private static string DisplayName(TailscaleNode node)
        {
            if (!string.IsNullOrEmpty(node.HostName)) return node.HostName;
            var dns = node.DNSName;
            if (!string.IsNullOrEmpty(dns)) return dns.Split('.')[0];
            return TailscaleManager.FirstIPv4(node.TailscaleIPs);
        }

        private static string BuildSubtitle(TailscaleNode node)
        {
            string ip = TailscaleManager.FirstIPv4(node.TailscaleIPs);
            string os = node.OS ?? string.Empty;
            if (!string.IsNullOrEmpty(ip) && !string.IsNullOrEmpty(os)) return $"{ip}  ·  {os}";
            return string.IsNullOrEmpty(ip) ? os : ip;
        }

        private void SetBusy(bool busy)
        {
            RefreshButton.IsEnabled = !busy;
            ConnectButton.IsEnabled = !busy;
            DisconnectButton.IsEnabled = !busy;
            LogoutButton.IsEnabled = !busy;
            ApplyExitNodeButton.IsEnabled = !busy;
            ExitNodeCombo.IsEnabled = !busy;
        }

        private void ShowMessage(string message)
        {
            MessageText.Text = message;
            MessageText.Visibility = Visibility.Visible;
        }

        private void HideMessage()
        {
            MessageText.Visibility = Visibility.Collapsed;
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            HideMessage();
            SetBusy(true);
            var error = await TailscaleManager.UpAsync();
            if (error != null) ShowMessage(error);
            await RefreshAsync();
        }

        private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            HideMessage();
            SetBusy(true);
            var error = await TailscaleManager.DownAsync();
            if (error != null) ShowMessage(error);
            await RefreshAsync();
        }

        private async void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            HideMessage();
            SetBusy(true);
            var error = await TailscaleManager.LogoutAsync();
            if (error != null) ShowMessage(error);
            await RefreshAsync();
        }

        private async void ApplyExitNodeButton_Click(object sender, RoutedEventArgs e)
        {
            if (ExitNodeCombo.SelectedItem is not ExitNodeChoice choice) return;
            HideMessage();
            SetBusy(true);
            var error = await TailscaleManager.SetExitNodeAsync(choice.Ip);
            if (error != null) ShowMessage(error);
            await RefreshAsync();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            HideMessage();
            await RefreshAsync();
        }

        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
            Owner?.Show();
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            isPinned = !isPinned;
            Topmost = isPinned;
            SetButtonIcon(PinButton, isPinned ? "pin-solid" : "pin-outline");
        }

        private static void SetButtonIcon(Button button, string iconName)
            => button.Content = ImageLoader.CreateIcon(iconName);

        private void PositionNearMainWindow(MainWindow owner)
        {
            Left = owner.Left;
            Top = owner.Top;
            Width = owner.Width;
            Height = owner.Height;
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            if (isPinned) return;
            this.Hide();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            WindowHelper.ApplyBlurEffect(this);
            SetButtonIcon(HomeButton, "home");
            SetButtonIcon(RefreshButton, "refresh");
            SetButtonIcon(PinButton, isPinned ? "pin-solid" : "pin-outline");
            await RefreshAsync();
        }
    }
}
