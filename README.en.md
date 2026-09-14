<div align="center">
  <img src="assets/app.png" alt="PadHop icon" width="96" height="96" />
  <h1>PadHop · Steam Controller utility</h1>
  <p>Make Steam Controller 2 useful in more places.</p>

[![Windows build](https://github.com/csvwolf/padhop/actions/workflows/windows.yml/badge.svg?branch=master)](https://github.com/csvwolf/padhop/actions/workflows/windows.yml)
[![Release](https://img.shields.io/github/v/release/csvwolf/padhop?include_prereleases&label=release&color=5ecbf5)](https://github.com/csvwolf/padhop/releases)
[![Downloads](https://img.shields.io/github/downloads/csvwolf/padhop/total?color=5ecbf5)](https://github.com/csvwolf/padhop/releases)
[![MIT](https://img.shields.io/badge/license-MIT-5ecbf5)](LICENSE)
[![Windows](https://img.shields.io/badge/Windows-10%20%2F%2011-0078d4)](#installation)

**English · [简体中文](README.md)**

**[Download installer](https://github.com/csvwolf/padhop/releases) · [Report an issue](https://github.com/csvwolf/padhop/issues)**

</div>

A Windows input utility for Steam Controller 2: global and per-app profiles, independent trackpads and haptics, keyboard/mouse bindings, Windows shortcuts, macros, the system on-screen keyboard, and optional virtual Xbox controller output. This is an independent project, not affiliated with Valve.

**0.2.4 experimental release: Bluetooth SC2 is the current supported connection.** USB/receiver connections and Steam Deck/Moonlight forwarded controllers are not currently supported. Protocol and firmware differences need hardware testing; automated tests cannot establish compatibility with every game.

## Installation

Requires Windows 10 1903+ / Windows 11 x64 and .NET Framework 4.8. Download `install.exe` from Releases. The installer supports English and Simplified Chinese. Install as administrator; the application normally runs unelevated. Upgrade by running the new installer. Installation and upgrades preserve profiles. Uninstall through Windows Installed apps.

- Standard mode controls ordinary windows. The optional **Administrator-window control / local signing** component creates a certificate on your PC and enables UIAccess after explicit consent. It is off by default on a fresh installation; Steam does not need to run as administrator.
- Local certificate trust affects all users of this PC. Misuse of the app or input path could affect administrator programs. Self-signing does not establish a public publisher identity or guarantee removal of security warnings. Only three PadHop executables are signed. The non-exportable private key is deleted after normal completion; no shared private key is distributed. UAC, Secure Boot and driver-signing policy are unchanged.
- Local certificates last one year. About shows the expiry date and offers renewal, with reminders during the final 30 days. Renewal requires administrator confirmation, closes PadHop, re-signs it and removes old trust. Reopen the app afterward. Run the installer again to enable or revoke local signing. Uninstall removes its recorded certificate. For interrupted operations, keep `.local-signing` and inspect `local-signing-last.log`; `Local-Signing.ps1 -Action Disable` restores original files, while `-Action RemoveTrust` only revokes trust before reinstallation.
- Optional Xbox output installs the bundled official ViGEmBus 1.22.0 driver only if needed. The driver is retired and future Windows compatibility is not guaranteed. Uninstalling PadHop keeps this shared driver. Driver failures and required restarts are reported. Choose output per application to avoid duplicate virtual controllers from Steam and PadHop.

## Profiles and language

Select your Bluetooth SC2 on Input status. Under Controller settings, choose or create a profile. **Try** previews the edited parameters; **Apply** controls the active profile; **Save** overwrites the named profile; **Save as** creates a copy. Trackpad actions and haptics belong to the same controller profile.

Exclusion mode handles applications except those excluded; inclusion mode handles only explicitly included applications. App profiles are independent of these rules. Steam ordinary windows follow app rules; Big Picture and overlays remain with Steam.

In **About**, select System, 简体中文 or English, then reopen PadHop. Existing profile names, application paths and mappings are not translated or reset. About also includes project/author links, log access, signature renewal and update controls.

Automatic update checks are **off by default**. When enabled, PadHop checks at startup and every 24 hours while running, and only notifies you. Download and installation each require an explicit click. Downloads are checked against GitHub's SHA-256 digest, and installation still requires administrator confirmation. No logs or profiles are uploaded.

Closing the window minimizes to tray. Launching again activates the existing instance. Data lives in `%LOCALAPPDATA%/PadHop` and survives uninstall. Use Share current profile when sharing with others; a full backup includes local application paths. Logs may contain paths and device identifiers: review before sharing.

## Build from source

Run in Windows PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/bootstrap-installer.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-installer.ps1
```

The build uses the system .NET Framework C# compiler, pinned NuGet dependencies, and SHA-256 checks for native DLLs. Initial dependency restoration requires internet access. Application binaries are in `bin/`; the installer is `dist/install.exe`. English translations are maintained in `languages/en.json`; the build embeds them in the application and input engine. Installer language strings live in `installer/PadHop.iss`.

Haptic recording needs optional Python and Microsoft Bluetooth analysis tools, configured with `configure-capture.ps1`. Raw ETL/PCAP traces can include other Bluetooth traffic and sensitive device data; do not publish raw capture directories. Prefer exported profile parameters. Detailed technical guides (currently Chinese): [recording](docs/capture.md), [privacy](docs/privacy.md), [release validation](docs/release.md), [local signing recovery](docs/local-signing.md).

## License and acknowledgments

PadHop is [MIT licensed](LICENSE). Trackpad behavior references SteamlessController; virtual output uses ViGEmClient. The optional official ViGEmBus installer is BSD licensed. Bluetooth WPR profiles derive from Microsoft busiotools. See [third-party notices](THIRD-PARTY-NOTICES.txt) for attribution and modifications. Microsoft's BTETLParse is not redistributed. Generated images under `branding/` are concept art; the application currently uses the simple icon.
