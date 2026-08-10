# AF Media Bar

<div align="center">

  <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/releases">
    <img src="https://img.shields.io/github/v/release/Fervent-Tempo/AF-Media-Bar?style=flat-square" alt="Latest release">
  </a>
  <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/releases">
    <img src="https://img.shields.io/github/downloads/Fervent-Tempo/AF-Media-Bar/total?style=flat-square" alt="Downloads">
  </a>
  <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/stargazers">
    <img src="https://img.shields.io/github/stars/Fervent-Tempo/AF-Media-Bar?style=flat-square" alt="Stars">
  </a>
  <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/issues">
    <img src="https://img.shields.io/github/issues/Fervent-Tempo/AF-Media-Bar?style=flat-square" alt="Issues">
  </a>
  <a href="LICENSE">
    <img src="https://img.shields.io/github/license/Fervent-Tempo/AF-Media-Bar?style=flat-square" alt="MIT License">
  </a>

  <br><br>

  <img src="assets/af-media-bar.png" alt="AF Media Bar" width="160" height="160">

  <h1>AF Media Bar</h1>

  <p>Media controls, audio device switching, and lightweight system metrics on the Windows 11 taskbar.</p>

  <p>
    <a href="README.md">简体中文</a>
    ·
    English
    <br>
    <a href="#installation">Quick start</a>
    ·
    <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/issues/new?template=bug_report.yml">Report a bug</a>
    ·
    <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/issues/new?template=feature_request.yml">Request a feature</a>
  </p>

</div>

## Demo

### In Action

![AF Media Bar in action](./运行展示.gif)

### Component Customization

![AF Media Bar component customization](./组件自定义.gif)

## Overview

AF Media Bar is a portable taskbar media controller for Windows 11. It reads Global System Media Transport Controls (GSMTC) sessions, displays artwork, title, and artist, and provides previous, play/pause, next, and source switching controls.

The interface is an independent WPF top-level window aligned with the taskbar. It does not modify or inject code into `explorer.exe`. Any player that publishes a GSMTC session can be discovered, including NetEase Cloud Music, QQ Music, Spotify, major browsers, VLC, PotPlayer, Windows Media Player, mpv, and foobar2000.

## Features

| Category | Capabilities |
| --- | --- |
| Media | Artwork, title, artist, previous, play/pause, next, and multiple source selection |
| Source interaction | Return to the current media app and switch sessions with the mouse wheel |
| Taskbar behavior | Manual placement and locking, experimental automatic avoidance, auto-hide and fullscreen handling |
| Audio devices | List and switch the default output device, including delayed wheel selection |
| App volume | Match the selected media process and adjust its Windows mixer volume in 2% steps |
| Visualizer | Nine-band spectrum from WASAPI loopback capture |
| Metrics | Optional system memory, CPU, GPU, and AF Media Bar process memory |
| Low-spec mode | Software rendering with transitions, marquees, and fades disabled |

Available commands depend on what each media application exposes through GSMTC.

## How It Works

```mermaid
flowchart LR
    A[Media apps] -->|GSMTC sessions| B[AF Media Bar]
    C[Windows Core Audio] -->|Devices, volume, loopback| B
    D[Windows 11 taskbar] -->|Position and auto-hide state| B
    B --> E[Independent WPF overlay]
```

The Windows 11 media card is an internal Explorer/Shell surface rather than a supported embeddable control. AF Media Bar uses the public GSMTC API behind that card and renders its own interface, avoiding Explorer injection and its stability risks.

## Installation

### Requirements

- Windows 11 x64
- No separate .NET installation is required for the recommended self-contained package

> [!IMPORTANT]
> Disabling Windows 11's **Automatically hide the taskbar** option is recommended while using AF Media Bar. Auto-hide tracking is supported, but its reveal and retract animation still needs further smoothness improvements. A fixed taskbar and fullscreen hiding are not affected by this limitation.

### Recommended package

1. Open [Releases](https://github.com/Fervent-Tempo/AF-Media-Bar/releases).
2. Download `AFMediaBar-vX.Y.Z-win-x64.zip`. Do not download GitHub's automatically generated source archives.
3. Extract the package to get one self-contained `AFMediaBar.exe`; the archive no longer contains hundreds of .NET runtime files.
4. Place it in a permanent writable directory, such as `%LOCALAPPDATA%\Programs\AFMediaBar`, and run it.
5. Right-click the player or tray icon to configure startup, visible modules, and placement.

AF Media Bar is portable and currently has no installer. Release binaries are not commercially code-signed, so Windows SmartScreen may show an unknown publisher warning on first launch. Download only from this repository and use `SHA256SUMS.txt` from the same Release to verify the archive.

## Basic Usage

| Action | Result |
| --- | --- |
| Hover over the bar | Expand media controls |
| Click artwork or title | Return to the selected media app |
| Scroll over the media area | Switch between GSMTC sessions |
| Click the output device button | Open the render device list |
| Scroll over the device button | Preview a device and apply it after one second |
| Click the volume button | Open the selected media app volume slider |
| Scroll over the volume button | Change application volume in 2% steps |
| Drag the artwork/title area | Move an unlocked manually placed bar |
| Right-click the bar or tray icon | Open settings or exit |

Some players require “system media controls,” “media keys,” or “SMTC” to be enabled in their own settings.

## Updating and Uninstalling

To update, exit AF Media Bar from the tray menu, extract the new release, and replace the old executable. When upgrading from 1.0.0, the remaining runtime files in the old directory can be removed after the app exits. Settings remain in the current user's registry.

To uninstall, disable startup from the context menu, exit the application, and delete its directory. To remove settings as well:

```powershell
reg.exe delete "HKCU\Software\AFMediaBar" /f
reg.exe delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "AF Media Bar" /f
```


## Troubleshooting

### No media session appears

Make sure the app is actively playing media and has system media controls enabled. Browsers normally create a session only while a tab is playing audio or video.

### The bar overlaps taskbar icons

Manual placement is the default. Unlock the position, drag the bar to an empty area, and lock it again. Automatic avoidance is experimental and may be affected by third-party taskbar tools or Windows updates.

### Output device switching fails

Device enumeration uses supported Windows APIs, but changing the default endpoint relies on the undocumented `PolicyConfig` COM interface. Windows updates, managed-device policies, or unusual drivers may block this operation without affecting media controls.

### Application volume is unavailable or targets the wrong process

Volume control matches the GSMTC source to Windows audio sessions. Browser process models, multiple streams in one process, and custom audio engines can make a unique match impossible.

### Resource usage is higher than expected

Disable unused metrics and the audio visualizer, or enable low-spec mode. The visualizer reads WASAPI loopback data every 50 ms while enabled.

## Technical Limitations

- The bar is a top-level overlay aligned with the taskbar, not an Explorer plugin.
- Output switching uses the undocumented Windows `PolicyConfig` interface and may change in future Windows releases.
- Automatic placement depends on Windows UI Automation and may not recognize customized taskbars.
- Auto-hide taskbar tracking can still show slight animation delay; disabling Windows taskbar auto-hide is recommended.
- The current instance follows the primary monitor taskbar only.
- Browsers decide whether multiple tabs appear as one or multiple GSMTC sessions.
- Only a `win-x64` package is currently published; ARM64 is not yet available.

## Privacy and Security

- No telemetry, advertisements, accounts, or network analytics are included.
- Media metadata, system metrics, and audio operations stay on the local machine.
- The app runs as the current user, does not request elevation, and does not inject into Explorer.
- Report security issues privately according to [SECURITY.md](SECURITY.md).

## Building from Source

Windows 11, the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), and PowerShell are required.

```powershell
git clone https://github.com/Fervent-Tempo/AF-Media-Bar.git
cd AF-Media-Bar
dotnet restore .\AFMediaBar.csproj
dotnet build .\AFMediaBar.csproj -c Release --no-restore
dotnet run --project .\AFMediaBar.csproj
```

Create a self-contained single executable for end users:

```powershell
dotnet publish .\AFMediaBar.csproj -c Release -r win-x64 --self-contained true -o .\artifacts\AFMediaBar-win-x64
```

## Project Structure

```text
AF-Media-Bar/
|-- .github/
|   |-- ISSUE_TEMPLATE/     # Issue forms
|   `-- workflows/          # Build and release workflows
|-- assets/                 # README and branding images
|-- Interop/                # Windows native API interop
|-- Models/                 # Media, audio, metrics, and taskbar models
|-- Services/               # Media, audio, placement, and tray services
|-- App.xaml                # WPF application resources
|-- App.xaml.cs             # Startup and exception handling
|-- MainWindow.xaml         # Main interface layout
|-- MainWindow.xaml.cs      # Main window interaction and coordination
|-- AFMediaBar.csproj       # .NET project and publish configuration
|-- app.manifest            # Windows application manifest
|-- icon.ico                # Application icon
|-- 运行展示.gif             # In-app demonstration
|-- 组件自定义.gif           # Component customization demonstration
|-- README.md               # Chinese documentation
`-- README.en-US.md         # English documentation
```

## TODO

- Improve tracking animation smoothness when the Windows taskbar is set to auto-hide.
- Complete automatic avoidance of taskbar icons.

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) before submitting an issue or change. Bug reports should include the Windows version, AF Media Bar version, media player, and complete reproduction steps.

See [CHANGELOG.md](CHANGELOG.md) for release history.

## License

AF Media Bar is available under the [MIT License](LICENSE).

<div align="center">

If AF Media Bar is useful to you, consider starring the repository.

</div>
