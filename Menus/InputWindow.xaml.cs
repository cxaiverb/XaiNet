using NetworkTrayApp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

namespace XaiNet2.Menus
{
    public partial class InputWindow : Window
    {

        public InputWindow(WirelessWindow owner)
        {
            InitializeComponent();
            this.Owner = owner;
            PositionNearMainWindow(owner);

            bool myrkurModeEnabled = Properties.Settings.Default.MyrkurMode;
            this.SetMyrkurMode(myrkurModeEnabled);



        }
        public string SSID { get; set; }

        private void PositionNearMainWindow(WirelessWindow owner)
        {
            // Get position from owner
            Left = Owner.Left;
            Top = Owner.Top;
            Width = Owner.Width;
            Height = Owner.Height;
        }

        public string GetPassword()
        {
            if (string.IsNullOrEmpty(pwdBox.Password))
            {
                return null;
            }
            Debug.WriteLine($"Password sent to profile template: {pwdBox.Password}");
            return pwdBox.Password;
        }
        private void SubmitButton_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine($"Submit button clicked");
            string userPass = pwdBox.Password.ToString();
            Debug.WriteLine($"Password saved: {userPass}");
            this.Hide();
        }
        private void Window_Deactivated(object sender, EventArgs e)
        {
            this.Hide();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            WindowHelper.ApplyBlurEffect(this);
            SSIDLabel.Text = $"Input Password for: {SSID}";
        }
    }
}
