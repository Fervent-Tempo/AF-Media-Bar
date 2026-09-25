> 中文日志见： [CHANGELOG.md](CHANGELOG.md).

# Changelog

All notable changes to AF Media Bar are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

Reliability fixes: media-session self-healing, spectrum level calibration, and lyric advancement.

### Added

- Lyric spacing: the lyrics page gains a "line gap" and a "character spacing" setting. The line gap adds extra spacing on top of the existing two-row layout (0–24% of the font size in two-percent steps, default 0 meaning the look is unchanged); it scales with the font, tightens automatically when space runs short, and never clips or shrinks the text. The character spacing widens the gap between characters as a percentage of the font size (0–20% in one-percent steps, default 0), scales with the font, applies to CJK and Latin alike, and never breaks words apart. Both are native CSS layout inside the web lyrics view, so they are continuous and take effect immediately.
- Fixed lyric-box length: the lyrics page gains a switch and a length slider (80–600 DIP, 240 by default). While enabled the lyric box keeps a fixed length instead of resizing with every line and keeps every line's alignment stable; it is off by default (the box follows the content).
- Instant line resizing: when the lyric line changes the bar now lands on the new length immediately instead of animating into it, so no brief ellipsis appears right after a line change.

### Fixed

- Losing media sessions permanently after a single missed SMTC event (for example at a track change): an auto-reconcile watchdog now heals on a one-second cadence for thirty seconds after a session closes and falls back to five seconds, and it rebuilds the media catalog when the third-party library is stuck beyond what ForceUpdate can fix.
- The spectrum standing still at its minimum bar height while listening at low volume: the level mapping now uses a relative-dB window around a reference peak, so bar heights no longer shrink with the system volume; the capture also follows the device that is actually audible (an application stream outranks the system mixer's echo, and the current endpoint is kept within one rank), so a virtual audio driver routing music to a non-default endpoint no longer leaves the spectrum silent (the target is checked every two seconds and the capture is rebuilt on a change).
- Lyrics not advancing when a player stops reporting playback progress (its timeline stays at the track start): lyric line selection now uses the same extrapolated position as the progress bar and is advanced by the existing 250 ms progress timer.
- Lyrics stuck on title/artist after a track change: when the player's timeline for a new track stalls at zero and is never refreshed (measured with Ceru Music), the hidden "waiting for the first line" frame stopped the lyric frame timer, so nothing re-projected with the wall clock afterwards; the timer now keeps running whenever media is playing with lyrics loaded, so the first line shows up on time without a pause and resume.

### Improved

- Spectrum resolution and look: the FFT size now follows the sample rate (96 kHz goes from 512 to 4096 points), band values integrate power with fractional-bin linear interpolation (low bands no longer share one bin with their neighbour, so the leading columns stop sharing one height), and a +3 dB/octave tilt in the power domain compensates the natural roll-off for a more balanced look.

## [1.2.1] - 2026-09-21

A fix release: the taskbar auto-hide animation, misplacement while capturing the screen, and multi-monitor display.

### Added

- The bar can be shown on every enabled taskbar at once.
- A syllable-highlight switch: turning it off keeps lyrics scrolling on the same timeline and trajectory.

### Fixed

- Taskbar auto-hide animation: stutter and jumping when the bar hides against the screen edge.
- The bar moving or collapsing while a screenshot or screen recorder is open.
- The performance-metrics component going dead after switching displays.
- A race between background memory pruning and releasing the log queue.
- The settings page crashing when a callout resource is missing, plus several window-stability issues.
- Wheel gestures and their tooltip scoped to the artwork and text region; the full layer no longer trims information.

## [1.2.0] - 2026-09-19

The first release of the rebuilt interface and interaction model: taskbar lyrics, an installer, and target-display selection are new capabilities, and the settings pages, appearance, and interactions were reorganised by function.

### Added

- Taskbar lyrics: sources are matched in order across NetEase Cloud Music, LRCLIB, QQ Music, Kugou Music, and Soda Music, with syllable-by-syllable reveal, unsung-part shading, translation, romanization, and two-line alignment; sources can be enabled, disabled, and reordered on the lyrics page.
- An installer: installed copies can check for updates in-app and download and silently install later versions, while the portable build stays a single file that writes no registry entries.
- Target display selection: the bar can follow the taskbar on a chosen display, recovering automatically after DPI changes or an Explorer restart.
- Quick launch: with no acceptable media, clicking or scrolling the note opens your own list, wheel-previews entries, and starts the last one about 1.2 s after scrolling stops.
- Now-playing track notification: shown once a track changes and starts playing, with an adjustable placement, duration, and display, and optional fullscreen suppression.
- An SMTC app allow list: only sessions published by selected apps are accepted.
- An audio control panel: tray click and wheel can be bound to the panel, Settings, or the device/volume entries.
- New spectrum styles: waveform and pixel bars.
- An About page: developers (GitHub contributors with avatars), sponsors, payment codes and the Afdian entry, and the open-source licence list.
- Automatic background memory trimming: graded reclaim while idle, with the display off, and while suspending, one more reclaim after startup settles, and a compress-memory-now action in Settings.
- An application log with unhandled-exception capture, and entries in Settings that open the log and settings folders.

### Improved

- Settings were reorganised into Display modes, Media & Notifications, Interaction, Lyrics, Appearance, and Application, plus About in the footer, with rebuilt group containers, group strips, and a fixed page header, and keyword search that jumps to the matching group.
- Unified appearance: fonts, font size, light/dark theme, window material, and material concentration now apply globally, and the accent colour follows the system by default or can be picked or entered as hex.
- Interface language: Simplified Chinese, Traditional Chinese, and English, following the Windows display language by default and applying immediately without a restart.
- Taskbar behaviour: artwork keeps its own aspect ratio, text that does not fit rotates inside its own region (a line being revealed follows its highlight), and the bar can auto-hide while nothing plays.
- Audio: the output-device list is stably ordered, the current media app is adjusted in 2% steps, and spatial audio can be inspected and the Windows sound settings opened from there.
- Windows 10: window material and the dark title bar fall back by system version.
- Low-performance fallback: decorative motion, marquees, spectrum easing, and blur are disabled automatically under software rendering or a low-performance path.
- The update check requests public manifest endpoints only and never goes through a third-party proxy; downloads are verified against SHA-256 while they download and retried through the accelerators listed in the manifest when the direct connection fails.

### Fixed

- Fixed automatic media following overriding a source that was selected manually.
- Fixed the marquee being displaced when hover begins and the full layer not opening after the hover layer is collapsed.
- Fixed the missing wheel tooltip over the media area and the missing quick-launch preview tooltip.
- Fixed the Windows 10 taskbar search box not being avoided and the offset when the bar is dragged manually.
- Fixed a possible crash when a media session is closed during playback and the wrong size of the first track notification.

### Compatibility and Limitations

- Vertical taskbars and floating mode are no longer offered: this version supports the horizontal taskbar only, and Dynamic Island, Desktop Card, and Floating Orb in Settings are placeholders that only change what the page shows.
- The grid layout editor is no longer part of Settings: layout profiles written by 1.1.1 and earlier (`%LOCALAPPDATA%\AFMediaBar\profiles\layout.json`, schema 5) are no longer used and the interface returns to a fixed layout.
- **Upgrading resets your settings**: this version reads only its own schema number (2) and never reads the older file that 1.1.1 wrote; that file is renamed to `settings.json.unsupported-<timestamp>` and kept, and the app starts from the defaults. The "my defaults" snapshot is handled the same way, so the settings have to be configured once more.
- Upgrading from 1.1.1 to 1.2.0 means downloading manually: the manifest for this version offers no automatically installable package, and in-app download and silent installation return in 1.2.1.
- Only `win-x64` is published and there is no ARM64 build yet; the published files are not commercially code-signed, so Windows SmartScreen may report an unknown publisher.

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

[1.2.1]: https://github.com/Fervent-Tempo/AF-Media-Bar/compare/v1.2.0...v1.2.1
[1.2.0]: https://github.com/Fervent-Tempo/AF-Media-Bar/compare/v1.1.1...v1.2.0
[1.1.1]: https://github.com/Fervent-Tempo/AF-Media-Bar/compare/v1.1.0...v1.1.1
[1.1.0]: https://github.com/Fervent-Tempo/AF-Media-Bar/compare/v1.0.1...v1.1.0
[1.0.1]: https://github.com/Fervent-Tempo/AF-Media-Bar/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/Fervent-Tempo/AF-Media-Bar/releases/tag/v1.0.0
