using ManagedNativeWifi;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using XaiNet2.Helpers;

namespace XaiNet2.Menus
{
    public partial class ProfilesWindow : Window
    {
        public class WiFiProfile
        {
            public string Name { get; set; } = string.Empty;
            public Guid InterfaceId { get; set; }
        }

        private bool isPinned;

        public ProfilesWindow(Window owner)
        {
            InitializeComponent();
            Owner = owner;
            PositionNearOwner();
            Loaded += OnLoaded;
            bool myrkurModeEnabled = Properties.Settings.Default.MyrkurMode;
            this.SetMyrkurMode(myrkurModeEnabled);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            HomeButton.Content = ImageLoader.CreateIcon("home");
            PinButton.Content = ImageLoader.CreateIcon(isPinned ? "pin-solid" : "pin-outline");
            LoadProfiles();
        }

        private void LoadProfiles()
        {
            var profiles = new List<WiFiProfile>();

            try
            {
                // Use each profile's own interface so connect/delete target the right adapter.
                // Guard per profile so one bad entry (e.g. an interface that was unplugged) doesn't
                // drop the entire list.
                foreach (var p in NativeWifi.EnumerateProfiles())
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(p.Name)) continue;
                        profiles.Add(new WiFiProfile { Name = p.Name, InterfaceId = p.Interface.Id });
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Skipping a profile: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading profiles: {ex.Message}");
            }

            ProfilesList.ItemsSource = profiles;
            NoProfilesTextBlock.Visibility = profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not WiFiProfile profile)
            {
                return;
            }

            try
            {
                NotificationHelper.ShowToast(profile.Name, $"Connecting to {profile.Name}…");

                // Await the real association result instead of a fire-and-forget request, so we can
                // tell the user when it actually fails (out of range, wrong key, radio off…).
                bool connected = await NativeWifi.ConnectNetworkAsync(
                    interfaceId: profile.InterfaceId,
                    profileName: profile.Name,
                    bssType: BssType.Infrastructure,
                    timeout: TimeSpan.FromSeconds(12));

                NotificationHelper.ShowToast(profile.Name,
                    connected
                        ? $"Connected to {profile.Name}"
                        : $"Couldn't connect to {profile.Name} (out of range, wrong key, or Wi-Fi off?)");
                LoadProfiles();
            }
            catch (Exception ex)
            {
                NotificationHelper.ShowToast("Error", ex.Message);
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not WiFiProfile profile)
            {
                return;
            }

            try
            {
                if (NativeWifi.DeleteProfile(profile.InterfaceId, profile.Name))
                {
                    NotificationHelper.ShowToast(profile.Name, "Profile deleted");
                    LoadProfiles();
                }
                else
                {
                    NotificationHelper.ShowToast("Error", "Failed to delete profile");
                }
            }
            catch (Exception ex)
            {
                NotificationHelper.ShowToast("Error", ex.Message);
            }
        }

        // Create a profile for a network by name (hidden SSIDs, WEP, Enterprise, …) and try to connect.
        private void AddNetworkButton_Click(object sender, RoutedEventArgs e)
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
                error ?? $"Profile saved — connecting to {dialog.NetworkSsid}…");
            LoadProfiles();
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            isPinned = !isPinned;
            Topmost = isPinned;
            PinButton.Content = ImageLoader.CreateIcon(isPinned ? "pin-solid" : "pin-outline");
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            WindowHelper.ApplyBlurEffect(this);
        }

        private void PositionNearOwner()
        {
            if (Owner != null)
            {
                Left = Owner.Left;
                Top = Owner.Top;
                Width = Owner.Width;
                Height = Owner.Height;
            }
        }

        private void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
            Owner?.Show();
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            // Don't hide while pinned or while a child dialog (e.g. the add-network prompt) is open.
            if (!isPinned && OwnedWindows.Count == 0)
            {
                Hide();
            }
        }
    }
}
