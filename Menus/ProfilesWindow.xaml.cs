using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ManagedNativeWifi;
using XaiNet2.Helpers;


namespace XaiNet2.Menus
{
    public partial class ProfilesWindow : Window
    {
        public class WiFiProfile
        {
            public string Name { get; set; } = string.Empty;
        }

        public ProfilesWindow(Window owner)
        {
            InitializeComponent();
            Owner = owner;
            PositionNearOwner();
            Loaded += OnLoaded;
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
            Debug.WriteLine($"Home icon loaded: {homeIco != null}");
            LoadProfiles();
        }
        private void LoadProfiles()
        {
            var profiles = new List<WiFiProfile>();

            try
            {
                var wifiAdapter = NativeWifi.EnumerateInterfaces().FirstOrDefault();
                if (wifiAdapter != null)
                {
                    profiles.AddRange(NativeWifi
                        .EnumerateProfiles()
                        .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                        .Select(p => new WiFiProfile { Name = p.Name }));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading profiles: {ex.Message}");
            }

            ProfilesList.ItemsSource = profiles;
        }
        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not WiFiProfile profile)
            {
                return;
            }

            var wifiAdapter = NativeWifi.EnumerateInterfaces().FirstOrDefault();
            if (wifiAdapter == null)
            {
                return;
            }

            try
            {
                NativeWifi.ConnectNetwork(
                    interfaceId: wifiAdapter.Id,
                    profileName: profile.Name,
                    bssType: BssType.Infrastructure);

                NotificationHelper.ShowToast(profile.Name, $"Connecting to {profile.Name}");
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

            var wifiAdapter = NativeWifi.EnumerateInterfaces().FirstOrDefault();
            if (wifiAdapter == null)
            {
                return;
            }

            try
            {
                if (NativeWifi.DeleteProfile(wifiAdapter.Id, profile.Name))
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
            Owner.Show();
        }
        private void Window_Deactivated(object sender, EventArgs e)
        {
            Hide();
        }

    }

}
