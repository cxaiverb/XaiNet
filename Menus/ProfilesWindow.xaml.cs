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
            string backIcon = "back";
            var backIco = ImageLoader.LoadImageFromResources(backIcon);

            if (backIco != null)
            {
                BackButton.Content = new Image
                {
                    Source = backIco,
                    Width = 16,
                    Height = 16
                };
            }

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
        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
        private void Window_Deactivated(object sender, EventArgs e)
        {
            Hide();
        }

    }

}
