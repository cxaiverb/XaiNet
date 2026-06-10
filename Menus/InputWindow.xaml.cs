using System.Windows;
using System.Windows.Input;
using XaiNet2.Helpers;

namespace XaiNet2.Menus
{
    public partial class InputWindow : Window
    {
        public InputWindow(WirelessWindow owner)
        {
            InitializeComponent();
            this.Owner = owner;

            bool myrkurModeEnabled = Properties.Settings.Default.MyrkurMode;
            this.SetMyrkurMode(myrkurModeEnabled);

            pwdBox.PasswordChanged += PwdBox_PasswordChanged;
            pwdPlain.TextChanged += PwdPlain_TextChanged;
        }

        public string SSID { get; set; }

        private bool _isPasswordVisible;
        private bool _suppressSync;

        // Returns the entered password if the user submitted (DialogResult == true),
        // null on cancel or when the input was empty.
        public string GetPassword()
        {
            if (DialogResult != true) return null;
            string raw = _isPasswordVisible ? pwdPlain.Text : pwdBox.Password;
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
            _isPasswordVisible = !_isPasswordVisible;
            _suppressSync = true;
            try
            {
                if (_isPasswordVisible)
                {
                    pwdPlain.Text = pwdBox.Password;
                    pwdPlain.Visibility = Visibility.Visible;
                    pwdBox.Visibility = Visibility.Collapsed;
                    ShowPasswordButton.Content = "Hide";
                    pwdPlain.Focus();
                    pwdPlain.CaretIndex = pwdPlain.Text.Length;
                }
                else
                {
                    pwdBox.Password = pwdPlain.Text;
                    pwdBox.Visibility = Visibility.Visible;
                    pwdPlain.Visibility = Visibility.Collapsed;
                    ShowPasswordButton.Content = "Show";
                    pwdBox.Focus();
                }
            }
            finally
            {
                _suppressSync = false;
            }
        }

        // Keep both controls in sync as the user types so the toggle is lossless.
        private void PwdBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_suppressSync) return;
            pwdPlain.Text = pwdBox.Password;
        }

        private void PwdPlain_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_suppressSync) return;
            pwdBox.Password = pwdPlain.Text;
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
