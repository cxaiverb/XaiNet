using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using XaiNet2.Helpers;

namespace XaiNet2.Menus
{
    // Themed dropdown picker for choosing a network name (Wi-Fi profile / SSID), with a "None"
    // entry to clear the selection. Used by VPN auto-connect instead of free-text entry.
    public partial class NetworkChoiceWindow : Window
    {
        private const string NoneLabel = "(None — disable auto-connect)";

        public NetworkChoiceWindow(Window owner, string title, string message,
            IEnumerable<string> options, string current)
        {
            InitializeComponent();
            Owner = owner;
            Title = title;

            bool myrkurModeEnabled = Properties.Settings.Default.MyrkurMode;
            this.SetMyrkurMode(myrkurModeEnabled);

            TitleLabel.Text = title;
            MessageLabel.Text = message;

            var items = new List<string> { NoneLabel };
            items.AddRange((options ?? Enumerable.Empty<string>())
                .Where(o => !string.IsNullOrWhiteSpace(o))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(o => o, StringComparer.OrdinalIgnoreCase));
            ChoiceCombo.ItemsSource = items;

            var match = string.IsNullOrEmpty(current)
                ? null
                : items.FirstOrDefault(i => string.Equals(i, current, StringComparison.OrdinalIgnoreCase));
            ChoiceCombo.SelectedItem = match ?? NoneLabel;
        }

        // Selected network name, or "" when "None" is chosen. Valid when DialogResult == true.
        public string Value =>
            ChoiceCombo.SelectedItem is string s && s != NoneLabel ? s : string.Empty;

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
                e.Handled = true;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            WindowHelper.ApplyBlurEffect(this);
        }
    }
}
