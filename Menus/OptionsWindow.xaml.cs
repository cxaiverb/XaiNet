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
using XaiNet2.Helpers;
using WinForms = System.Windows.Forms;

namespace XaiNet2.Menus
{
    public partial class OptionsWindow : Window
    {
        private const string AutoStartKey = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";
        private const string AppName = "XaiNet";
        private Dictionary<string, CheckBox> adapterCheckboxes = new Dictionary<string, CheckBox>();

        // Suppresses deactivate-to-hide while a modal folder picker is open.
        private bool _suppressHide;

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
            this.SetMyrkurMode(myrkurModeEnabled);
            // Load Nerd Stats state
            bool nerdStatsEnabled = Properties.Settings.Default.NerdStats;
            NerdStatsCheckBox.IsChecked = nerdStatsEnabled;
            // Load logging state
            EnableLoggingCheckBox.IsChecked = Properties.Settings.Default.EnableLogging;

            // Opening the log folder launches Explorer, which deactivates this modal window; resetting
            // the guard on re-activation keeps it from getting stranded (mirrors the Browse buttons).
            Activated += (_, _) => _suppressHide = false;

            if (OpenVPNManager.IsInstalled)
            {
                ConfigDirTextBox.Text = OpenVPNManager.ConfigDirectory;
                LogDirTextBox.Text = OpenVPNManager.LogDirectory;
            }
            else
            {
                OpenVPNPathsExpander.Visibility = Visibility.Collapsed;
            }

        }
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            WindowHelper.ApplyBlurEffect(this);
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
                // CreateSubKey returns the key if it exists, creates it otherwise — never null on success.
                // OpenSubKey can return null if the Run key is somehow missing or denied.
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(AutoStartKey, writable: true);
                if (key == null)
                {
                    Debug.WriteLine("Auto-Start: failed to open Run registry key.");
                    return;
                }
                if (enable)
                {
                    string executablePath = Process.GetCurrentProcess().MainModule?.FileName
                        ?? Environment.ProcessPath;
                    if (string.IsNullOrEmpty(executablePath))
                    {
                        Debug.WriteLine("Auto-Start: could not determine executable path.");
                        return;
                    }
                    key.SetValue(AppName, $"\"{executablePath}\"");
                    Debug.WriteLine("Auto-Start Enabled");
                }
                else
                {
                    key.DeleteValue(AppName, false);
                    Debug.WriteLine("Auto-Start Disabled");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating Auto-Start: {ex.Message}");
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

            // Apply Nerd Mode to Stats
            bool nerdStats = NerdStatsCheckBox.IsChecked == true;
            Properties.Settings.Default.NerdStats = nerdStats;

            // Logging toggle
            bool enableLogging = EnableLoggingCheckBox.IsChecked == true;
            Properties.Settings.Default.EnableLogging = enableLogging;

            // ApplyNerdStats reads the in-memory setting (already set above); the single Save() at
            // the end of this method persists everything to disk.
            ((MainWindow)Owner).ApplyNerdStats();
            if (enableLogging)
            {
                Logger.Info("Logging enabled via Options.");
            }

            foreach (Window window in Application.Current.Windows)
            {
                window.SetMyrkurMode(myrkurMode);
            }
            
            OpenVPNManager.SetDirectories(ConfigDirTextBox.Text, LogDirTextBox.Text);

            Properties.Settings.Default.Save();

            this.Hide();
            Owner.Show();
        }
        private void BrowseConfigDir_Click(object sender, RoutedEventArgs e)
        {
            ConfigDirTextBox.Text = BrowseForFolder(ConfigDirTextBox.Text) ?? ConfigDirTextBox.Text;
        }

        private void BrowseLogDir_Click(object sender, RoutedEventArgs e)
        {
            LogDirTextBox.Text = BrowseForFolder(LogDirTextBox.Text) ?? LogDirTextBox.Text;
        }

        // Returns the chosen folder, or null if cancelled. Suppresses the deactivate-hide while open.
        private string BrowseForFolder(string initialPath)
        {
            using var dialog = new WinForms.FolderBrowserDialog();
            if (!string.IsNullOrWhiteSpace(initialPath) && System.IO.Directory.Exists(initialPath))
            {
                dialog.SelectedPath = initialPath;
            }

            _suppressHide = true;
            WinForms.DialogResult result;
            try { result = dialog.ShowDialog(); }
            finally { _suppressHide = false; }

            return result == WinForms.DialogResult.OK ? dialog.SelectedPath : null;
        }
        private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
        {
            // Explorer steals focus asynchronously; keep this modal window from auto-hiding until
            // it is re-activated (the constructor's Activated handler resets the guard).
            _suppressHide = true;
            Logger.OpenLogFolder();
        }

        private void CancelOptions_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("Closed options without saving.");
            Close();
            Owner.Show();
        }
        private void Window_Deactivated(object sender, EventArgs e)
        {
            if (_suppressHide) return;
            this.Hide();
        }

    }
}
