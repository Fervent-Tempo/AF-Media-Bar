<p align="center">
  <img src="docs/assets/readme/hero-en.svg" width="100%" alt="AF Media Bar layout illustration: the media bar sits at the lower left of the Windows taskbar, its full media popover is directly above, and the app icon and project introduction are on the right">
</p>

<p align="center">
  <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/releases"><img src="https://img.shields.io/github/v/release/Fervent-Tempo/AF-Media-Bar?style=flat-square&amp;label=release" alt="release"></a>
  <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/releases"><img src="https://img.shields.io/github/downloads/Fervent-Tempo/AF-Media-Bar/total?style=flat-square&amp;label=downloads" alt="downloads"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/Fervent-Tempo/AF-Media-Bar?style=flat-square&amp;label=license" alt="license"></a>
  <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/actions/workflows/build.yml"><img src="https://img.shields.io/github/actions/workflow/status/Fervent-Tempo/AF-Media-Bar/build.yml?style=flat-square&amp;label=build&amp;branch=main" alt="build"></a>
  <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/stargazers"><img src="https://img.shields.io/github/stars/Fervent-Tempo/AF-Media-Bar?style=flat-square&amp;label=Stars&amp;color=gold" alt="Stars"></a>
  <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/forks"><img src="https://img.shields.io/github/forks/Fervent-Tempo/AF-Media-Bar?style=flat-square&amp;label=Forks" alt="Forks"></a>
  <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/graphs/contributors"><img src="https://img.shields.io/github/contributors/Fervent-Tempo/AF-Media-Bar?style=flat-square&amp;label=contributors" alt="contributors"></a>
  <a href="docs/localization.md"><img src="https://img.shields.io/badge/UI%20languages-4-0078d4?style=flat-square" alt="UI languages"></a>
  <br>
  <a href="README.md">简体中文</a> · English · <a href="README.vi-VN.md">Tiếng Việt</a>
  <br>
  <a href="#download-and-run">Download</a> · <a href="#live-demo">Live demo</a> · <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/issues/new?template=bug_report.yml">Report a bug</a> · <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/issues/new?template=feature_request.yml">Request a feature</a> · <a href="docs/localization.md">Help translate</a> · <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/discussions">Discussions and help</a>
</p>

AF Media Bar is a media controller for the Windows 10/11 taskbar. It reads what's playing from the system media session and makes media information, lyrics, playback controls, and device switching easier to see and use on your desktop.

## Live demo

<p align="center">
  <img src="docs/assets/readme/展示.gif" width="80%" alt="AF Media Bar running on a Windows taskbar with artwork, playback controls, and media information">
</p>

[Watch the introduction video on Bilibili](https://www.bilibili.com/video/BV17yhh6aEgK)

## Download and run

Open [GitHub Releases](https://github.com/Fervent-Tempo/AF-Media-Bar/releases) and choose a package:

1. **Installer (recommended):** Download `AFMediaBar-Setup-vX.Y.Z-win-x64.exe` and follow the installation wizard.
2. **Portable:** Download `AFMediaBar-vX.Y.Z-win-x64.zip`, extract it to a writable folder, and run `AFMediaBar.exe`.

**Requirements:** Windows 10 1809 or later, x64, with Microsoft Edge WebView2 Runtime. Install the Evergreen Runtime if it is missing. Release packages include the .NET runtime.

## Updating and uninstalling

- **Updates:** The app notifies you when a new version is available. Check for updates on the Application page. Installed builds can download and install updates in the app; portable builds require manual file replacement.
- **Settings:** Stored in `%LOCALAPPDATA%\AFMediaBar\settings.json`. Open the settings folder from the Application page.
- **Uninstall:** Remove installed builds through Windows Installed apps or the Start menu. For portable builds, delete the program folder. To clear settings and cache, also delete `%LOCALAPPDATA%\AFMediaBar` manually.

## Privacy and security

- No telemetry, ads, or account system. Media controls and volume operations run locally.
- Update checks and online lyric searches use the internet. Lyric searches use track and artist information; lyric sources can be disabled in Settings.
- The app runs with the current user's rights. Report security issues privately as described in [SECURITY.md](SECURITY.md).

## Star history

<a href="https://www.star-history.com/?repos=Fervent-Tempo%2FAF-Media-Bar&amp;type=date&amp;legend=top-left">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=Fervent-Tempo/AF-Media-Bar&amp;type=date&amp;theme=dark&amp;legend=top-left">
    <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/chart?repos=Fervent-Tempo/AF-Media-Bar&amp;type=date&amp;legend=top-left">
    <img src="https://api.star-history.com/chart?repos=Fervent-Tempo/AF-Media-Bar&amp;type=date&amp;legend=top-left" width="100%" alt="AF Media Bar GitHub star count over time">
  </picture>
</a>

### Project activity

![AF Media Bar repository activity and contribution statistics](https://repobeats.axiom.co/api/embed/5c84aada847e73d1f944fc22151a821d0fb2a9a9.svg "Repobeats analytics image")

## Project Structure

Application source is in `src/`, tests in `tests/`, and development tools in `tools/`. See the [architecture document](docs/architecture.md#仓库目录) for the directory overview and responsibilities, and the [contribution guide](CONTRIBUTING.md#development-and-validation) for build and test instructions.

## Contributing

Read the [contribution guide](CONTRIBUTING.md) before contributing, and see the [architecture document](docs/architecture.md) for architectural boundaries.

Bug reports should include your Windows version, app version, player, and reproduction steps. See [CHANGELOG.en-US.md](CHANGELOG.en-US.md) for version history.

## Localization and translation

The interface currently supports **简体中文, 繁體中文, English, and Tiếng Việt**. Choose a language on the Application page. The README is available in Chinese, English, and Vietnamese.

Translation contributions in every language are welcome, including new languages and improvements to existing wording. See the [translation contribution guide](docs/localization.md#translation-guide). Use [translation feedback](https://github.com/Fervent-Tempo/AF-Media-Bar/issues/new?template=localization.yml) to report incorrect, missing, or clipped text, or to contribute a new language.

## Acknowledgements

### Contributors

Thank you to all the developers who have contributed to AF Media Bar.

[![AF Media Bar contributors](https://contrib.nn.ci/api?repo=Fervent-Tempo/AF-Media-Bar&cols=12&radius=24&no_bot=true)](https://github.com/Fervent-Tempo/AF-Media-Bar/graphs/contributors)

### Open-source projects

Thanks to the following open-source projects:

- [FluentFlyout](https://github.com/unchihugo/FluentFlyout): A simple, modern Windows volume flyout.
- [Lyricify-Lyrics-Helper](https://github.com/WXRIW/Lyricify-Lyrics-Helper): A Lyricify lyrics library for parsing, generating, searching, decrypting, and refining lyrics.
- [TaskbarLyrics](https://github.com/ANYNC/TaskbarLyrics): A Windows taskbar lyrics tool.
- [NAudio](https://github.com/naudio/NAudio): Windows audio capture and processing.
- [WPF UI](https://github.com/lepoco/wpfui): Fluent WPF controls.

## Sponsor

<div align="center">

If AF Media Bar helps you, a Star is appreciated ❤️

</div>

Buy me a coffee. **Sponsorships above 10 CNY can join the sponsor list — please leave your ID in the payment note.**

<div align="center">

| WeChat | Alipay |
| :---: | :---: |
| <img src="src/AFMediaBar/Assets/Sponsor/wechat-pay.png" alt="WeChat payment code" width="220"> | <img src="src/AFMediaBar/Assets/Sponsor/alipay-pay.png" alt="Alipay payment code" width="220"> |

</div>
