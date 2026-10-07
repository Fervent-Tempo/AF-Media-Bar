<p align="center">
  <img src="docs/assets/readme/hero-en.svg" width="100%" alt="AF Media Bar layout illustration: the media bar sits at the lower left of the Windows taskbar, its full media popover is directly above, and the app icon and project introduction are on the right">
</p>

<p align="center">
  <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/releases"><img src="https://img.shields.io/github/v/release/Fervent-Tempo/AF-Media-Bar?style=flat-square" alt="Latest release"></a>
  <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/releases"><img src="https://img.shields.io/github/downloads/Fervent-Tempo/AF-Media-Bar/total?style=flat-square" alt="Downloads"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/Fervent-Tempo/AF-Media-Bar?style=flat-square" alt="MIT License"></a>
  <br>
  <a href="README.md">简体中文</a> · English
  <br>
  <a href="#download-and-run">Download</a> · <a href="#features">Features</a> · <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/issues/new?template=bug_report.yml">Report a bug</a> · <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/issues/new?template=feature_request.yml">Request a feature</a>
</p>

AF Media Bar is a media controller for the Windows 10/11 taskbar. It reads the system media session and keeps artwork, lyrics, playback controls, and audio device switching at the edge of your desktop.

## Live demo

<p align="center">
  <img src="docs/assets/readme/展示.gif" width="100%" alt="AF Media Bar running on a Windows taskbar with artwork, playback controls, and media information">
</p>

[Watch the introduction video on Bilibili](https://www.bilibili.com/video/BV17yhh6aEgK)

## Download and run

Open [GitHub Releases](https://github.com/Fervent-Tempo/AF-Media-Bar/releases) and choose a package:

1. **Installer (recommended):** Download `AFMediaBar-Setup-vX.Y.Z-win-x64.exe` and follow the installation wizard.
2. **Portable:** Download `AFMediaBar-vX.Y.Z-win-x64.zip`, extract it to a writable folder, and run `AFMediaBar.exe`.

**Requirements:** Windows 10 1809 or later, x64, with Microsoft Edge WebView2 Runtime. Install the Evergreen Runtime if it is missing. Release packages include the .NET runtime. **.NET 10 officially supports only Windows 10 LTSC and Enterprise editions**; consumer editions are outside its support scope. Windows 11 is unaffected.

Download a release package rather than a Source code archive. The app is not commercially code-signed, so Windows SmartScreen may show an unknown-publisher warning on first run.

## Features

| Feature | Description |
| --- | --- |
| Playback and sources | View artwork and track information, switch media sources, and use previous, play/pause, next, repeat, and seeking controls. |
| Live lyrics | Fetch lyrics automatically and follow playback with word highlighting, translation, romanization, and two-line display. |
| Audio controls | Quickly switch the default output device, adjust the current media app's volume, and view spatial audio status. |
| Spectrum and metrics | Choose from four audio spectrum styles and show CPU, GPU, and memory metrics on the taskbar. |
| Layout and appearance | Left, center, and right alignment with left/right content arrangement and independent hover-button alignment; avoid taskbar icons and system areas, choose a display, auto-hide when nothing plays, and customize fonts, accent colour, and window material. |
| Shortcuts | Hover to reveal controls or open the full media popover for more information; customize click actions, quickly launch players, and use the tray menu. |

## Basic use

- Start your player and play a track. The media bar shows the current song; some players require “system media controls” or “media keys” to be enabled first.
- Hover over the media bar to reveal controls. Click or drag the progress bar to seek.
- Use the tray icon's context menu to open Settings, access quick launch, or exit.

**Compatibility:** Most players rely on Windows system media sessions. NetEase Cloud Music can be detected independently for track information, but artwork and playback controls still require its system media control support. Only taskbar mode is available; other display modes are not yet supported.

## Updating and uninstalling

- **Updates:** The app notifies you when a new version is available. Check for updates on the Application page. Installed builds can download and install updates in the app; portable builds require manual file replacement.
- **Settings:** Stored in `%LOCALAPPDATA%\AFMediaBar\settings.json`. Open the settings folder from the Application page.
- **Uninstall:** Remove installed builds through Windows Installed apps or the Start menu. For portable builds, delete the program folder. To clear settings and cache, also delete `%LOCALAPPDATA%\AFMediaBar` manually.

## Privacy and security

- No telemetry, ads, or account system. Media controls and volume operations run locally.
- Update checks and online lyric searches use the internet. Lyric searches use track and artist information; lyric sources can be disabled in Settings.
- The app runs with the current user's rights. Report security issues privately as described in [SECURITY.md](SECURITY.md).

## Building from Source

Windows 10 1809 or later, the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), and PowerShell are required; the repository pins the supported SDK feature band through `global.json`.

```powershell
git clone https://github.com/Fervent-Tempo/AF-Media-Bar.git
cd AF-Media-Bar
dotnet restore .\src\AFMediaBar.slnx -r win-x64
dotnet build .\src\AFMediaBar.slnx -c Release --no-restore -p:ContinuousIntegrationBuild=true -p:BuildInParallel=false
dotnet test .\tests\AFMediaBar.Layout.Tests\AFMediaBar.Layout.Tests.csproj -c Release --no-build --no-restore
dotnet run --project .\src\AFMediaBar\AFMediaBar.csproj
```

To produce the self-contained single file users download:

```powershell
dotnet publish .\src\AFMediaBar\AFMediaBar.csproj -c Release -r win-x64 --self-contained true -o .\artifacts\AFMediaBar-win-x64
```

## Project Structure

```text
AF-Media-Bar/
├── .github/workflows/        # Build and release workflows
├── src/AFMediaBar/           # The WPF application
│   ├── Classes/              # Layered application code
│   │   ├── Abstractions/     # Cross-module contracts
│   │   ├── Interop/          # Windows API interop
│   │   ├── Models/           # Data models, layout schema included
│   │   ├── Services/         # Ownership-scoped services (Media, Lyrics, Audio, Updates…)
│   │   ├── Settings/         # Settings model and compatibility facade
│   │   └── Utils/            # Stateless helpers and bounded caches
│   ├── Components/           # Reusable WPF controls
│   ├── Resources/            # Theme, styles, and three-language strings
│   ├── ViewModels/           # MVVM view models
│   └── Views/                # Pages and host windows
├── tests/AFMediaBar.Layout.Tests/   # Pure-logic, policy, and settings tests
├── tools/                    # Architecture static checks and helper scripts
├── installer/                # Inno Setup script
└── docs/                     # Documentation, assets, and the version manifest
```

## Contributing

Read the [contribution guide](CONTRIBUTING.md) before contributing, and see the [architecture document](docs/architecture.md) for architectural boundaries.

Bug reports should include your Windows version, app version, player, and reproduction steps. See [CHANGELOG.en-US.md](CHANGELOG.en-US.md) for version history.

## Acknowledgements

Thank you to all the developers who have contributed to AF Media Bar.

Thanks to the following open-source projects:

- [FluentFlyout](https://github.com/unchihugo/FluentFlyout): A simple, modern Windows volume flyout.
- [Lyricify-Lyrics-Helper](https://github.com/WXRIW/Lyricify-Lyrics-Helper): A Lyricify lyrics library for parsing, generating, searching, decrypting, and refining lyrics.
- [TaskbarLyrics](https://github.com/ANYNC/TaskbarLyrics): A Windows taskbar lyrics tool.


## License

AF Media Bar is released under the [MIT License](LICENSE).

<div align="center">

If AF Media Bar helps you, a Star is appreciated ❤️

</div>

## Sponsor

Buy me a coffee. **Sponsorships above 10 CNY can join the sponsor list — please leave your ID in the payment note.**

<div align="center">

| WeChat | Alipay |
| :---: | :---: |
| <img src="src/AFMediaBar/Assets/Sponsor/wechat-pay.png" alt="WeChat payment code" width="220"> | <img src="src/AFMediaBar/Assets/Sponsor/alipay-pay.png" alt="Alipay payment code" width="220"> |

</div>
