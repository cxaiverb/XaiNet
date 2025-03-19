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

namespace XaiNet2 
{ 

    public partial class VPNWindow : Window
    {
        public VPNWindow(MainWindow owner)
        {
            InitializeComponent();
            this.Owner = owner;
            PositionNearMainWindow(owner);
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
    }
}
