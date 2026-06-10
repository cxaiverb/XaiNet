using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using ManagedNativeWifi;

namespace XaiNet2.Helpers
{
    // Builds WLAN profile XML and applies it, then issues a connect. Shared by the Wi-Fi list
    // (visible networks), the "add network manually" flow, and the saved-profiles page.
    public static class WlanProfileHelper
    {
        // Maps ManagedNativeWifi's AuthenticationAlgorithm/CipherAlgorithm names (passed as strings so
        // we don't couple to specific enum members) to WLAN-profile schema values, and flags 802.1X.
        public static (string authentication, string encryption, string keyType, bool enterprise) MapSecurity(
            string authAlgorithm, string cipherAlgorithm)
        {
            string encryption = cipherAlgorithm switch
            {
                "None" => "none",
                "WEP" or "WEP40" or "WEP104" => "WEP",
                "TKIP" => "TKIP",
                _ => "AES", // CCMP and anything newer (GCMP / WPA3) -> AES is the safe schema value
            };

            // Non-PSK WPA/RSNA means 802.1X (Enterprise); the *_PSK variants are Personal.
            bool enterprise = authAlgorithm is "WPA" or "RSNA";

            string authentication = authAlgorithm switch
            {
                "Open" => "open",
                "SharedKey" => "shared",
                "WPA_PSK" => "WPAPSK",
                "WPA" => "WPA",
                "RSNA_PSK" => "WPA2PSK",
                "RSNA" => "WPA2",
                _ => "WPA2PSK",
            };

            string keyType =
                enterprise ? null :
                authentication == "shared" ? "networkKey" :
                (authentication == "open" && encryption == "WEP") ? "networkKey" :
                "passPhrase";

            return (authentication, encryption, keyType, enterprise);
        }

        // Creates/overwrites a WLAN profile for the SSID and fires a (non-blocking) connect.
        // Returns null on success, or a human-readable error message. Prefer CreateAndConnectAsync
        // when you want to wait for and report the actual association result.
        public static string CreateAndConnect(
            string ssid, string authentication, string encryption, string password,
            bool nonBroadcast, bool autoConnect = true, string keyType = "passPhrase", bool enterprise = false)
        {
            var error = TrySetProfile(ssid, authentication, encryption, password, nonBroadcast, autoConnect, keyType, enterprise, out var interfaceId);
            if (error != null) return error;
            try
            {
                NativeWifi.ConnectNetwork(interfaceId, ssid, BssType.Infrastructure);
                return null;
            }
            catch (Exception ex)
            {
                return ExtractError(ex);
            }
        }

        // Like CreateAndConnect, but awaits the real association result so a wrong key / out-of-range
        // network reports failure instead of a misleading "connecting". Returns null on success.
        public static async Task<string> CreateAndConnectAsync(
            string ssid, string authentication, string encryption, string password,
            bool nonBroadcast, bool autoConnect = true, string keyType = "passPhrase", bool enterprise = false,
            int timeoutSeconds = 12)
        {
            var error = TrySetProfile(ssid, authentication, encryption, password, nonBroadcast, autoConnect, keyType, enterprise, out var interfaceId);
            if (error != null) return error;
            try
            {
                bool connected = await NativeWifi.ConnectNetworkAsync(
                    interfaceId, ssid, BssType.Infrastructure, TimeSpan.FromSeconds(timeoutSeconds));
                return connected ? null : "Couldn't connect — wrong key, out of range, or the network refused us.";
            }
            catch (Exception ex)
            {
                return ExtractError(ex);
            }
        }

        // Builds the profile XML and applies it. Returns null + the interface id on success, else an error.
        private static string TrySetProfile(
            string ssid, string authentication, string encryption, string password,
            bool nonBroadcast, bool autoConnect, string keyType, bool enterprise, out Guid interfaceId)
        {
            interfaceId = Guid.Empty;
            var wifiAdapter = NativeWifi.EnumerateInterfaces().FirstOrDefault();
            if (wifiAdapter == null) return "No Wi-Fi adapter available";
            interfaceId = wifiAdapter.Id;

            string hexSsid = Convert.ToHexString(Encoding.UTF8.GetBytes(ssid));
            string profileXml = BuildWlanProfileXml(ssid, hexSsid, authentication, encryption, password,
                nonBroadcast, autoConnect, keyType, enterprise);
            try
            {
                NativeWifi.SetProfile(interfaceId, ProfileType.AllUser, profileXml, profileSecurity: null, overwrite: true);
                return null;
            }
            catch (Exception ex)
            {
                return ExtractError(ex);
            }
        }

        // ManagedNativeWifi surfaces native errors as a long message; pull the useful bit out.
        private static string ExtractError(Exception ex)
        {
            string pattern = @"ErrorMessage:\s([a-zA-Z\s:]+)[a-zA-Z\s.,;:\d]+ReasonMessage:\s([a-zA-Z\s:.]+)";
            var match = Regex.Match(ex.Message, pattern);
            return match.Success
                ? $"{match.Groups[1].Value.Trim()}\n{match.Groups[2].Value.Trim()}"
                : ex.Message;
        }

        // Build the WLANProfile XML via XDocument so SSIDs and passwords containing
        // '<', '&', '"' are escaped properly instead of breaking the document.
        //
        //   keyType:      "passPhrase" for WPA, "networkKey" for WEP. Ignored when enterprise=true.
        //   nonBroadcast: true for hidden SSIDs — Windows actively probes instead of waiting on beacons.
        //   autoConnect:  drives <connectionMode>auto|manual</connectionMode>.
        //   enterprise:   true for 802.1X profiles. Generates a PEAP-MSCHAPv2 OneX block — Windows
        //                 prompts the user for credentials at association time.
        private static string BuildWlanProfileXml(
            string ssid,
            string hexSsid,
            string authentication,
            string encryption,
            string password,
            bool nonBroadcast = false,
            bool autoConnect = true,
            string keyType = "passPhrase",
            bool enterprise = false)
        {
            XNamespace v1 = "http://www.microsoft.com/networking/WLAN/profile/v1";
            XNamespace v3 = "http://www.microsoft.com/networking/WLAN/profile/v3";

            var security = new XElement(v1 + "security",
                new XElement(v1 + "authEncryption",
                    new XElement(v1 + "authentication", authentication),
                    new XElement(v1 + "encryption", encryption),
                    new XElement(v1 + "useOneX", enterprise ? "true" : "false")));

            if (enterprise)
            {
                // PEAP-MSCHAPv2 — the most widely deployed Enterprise EAP combo.
                // Credentials are not embedded; Windows prompts at connect time.
                security.Add(BuildPeapMsChapV2OneX());
            }
            else if (!string.IsNullOrEmpty(password))
            {
                // Personal: pre-shared key. WPA networks use passPhrase; WEP uses networkKey.
                security.Add(new XElement(v1 + "sharedKey",
                    new XElement(v1 + "keyType", keyType ?? "passPhrase"),
                    new XElement(v1 + "protected", "false"),
                    new XElement(v1 + "keyMaterial", password)));
            }

            var msm = new XElement(v1 + "MSM", security);

            var ssidConfig = new XElement(v1 + "SSIDConfig",
                new XElement(v1 + "SSID",
                    new XElement(v1 + "hex", hexSsid),
                    new XElement(v1 + "name", ssid)));
            if (nonBroadcast)
            {
                ssidConfig.Add(new XElement(v1 + "nonBroadcast", "true"));
            }

            var doc = new XDocument(
                new XDeclaration("1.0", null, null),
                new XElement(v1 + "WLANProfile",
                    new XElement(v1 + "name", ssid),
                    ssidConfig,
                    new XElement(v1 + "connectionType", "ESS"),
                    new XElement(v1 + "connectionMode", autoConnect ? "auto" : "manual"),
                    msm,
                    new XElement(v3 + "MacRandomization",
                        new XElement(v3 + "enableRandomization", "false"))));

            return doc.Declaration + Environment.NewLine + doc.ToString();
        }

        // Builds the <OneX>…<EAPConfig>…</EAPConfig></OneX> block for PEAP/MSCHAPv2.
        // EAP method type 25 = PEAP, type 26 = MSCHAPv2 (RFC 3748 / Microsoft schemas).
        private static XElement BuildPeapMsChapV2OneX()
        {
            XNamespace oneX     = "http://www.microsoft.com/networking/OneX/v1";
            XNamespace eapHost  = "http://www.microsoft.com/provisioning/EapHostConfig";
            XNamespace eapCommon = "http://www.microsoft.com/provisioning/EapCommon";
            XNamespace baseEap  = "http://www.microsoft.com/provisioning/BaseEapConnectionPropertiesV1";
            XNamespace peap     = "http://www.microsoft.com/provisioning/MsPeapConnectionPropertiesV1";
            XNamespace mschap   = "http://www.microsoft.com/provisioning/MsChapV2ConnectionPropertiesV1";

            return new XElement(oneX + "OneX",
                new XElement(oneX + "authMode", "user"),
                new XElement(oneX + "EAPConfig",
                    new XElement(eapHost + "EapHostConfig",
                        new XElement(eapHost + "EapMethod",
                            new XElement(eapCommon + "Type", 25),
                            new XElement(eapCommon + "VendorId", 0),
                            new XElement(eapCommon + "VendorType", 0),
                            new XElement(eapCommon + "AuthorId", 0)),
                        new XElement(eapHost + "Config",
                            new XElement(baseEap + "Eap",
                                new XElement(baseEap + "Type", 25),
                                new XElement(peap + "EapType",
                                    new XElement(peap + "ServerValidation",
                                        new XElement(peap + "DisableUserPromptForServerValidation", "false"),
                                        new XElement(peap + "ServerNames", string.Empty)),
                                    new XElement(peap + "FastReconnect", "true"),
                                    new XElement(peap + "InnerEapOptional", "false"),
                                    new XElement(baseEap + "Eap",
                                        new XElement(baseEap + "Type", 26),
                                        new XElement(mschap + "EapType",
                                            new XElement(mschap + "UseWinLogonCredentials", "false"))),
                                    new XElement(peap + "EnableQuarantineChecks", "false"),
                                    new XElement(peap + "RequireCryptoBinding", "false"),
                                    new XElement(peap + "PeapExtensions"))))))
            );
        }
    }
}
