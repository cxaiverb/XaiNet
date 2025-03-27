using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Controls;
using System.Windows.Media;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace XaiNet2.Helpers
{
    public static class WindowExtensions
    {
        public static void SetMyrkurMode(this Window window, bool enable)
        {
            if (enable)
            {
                window.FontFamily = new System.Windows.Media.FontFamily("Comic Sans MS");
                Debug.WriteLine("Enabled MyrkurMode");
            }
            else
            {
                window.FontFamily = new System.Windows.Media.FontFamily("Segoe UI"); // Default font
                Debug.WriteLine("Disabled MyrkurMode");
            }
        }
    }
}
