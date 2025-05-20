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
    public partial class ProfilesWindow : Window
    {
        public ProfilesWindow(Window owner)
        {
            InitializeComponent();
            Owner = owner;
            PositionNearOwner();
            Loaded += OnLoaded;
        }
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            string homeIcon = "back";
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
        }
        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            return;
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
