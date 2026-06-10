using System.Windows;
using System.Windows.Input;
using XaiNet2.Helpers;

namespace XaiNet2.Menus
{
    public partial class InputWindow : Window
    {
        private PasswordRevealController _reveal;

        public InputWindow(WirelessWindow owner)
        {
            InitializeComponent();
            this.Owner = owner;

            bool myrkurModeEnabled = Properties.Settings.Default.MyrkurMode;
            this.SetMyrkurMode(myrkurModeEnabled);

            _reveal = new PasswordRevealController(pwdBox, pwdPlain, ShowPasswordButton);
        }

        public string SSID { get; set; }

        // Returns the entered password if the user submitted (DialogResult == true),
        // null on cancel or when the input was empty.
        public string GetPassword()
        {
            if (DialogResult != true) return null;
            string raw = _reveal.Password;
            return string.IsNullOrEmpty(raw) ? null : raw;
        }

        private void SubmitButton_Click(object sender, RoutedEventArgs e)
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
            else
            {
                UpdateCapsLockHint();
            }
        }

        private void ShowPasswordButton_Click(object sender, RoutedEventArgs e)
        {
            _reveal.Toggle();
        }

        private void UpdateCapsLockHint()
        {
            CapsLockHint.Visibility = Keyboard.IsKeyToggled(Key.CapsLock)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            WindowHelper.ApplyBlurEffect(this);
            SSIDLabel.Text = SSID ?? string.Empty;
            UpdateCapsLockHint();
            pwdBox.Focus();
        }
    }
}
