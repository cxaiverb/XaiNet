# XaiNet2 — developer notes

A .NET 8 WPF system-tray network manager for Windows (tray icon + popup adapter list, Wi-Fi
management, OpenVPN GUI integration, options). Inspired by KDE's Network Manager.

## Build / run

```
dotnet build            # Debug
dotnet run              # launches the tray app
```

Publishing (single-file, framework-dependent — what's shipped to testers):

```
dotnet publish -r win-x64 -c Release -p:PublishSingleFile=true --self-contained false
```

- Target: `net8.0-windows10.0.19041.0`, `UseWPF` + `UseWindowsForms` (the tray icon is a WinForms
  `NotifyIcon`; the rest is WPF).
- **Do NOT remove `<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>`
  from the csproj.** With single-file publish it embeds the SkiaSharp/HarfBuzz/glfw native DLLs into
  the exe. Without it those land as loose files beside the exe, and shipping just `XaiNet2.exe`
  causes a `libSkiaSharp` `DllNotFoundException` the moment a LiveCharts chart renders.
- There is no test project yet.

## Layout

- `App.xaml(.cs)` — application entry; `StartupUri` is `Menus/MainWindow.xaml`. Holds a per-user
  single-instance `Mutex` so a second launch exits quietly.
- `Menus/` — all windows, namespace `XaiNet2.Menus`:
  - `MainWindow` — owns the tray icon and the popup. A 1 s `DispatcherTimer` drives speed graphs and
    (every ~5 s) refreshes adapter metadata; a 5 s `System.Timers.Timer` updates the tray icon.
  - `WirelessWindow` / `InputWindow` / `HiddenNetworkWindow` / `ProfilesWindow` — Wi-Fi UI.
  - `VPNWindow` — OpenVPN UI.
  - `TailscaleWindow` — Tailscale UI (status, connect/disconnect/logout, exit-node picker,
    device list). Header button in MainWindow is shown only when Tailscale is installed.
  - `OptionsWindow` — settings.
  - `TextPromptWindow` — small themed single-line input dialog (replaces VB `InputBox`).
- `Helpers/` — namespace `XaiNet2.Helpers`:
  - `OpenVPNManager` — static manager for openvpn-gui (locate exe, profiles, connect/disconnect,
    auto-connect, logs). Thread-safe via `stateLock`.
  - `TailscaleManager` — static async wrapper around the `tailscale.exe` CLI (locate exe,
    `status --json`, up/down/logout, exit-node). Returns null on success / an error string on
    failure. State lives in tailscaled; we just query/drive it.
  - `WindowHelper` — acrylic blur via `SetWindowCompositionAttribute`.
  - `WindowExtensions.SetMyrkurMode` — the single Myrkur Mode (Comic Sans) toggle for all windows.
  - `Logger` — opt-in file logger (gated by `Settings.EnableLogging`). Writes to
    `%LOCALAPPDATA%\XaiNet2\logs\xainet2.log`; never throws. `App.OnStartup` registers global
    handlers (Dispatcher / AppDomain / TaskScheduler) that funnel unhandled exceptions here, so a
    tester can enable logging, reproduce a crash, and send the log (Options has an "Open log folder").
  - `WlanProfileHelper` — builds WLAN profile XML (incl. PEAP-MSCHAPv2 for Enterprise) and
    SetProfile+ConnectNetwork. Shared by the Wi-Fi list, "add network manually", and the saved-
    profiles page. Returns null on success / an error string. `HiddenNetworkWindow.GetWlanParameters()`
    maps its security enum to the auth/encryption/keyType/enterprise args.
  - `ImageLoader` — `CreateIcon(name, size)` builds a crisp button icon (decodes ~2x, HighQuality
    scaling); use it instead of `new Image{...}`. Tray icons load at `SystemInformation.SmallIconSize`.
  - `NotificationHelper` (toasts), `BitsToHumanConverter`.
  - Shared dialog in `Menus/`: `NetworkChoiceWindow` (dropdown picker of Wi-Fi profiles/SSIDs —
    used by VPN auto-connect).
  - `Themes.xaml` — shared styles (buttons, combobox, checkbox, textbox, scrollbar, expander).
- `Properties/Settings` — user settings: `VisibleAdapters`, `MyrkurMode`, `NerdStats`,
  `OpenVpnConfigDir`, `OpenVpnLogDir`. (Auto-start is handled via the registry Run key in
  `OptionsWindow`, not a setting.)
- `resources/*.ico` — exposed through `Properties/Resources.resx`; loaded by name with `ImageLoader`.

## Conventions / gotchas

- NIC IDs are GUID strings; always parse defensively (`Guid.TryParse`) — see
  `MainWindow.ParseAdapterId`.
- Speeds are stored in **bits/s** (`BytesSent * 8`) and formatted by `BitsToHumanConverter`.
- Child windows that open a **native** modal (WinForms `FolderBrowserDialog`, `OpenFileDialog`)
  set a `_suppressHide` flag around `ShowDialog` so `Window_Deactivated` doesn't hide the parent.
- **OpenVPN config identity:** profiles are stored flat as `{configDir}\{name}.ovpn`. The openvpn-gui
  "config name" is the path relative to the config dir without extension — `OpenVPNManager.GetConfigName`
  is the single source of truth and is used for **both** connect and disconnect. `activeConnections`
  is best-effort in-memory state (openvpn-gui has no simple status query).
- Wi-Fi work uses the `ManagedNativeWifi` package; `SignalStrength`/`SignalQuality` are 0–100 %.
- **WLAN queries need Windows Location services.** On Windows 10/11, `WlanQueryInterface` for the
  current connection / SSID requires the Location service to be ON, otherwise it throws
  `UnauthorizedAccessException` (ERROR_ACCESS_DENIED, 5). So `NativeWifi.EnumerateInterfaceConnections`,
  `EnumerateAvailableNetworks`, and `EnumerateBssNetworks` can throw — always wrap them. The Wi-Fi
  window degrades to a "turn on Location services" message instead of crashing.
