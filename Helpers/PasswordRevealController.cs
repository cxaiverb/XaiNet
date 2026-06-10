using System.Windows;
using System.Windows.Controls;

namespace XaiNet2.Helpers
{
    // Keeps a masked PasswordBox and a plain TextBox in sync and toggles which one is shown, so the
    // "show/hide password" behaviour lives in one place instead of being copy-pasted into each dialog.
    // Construct it after InitializeComponent with the dialog's pwd box, plain box, and toggle button.
    public sealed class PasswordRevealController
    {
        private readonly PasswordBox _masked;
        private readonly TextBox _plain;
        private readonly Button _toggle;
        private bool _isVisible;
        private bool _suppressSync;

        public PasswordRevealController(PasswordBox masked, TextBox plain, Button toggle)
        {
            _masked = masked;
            _plain = plain;
            _toggle = toggle;

            // Mirror edits both ways so the show/hide toggle is lossless.
            _masked.PasswordChanged += (_, _) => { if (!_suppressSync) _plain.Text = _masked.Password; };
            _plain.TextChanged += (_, _) => { if (!_suppressSync) _masked.Password = _plain.Text; };
        }

        // The current password regardless of which field is showing.
        public string Password => _isVisible ? _plain.Text : _masked.Password;

        public void Toggle()
        {
            _isVisible = !_isVisible;
            _suppressSync = true;
            try
            {
                if (_isVisible)
                {
                    _plain.Text = _masked.Password;
                    _plain.Visibility = Visibility.Visible;
                    _masked.Visibility = Visibility.Collapsed;
                    _toggle.Content = "Hide";
                    _plain.Focus();
                    _plain.CaretIndex = _plain.Text.Length;
                }
                else
                {
                    _masked.Password = _plain.Text;
                    _masked.Visibility = Visibility.Visible;
                    _plain.Visibility = Visibility.Collapsed;
                    _toggle.Content = "Show";
                    _masked.Focus();
                }
            }
            finally
            {
                _suppressSync = false;
            }
        }
    }
}
