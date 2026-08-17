> 中文日志见： [CHANGELOG.md](CHANGELOG.md).

# Changelog

All notable changes to AF Media Bar are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Planned

- Display scrolling video subtitles/lyrics.
- Display media progress bars.
- Polish the UI and provide multiple preset themes.
- Add a UI editor so users can deeply customize the appearance.
- Export and share configurations.
- Add an onboarding tutorial.

## [1.1.1] - 2026-08-17

### Changed

- Improved live switching among Simplified Chinese, Traditional Chinese, and English interfaces.
- Refined the settings window, diagnostic logging, and font preset switching experience.
- Improved controls for length, spacing, thickness, independent sizing, font weight, and vertical taskbar offset.
- Improved media-content visibility, artwork corner-radius controls, and automatic layout switching.
- Added quick access to Task Manager from the resource metrics area.
- Removed legacy registry compatibility logic to simplify settings loading.

### Fixed

- Fixed floating-window disappearance and focus interference.
- Fixed browser artwork refresh and media switching during the disconnection grace period.
- Fixed tray media activation, desktop-edge size re-anchoring, and related window recovery behavior.
- Improved automatic media-source switching after playback pauses.

## [1.1.0] - 2026-08-14

### Added

- Added compatibility support for Windows 10.
- Added automatic system-theme matching and independent theme settings.
- Added horizontal and vertical taskbar player layouts.
- Added floating-window, edge-collapse, and window visibility options.
- Added automatic update checks, fallback manifest sources, and version skipping.

### Changed

- Refactored the settings window and settings menu.
- Refactored taskbar window hosting to improve adaptation across taskbar layouts.
- Improved bilingual community and project documentation.

### Fixed

- Fixed context-menu z-order behavior.

## [1.0.1] - 2026-08-10

### Fixed

- Reduced taskbar auto-hide reveal and retract lag with Shell event tracking, raw taskbar geometry observation, and composition-frame updates.
- Preserved fullscreen hiding while avoiding unnecessary window destruction during normal taskbar auto-hide transitions.

### Changed

- Replaced the application and README branding icon.
- Changed the self-contained `win-x64` Release package to a single executable instead of hundreds of runtime files.
- Documented the current auto-hide animation limitation and recommended fixed-taskbar configuration.

## [1.0.0] - 2026-08-09

### Added

- GSMTC media discovery, source switching, metadata, artwork, and transport controls.
- Windows 11 taskbar placement, auto-hide tracking, fullscreen hiding, and tray integration.
- Default output device switching and selected media application volume control.
- WASAPI loopback audio visualizer and optional system/process metrics.
- Low-spec rendering mode and startup support.
- Chinese and English documentation plus self-contained `win-x64` release automation.

### Changed

- Renamed the product to AF Media Bar and the executable to `AFMediaBar.exe`.
- Bounded artwork buffering and long-running media-volume source caches.

### Security

- Restricted native library lookup to System32.
- Removed generic execution of media-provided `.exe` source identifiers.

[Unreleased]: https://github.com/Fervent-Tempo/AF-Media-Bar/compare/v1.1.1...HEAD
[1.1.1]: https://github.com/Fervent-Tempo/AF-Media-Bar/compare/v1.1.0...v1.1.1
[1.1.0]: https://github.com/Fervent-Tempo/AF-Media-Bar/compare/v1.0.1...v1.1.0
[1.0.1]: https://github.com/Fervent-Tempo/AF-Media-Bar/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/Fervent-Tempo/AF-Media-Bar/releases/tag/v1.0.0
