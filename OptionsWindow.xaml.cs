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
using Microsoft.Win32;
using System.Windows.Media.Animation;

namespace XaiNet2
{
    public partial class OptionsWindow : Window
    {
        private const string AutoStartKey = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";
        private const string AppName = "XaiNet";
        private Dictionary<string, CheckBox> adapterCheckboxes = new Dictionary<string, CheckBox>();

        public OptionsWindow(MainWindow owner)
        {
            InitializeComponent();
            this.Owner = owner;
            PositionNearMainWindow(owner);

            // Load auto start status
            AutoStartCheckBox.IsChecked = IsAppInStartup();
            // Load adapter toggles
            PopulateAdapterToggles(owner);
            // Load Myrkur Mode state
            bool myrkurModeEnabled = Properties.Settings.Default.MyrkurMode;
            MyrkurModeCheckBox.IsChecked = myrkurModeEnabled;

            // Apply Myrkur Mode to Options Window
            SetMyrkurMode(myrkurModeEnabled);
        }

        private void PositionNearMainWindow(MainWindow owner)
        {
            // Get position from owner
            this.Left = Owner.Left;
            this.Top = Owner.Top;
            this.Width = Owner.Width;
            this.Height = Owner.Height;
        }
        private void PopulateAdapterToggles(MainWindow owner)
        {
            AdapterTogglePanel.Children.Clear();

            foreach (var adapter in owner.GetNetworkAdapters())
            {
                Debug.WriteLine($"Creating checkbox for: {adapter.Name}");

                var checkBox = new CheckBox
                {
                    Content = adapter.Name,
                    Foreground = Brushes.White,
                    IsChecked = owner.IsAdapterVisible(adapter.Name) // Check if it's enabled
                };

                adapterCheckboxes[adapter.Name] = checkBox;
                AdapterTogglePanel.Children.Add(checkBox);
            }
        }



        private bool IsAppInStartup()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(AutoStartKey, false))
            {
                if (key != null)
                {
                    return key.GetValue(AppName) != null;
                }
            }
            return false;
        }

        private void SetAutoStart(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(AutoStartKey, true))
                {
                    if (enable)
                    {
                        string executablePath = Process.GetCurrentProcess().MainModule.FileName;
                        key.SetValue(AppName, $"\"{executablePath}\"");
                        Debug.WriteLine("Auto-Start Enabled");
                    }
                    else
                    {
                        key.DeleteValue(AppName, false);
                        Debug.WriteLine("Auto-Start Disabled");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating Auto-Start: {ex.Message}");
            }
        }
        public void SetMyrkurMode(bool enable)
        {
            if (enable)
            {
                this.FontFamily = new System.Windows.Media.FontFamily("Comic Sans MS");
                Debug.WriteLine("Enabled MyrkurMode");
            }
            else
            {
                this.FontFamily = new System.Windows.Media.FontFamily("Segoe UI"); // Default font
                Debug.WriteLine("Disabled MyrkurMode");
            }
        }

        private void SaveOptions_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("Options saved!");
            SetAutoStart(AutoStartCheckBox.IsChecked == true);
            // Collect enabled adapters
            List<string> enabledAdapters = adapterCheckboxes
                .Where(kv => kv.Value.IsChecked == true)
                .Select(kv => kv.Key)
                .ToList();

            // Save as a comma-separated string
            Properties.Settings.Default.VisibleAdapters = string.Join(",", enabledAdapters);

            // Apply settings in MainWindow
            ((MainWindow)Owner).UpdateAdapterVisibility(enabledAdapters);

            // Apply Myrkur Mode to all windows
            bool myrkurMode = MyrkurModeCheckBox.IsChecked == true;
            Properties.Settings.Default.MyrkurMode = myrkurMode;
            Properties.Settings.Default.Save();

            // Apply Myrkur Mode to MainWindow too
            ((MainWindow)Owner).SetMyrkurMode(myrkurMode);

            this.Close();
            Owner.Show();
        }
        private void Window_Deactivated(object sender, EventArgs e)
        {
            this.Hide();
        }

    }
}
