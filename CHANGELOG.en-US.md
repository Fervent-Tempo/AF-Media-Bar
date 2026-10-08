> 中文日志见： [CHANGELOG.md](CHANGELOG.md).

# Changelog

All notable changes to AF Media Bar are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.3.2] - 2026-10-06

Improve Settings responsiveness, lyric matching, and desktop interactions, and fix artwork, startup settings, and update installation.

### Added

- Manage global artist separators in Lyrics settings, with add, edit, and remove operations, preserved spaces, and multi-character separators. Search and scoring share the rules while the bar retains the original artist names.

### Improved

- Show system fonts and saved choices immediately in Appearance, then load installed font previews in the background. Repeated navigation shares an active scan, leaving the page cancels its wait, and language changes no longer rescan fonts.
- Remove total track duration from lyric cache identity, reducing repeated retrieval when the reported duration changes for the same track.

### Changed

- Narrow the Settings sidebar from 240 DIP to a fixed 228 DIP, sized for navigation titles, the search prompt, and update notices in all three languages. Retain tooltips for the full search prompt and Media and notifications title.
- Lower the QQ Music immediate-acceptance threshold from 85 to 80. Parallel fallback queries prefer an enabled source matching the current player; without one, candidates continue to compete by score.
- Enable track-change notifications by default, including during fullscreen, at the top center for 3 seconds on the foreground window's display. Existing valid settings and user-default snapshots remain in use.
- Add theme-aware card backgrounds, borders, and metric labels to resource monitoring in the full panel, improving readability in light mode.

### Fixed

- Fix artwork failing to display because of color format, and artwork clipping when it did not follow its actual display dimensions. Transparent pixels no longer produce a false dominant color.
- Fix media-bar displacement and clipping during taskbar auto-hide motion caused by stale parent-window coordinates.
- Fix the media bar moving onto Widgets when the Windows 11 taskbar context menu is open.
- Fix the tray mixer scrollbar covering volume percentages, and a device-selector click both opening the list and accidentally selecting its first entry.
- Fix Settings search candidates disappearing when the control filtered them a second time.
- Fix startup registration not responding correctly to settings changes. Read existing registration on startup, show its current state, and remove only the current user's entry targeting this installation when uninstalling.
- Fix Quick Launch reset ignoring the user's default snapshot, and reset prompts describing an inaccurate scope.
- Continue scanning for valid lyrics after encountering a damaged wrapper.
- Fall back to a solid background when native backdrop calls fail.
- Revalidate installers in the background and retain a read-only file lease. Check cancellation and installation conditions before handoff, avoiding synchronous verification stalls and package replacement during preparation.
- Keep new log entries when the queue is full and flush periodically under continuous load.
- Connect background pruning to power-state notifications and stop startup-reclaim polling at its deadline.

### Internal and compatibility

- Generate checksums from final release packages, create a draft Release and an isolated metadata review PR, and update stable only after explicit promotion. Generate contributor snapshots with release metadata and synchronize legacy client data through a PR.
- Update architecture, code-style, and test guidelines and add regression coverage for the affected paths.
- Keep settings schema 2, preserving 1.3.1 settings and user-default snapshots. Update manifests remain schema 1.

## [1.3.1] - 2026-10-03

This release improves performance and taskbar auto-hide transitions, and fixes lyrics, audio, and update issues from 1.3.0.

### Improved

- Load lyrics on demand and release the view when disabled or lyrics remain unavailable. Attempt suspension when the display is off or the session is locked.
- Stop sampling hidden spectrum widgets and reduce repeated lyric styling, text measurement, spectrum animation, and taskbar positioning.
- The media bar slides with the auto-hidden taskbar while keeping lyrics visible. Clicks, scrolling, and dragging are blocked during motion and restored once it settles.

### Fixed

- Fixed WebView2 data-directory errors when the installation directory is not writable.
- Resetting Display Modes now restores the artwork switch.
- Spatial audio no longer appears enabled when it is off.
- Checking again preserves a ready update and its restart-and-install entry.
- Recheck SHA-256 before installation and accept only valid packages in the application's update directory.

## [1.3.0] - 2026-09-30

This release rebuilds lyric display and media-state transitions, adds appearance and interaction settings, and fixes media, audio, and layout issues.

### Added

- Lyric line spacing, character spacing, and fixed display width. The bar now updates its length promptly when the lyric line changes, avoiding a brief ellipsis or clipped text.
- An opt-in switch for fetching lyrics from browsers and video apps. Changing it clears results fetched under the previous choice.
- NetEase Cloud Music track information and lyrics through memory reading without SMTC, including the Store version. Kugou memory reading now provides progress with a shorter polling interval.
- A separate Components page for idle widgets, with visual controls such as bar width, spacing, and interaction button size in Appearance. Hover-button spacing now has a slider.
- A wheel-hint switch and disabled choices for tray actions. Installed fonts now have previews and Chinese names; Latin and Chinese font selections apply to their respective scripts.
- A rest-layer artwork switch, on by default. Hiding artwork moves media text left; the existing idle-component list still controls the note when no media is connected.
- A Quick Launch submenu below Switch Media Source in the media bar and tray context menus, populated from the latest bound entries each time they open.

### Changed

- Rebuilt lyric display with a WebView2 view for syllable highlighting, translations, romanization, and styling; the view releases its resources when lyrics are unavailable or turned off.
- Added a two-way transition between playback and the idle note, moving artwork, text, and right-side widgets together. Notification artwork follows the bar artwork's aspect ratio. Missing browser media starts leaving the screen after about 500 ms while the backend session-rebuild grace remains in place.
- Lyric retrieval tries enabled QQ Music online first (downloading only matches scoring at least 85), then queries other enabled sources concurrently and adopts the highest score, including a confirmed no-lyrics result. Translated-title search and short-preview scoring are improved. QQ local-cache reading and the obsolete strictness, dispatch, adoption, player-binding, and source-order controls and settings fields were removed.
- Independent NetEase artwork URL downloads were removed; artwork comes from the same-track SMTC session. Matching tolerates guest-artist differences while rejecting artwork from another track.
- Spectrum analysis adapts FFT length to the sample rate, improves band integration and high-frequency balance, shows relative levels at low volume, and follows the endpoint that is actually audible.
- Reorganized Settings navigation, descriptions, and About content. Project information appears first, two sponsors were added, and Afdian links were removed. The unimplemented orientation option and tray modifier-wheel setting are temporarily disabled.
- Removed manual update checking from the taskbar context menu. An update entry appears only when a newer version is known and opens the update page; manual checking remains available in Application settings.

### Fixed

- Fixed-width limits update when taskbar space grows. Rapid width adjustments no longer briefly clip right-side widgets, shift a left-anchored bar, or jitter its components.
- Fixed jumps, twitching, and invalid visual coordinates when media closes; the idle note no longer shows an empty tooltip and idle widgets regain hover feedback.
- Fixed a startup crash after resizing, stale browser media after closing a video, and tray tooltips that did not follow system output-device or volume changes. Hiding wheel-gesture hints keeps the current status and operation-result hints.
- Media sessions recover after missed events. Catalog rebuilds now cool down after repeated failures instead of looping during situations such as exclusive fullscreen games.
- Lyrics advance when a player's timeline stalls, the first line appears after a track change, missing album metadata no longer causes a false match, and QRC parsing and QQ Music empty translations are handled correctly.
- WebView2 graphics faults no longer exit the app; zero-size lyric layouts no longer crash it, and the hidden web spectrum stops consuming CPU in lyric mode.

### Internal and compatibility

- Replaced custom audio-device, app-volume, and spectrum capture code with NAudio.Wasapi. Spectrum capture, metrics sampling, media snapshots, and some player-memory reads run off the UI thread and release their resources on shutdown.
- Taskbar UIA probes reuse one background MTA worker and coalesce pending work per taskbar. Media adapters and source arbitration use shared contracts.
- LRU caches validate size and cost; transient failures expire. SMTC lyric cache keys include complete matching metadata, fallback lookups can retry, and artwork-color keys include theme and extraction parameters.
- Moved all three interface languages into `Resources/*.resx` while retaining runtime language switching. Removed inactive Dynamic Island runtime code and raised the log retention limit to the latest 2,000 entries.
- Settings schema remains 2: 1.2.1 settings still load, obsolete lyric fields disappear when saved again, and new options use defaults. The public update-manifest schema remains 1.

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

[1.3.2]: https://github.com/Fervent-Tempo/AF-Media-Bar/compare/v1.3.1...v1.3.2
[1.3.1]: https://github.com/Fervent-Tempo/AF-Media-Bar/compare/v1.3.0...v1.3.1
[1.3.0]: https://github.com/Fervent-Tempo/AF-Media-Bar/compare/v1.2.1...v1.3.0
[1.2.1]: https://github.com/Fervent-Tempo/AF-Media-Bar/compare/v1.2.0...v1.2.1
[1.2.0]: https://github.com/Fervent-Tempo/AF-Media-Bar/compare/v1.1.1...v1.2.0
[1.1.1]: https://github.com/Fervent-Tempo/AF-Media-Bar/compare/v1.1.0...v1.1.1
[1.1.0]: https://github.com/Fervent-Tempo/AF-Media-Bar/compare/v1.0.1...v1.1.0
[1.0.1]: https://github.com/Fervent-Tempo/AF-Media-Bar/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/Fervent-Tempo/AF-Media-Bar/releases/tag/v1.0.0
