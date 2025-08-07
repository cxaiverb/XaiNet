using NetworkTrayApp;
using System;
using System.Collections.Generic;
using System.Linq;
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
using XaiNet2.Helpers;
using Microsoft.Win32;
using Microsoft.VisualBasic;
using System.IO;
using System.Diagnostics;

namespace XaiNet2 
{ 

    public partial class VPNWindow : Window
    {
        public VPNWindow(MainWindow owner)
        {
            InitializeComponent();
            this.Owner = owner;
            PositionNearMainWindow(owner);

            bool myrkurModeEnabled = Properties.Settings.Default.MyrkurMode;
            this.SetMyrkurMode(myrkurModeEnabled);

            LoadProfiles();
        }
        private void LoadProfiles()
        {
            VpnProfilesList.ItemsSource = null;
            VpnProfilesList.ItemsSource = OpenVPNManager.GetProfiles();
        }

        private void AddProfileButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "OpenVPN config (*.ovpn)|*.ovpn"
            };
            if (dialog.ShowDialog() == true)
            {
                OpenVPNManager.AddProfile(dialog.FileName);
                LoadProfiles();
            }
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is OpenVpnProfile profile)
            {
                OpenVPNManager.Connect(profile.Name);
            }
        }

        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is OpenVpnProfile profile)
            {
                OpenVPNManager.Disconnect(profile.Name);
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is OpenVpnProfile profile)
            {
                OpenVPNManager.RemoveProfile(profile.Name);
                LoadProfiles();
            }
        }
        private void LogButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is OpenVpnProfile profile)
            {
                OpenVPNManager.OpenLog(profile.Name);
            }
        }
        private void AutoButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is OpenVpnProfile profile)
            {
                string current = profile.AutoConnectNetwork ?? string.Empty;
                string input = Interaction.InputBox("Auto connect when connected to network:", "Auto Connect", current);
                OpenVPNManager.SetAutoConnect(profile.Name, string.IsNullOrWhiteSpace(input) ? null : input);
                LoadProfiles();
            }
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

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            WindowHelper.ApplyBlurEffect(this);
        }


    }
}
