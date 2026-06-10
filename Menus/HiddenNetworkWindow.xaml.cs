using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using XaiNet2.Helpers;

namespace XaiNet2.Menus
{
    public enum HiddenNetworkSecurity
    {
        Open,                // open + none
        Wep,                 // open + WEP, networkKey
        Wpa2PersonalAes,     // WPA2PSK + AES, passPhrase
        Wpa2EnterpriseAes,   // WPA2 + AES + 802.1X (PEAP-MSCHAPv2)
        WepEnterprise,       // open + WEP + 802.1X (PEAP-MSCHAPv2)
    }

    public partial class HiddenNetworkWindow : Window
    {
        private PasswordRevealController _reveal;

        public HiddenNetworkWindow(Window owner)
        {
            InitializeComponent();
            this.Owner = owner;

            bool myrkurModeEnabled = Properties.Settings.Default.MyrkurMode;
            this.SetMyrkurMode(myrkurModeEnabled);

            _reveal = new PasswordRevealController(pwdBox, pwdPlain, ShowPasswordButton);
        }

        // Result fields, valid when DialogResult == true.
        public string NetworkSsid { get; private set; } = string.Empty;
        public HiddenNetworkSecurity Security { get; private set; } = HiddenNetworkSecurity.Wpa2PersonalAes;
        public string NetworkPassword { get; private set; } = string.Empty;
        public bool AutoConnect { get; private set; } = true;
        public bool NonBroadcast { get; private set; } = true;

        // Maps the chosen security type to WLAN-profile schema values. Valid after DialogResult == true.
        public (string authentication, string encryption, string keyType, bool enterprise) GetWlanParameters()
        {
            switch (Security)
            {
                case HiddenNetworkSecurity.Open:              return ("open",    "none", null,         false);
                case HiddenNetworkSecurity.Wep:               return ("open",    "WEP",  "networkKey", false);
                case HiddenNetworkSecurity.Wpa2PersonalAes:   return ("WPA2PSK", "AES",  "passPhrase", false);
                case HiddenNetworkSecurity.Wpa2EnterpriseAes: return ("WPA2",    "AES",  null,         true);
                case HiddenNetworkSecurity.WepEnterprise:     return ("open",    "WEP",  null,         true);
                default:                                      return ("WPA2PSK", "AES",  "passPhrase", false);
            }
        }

        private static bool RequiresPassword(HiddenNetworkSecurity s) =>
            s == HiddenNetworkSecurity.Wep || s == HiddenNetworkSecurity.Wpa2PersonalAes;

        private static HiddenNetworkSecurity ParseSecurityTag(string tag) => tag switch
        {
            "Open"              => HiddenNetworkSecurity.Open,
            "Wep"               => HiddenNetworkSecurity.Wep,
            "Wpa2PersonalAes"   => HiddenNetworkSecurity.Wpa2PersonalAes,
            "Wpa2EnterpriseAes" => HiddenNetworkSecurity.Wpa2EnterpriseAes,
            "WepEnterprise"     => HiddenNetworkSecurity.WepEnterprise,
            _                   => HiddenNetworkSecurity.Wpa2PersonalAes,
        };

        private HiddenNetworkSecurity SelectedSecurity()
        {
            if (SecurityCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
                return ParseSecurityTag(tag);
            return HiddenNetworkSecurity.Wpa2PersonalAes;
        }

        private void SecurityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Show password row only for security types that take a key.
            if (PasswordSection == null) return; // fired during construction
            var sec = SelectedSecurity();
            PasswordSection.Visibility = RequiresPassword(sec) ? Visibility.Visible : Visibility.Collapsed;

            // WEP labels its key "Network security key", WPA "Security key" — keep it generic.
            if (PasswordLabel != null)
            {
                PasswordLabel.Text = sec == HiddenNetworkSecurity.Wep ? "Network security key" : "Security key";
            }
        }

        private void SubmitButton_Click(object sender, RoutedEventArgs e)
        {
            string ssid = SsidBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(ssid))
            {
                NotificationHelper.ShowToast("Add network", "Please enter a network name.");
                SsidBox.Focus();
                return;
            }

            var sec = SelectedSecurity();
            string password = string.Empty;

            if (RequiresPassword(sec))
            {
                password = _reveal.Password;
                if (string.IsNullOrEmpty(password))
                {
                    NotificationHelper.ShowToast("Add network", "A security key is required for this security type.");
                    pwdBox.Focus();
                    return;
                }
                if (sec == HiddenNetworkSecurity.Wep && !IsValidWepKey(password))
                {
                    NotificationHelper.ShowToast("Add network",
                        "WEP keys must be 5 or 13 ASCII characters, or 10 or 26 hex characters.");
                    pwdBox.Focus();
                    return;
                }
            }

            NetworkSsid = ssid;
            Security = sec;
            NetworkPassword = password;
            AutoConnect = AutoConnectCheckBox.IsChecked == true;
            NonBroadcast = NonBroadcastCheckBox.IsChecked == true;
            DialogResult = true;
            Close();
        }

        // Validates a WEP key: 5/13 ASCII chars (text key) or 10/26 hex chars (raw key).
        private static bool IsValidWepKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (key.Length == 5 || key.Length == 13) return true;
            if ((key.Length == 10 || key.Length == 26) && IsHex(key)) return true;
            return false;

            static bool IsHex(string s)
            {
                foreach (var c in s)
                {
                    bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                    if (!ok) return false;
                }
                return true;
            }
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
            // Apply initial visibility for the password row based on the default security.
            SecurityCombo_SelectionChanged(SecurityCombo, null);
            UpdateCapsLockHint();
            SsidBox.Focus();
        }
    }
}
