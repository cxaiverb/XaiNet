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
            return pwdBox.Password;
        }
        private void SubmitButton_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine($"Submit button clicked");
            string userPass = pwdBox.Password.ToString();
            this.Close();
        }
        private void Window_Deactivated(object sender, EventArgs e)
        {
            this.Close();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            WindowHelper.ApplyBlurEffect(this);
        }
    }
}
