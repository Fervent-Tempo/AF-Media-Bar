> 中文日志见： [CHANGELOG.md](CHANGELOG.md).

# Changelog

All notable changes to AF Media Bar are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.6.0] - 2026-10-24

Make the new audio controls, vertical taskbars and desktop floating mode generally available; extend device management, media-source and spectrum selection; add opt-in Bluetooth battery and memory-tool experiments.

### Added

- Add device aliases, enabled and disabled groups, drag ordering, disconnected-device settings and shortcuts to Windows sound settings. Record input and output cycling hotkeys, cancel with Esc or remove a binding.
- Remember application volumes adjusted in AF and restore them in new audio sessions for the corresponding application, without a separate configuration switch.
- Add a Bluetooth battery experiment for one manually selected paired device, with a widget shown when connected or only when low, plus optional alerts. The experiment, widget and alerts default to off and require Windows battery properties or standard BLE support.
- Add experimental AF memory diagnostics, manual reclamation and operation results. System cleanup supports on-demand or persistent authorization, standard and deep levels, and opt-in timer or threshold policies. Standard cleanup clears low-priority standby pages; deep cleanup also handles the system file cache and all standby pages, without trimming other applications' working sets.
- Add automatic, latest-session and preferred-application media selection, save stable source preferences and retain recovery state for manually selected sources.
- Add spectrum endpoint strategies for the media application's actual output, the system default or a specific device. Default-device changes can rebuild capture; disconnected fixed devices retain their selection and pause capture. Endpoint strategies apply only to device capture; process capture still spans endpoints.
- Add list-launch and select-then-launch interactions, a shared selected target and application icons on hover. List launch remains the default; the alternative supports wheel selection and double-click launch.
- Add four-edge collapse, edge indicators and saved docking direction to floating mode. Edge collapse defaults to on and can be disabled in Display and placement.
- Add an external full-panel handle with click or hover activation, adapting to taskbar and floating expansion directions. The handle defaults to visible with click activation and hides without media.
- Add long-line scrolling for syllable-timed lyrics in follow-content mode, enabled by default when a line exceeds the width cap.
- Add display-only source-format cleanup, enabled by default, and an optional filter for Japanese kana annotations in parentheses, disabled by default.

### Changed

- Make the new audio controls, input devices and application routing available without an experiment switch. Floating mode and vertical taskbars are directly configurable; legacy switches no longer gate them, while saved modes and geometry remain intact.
- Move widgets to a dedicated settings page, unify device ordering, cards, group titles, related-setting links and list hierarchy, simplify title descriptions and improve icon alignment. Group release highlights by topic with expandable items.
- Reorganize media information and controls in the full panel. Adapt the external handle to its background, with interruptible visibility transitions, a larger hit area and a leave delay.
- Batch foreground-color sampling and reuse buffers, add interruptible color transitions, reuse performance text nodes, settings resources and artwork geometry, and defer audio-flyout and spectrum-buffer creation.
- Keep settings schema 2 and continue reading 1.5.0 settings and user-default snapshots. New experiments and automatic memory policies default to off; long-line scrolling and format cleanup default to on; kana filtering defaults to off. Keep update schema 1, minimumSupportedVersion 1.2.0 and non-mandatory updates.
- Add structured topics and items to release highlights while retaining legacy string arrays. Generate catalogue and candidate content from the same release notes, preserving compatibility with older clients.

### Fixed and improved

- Fix long-lyric width refresh and uneven scrolling, redundant frames when displayed text is unchanged, and interrupted transitions when changing highlighting or filtering. Remove format markers from syllables consistently, retain parenthesized vocals and exclude Japanese lyrics from Chinese conversion.
- Reduce repeated lyric retrieval while paused and limit automatic failure retries for the same track. Improve handling of missing SMTC notifications, manual-source recovery and stale media snapshots.
- Fix full-panel entry drift during in-place artwork zoom, reversed layout pushes and whole-bar relayout caused by lyric display settings.
- Fix delayed first taskbar expansion, missing placement polling after hidden startup and overly broad host recovery. Unify content centers and available-height budgets across layouts.
- Fix interrupted quick-launch icon transitions, stale icons during cold reads and changing hit areas while scaling. Clear status feedback after a short interval.
- Fix sidebar selection after settings shortcuts, inaccurate display names, empty callout lines and quick-launch list hierarchy.
- Fix network-rate arrows drifting with centered values in the non-compact two-line layout.
- Atomically mark update downloads busy before starting work to prevent simultaneous downloads from two entry points. Restore download-state logging and verify concurrency rejection and shutdown with a controlled local server.
- Discard queued battery results after device changes and publish startup and configuration state on the UI thread. Add resource-baseline comparison and output clearing to developer tools.

### Internal and release

- Reorganize code by responsibility, reduce redundant tests and isolate native-window, UIA and settings-contract fixtures. Refresh sponsor information and release checks; test-count changes do not establish changes in functional coverage.
- Retain the MIT license and correct attribution for independently implemented features; the temporary GPL change in intermediate commits is not part of this release.
- Retain draft prereleases, candidate-metadata review and explicit stable promotion. Generate package sizes and SHA-256 values from final artifacts; the production release chain still has no code signing.

## [1.5.0] - 2026-10-17

Add opt-in experiments for vertical taskbars and desktop floating mode, with improvements to narrow taskbars, Settings ordering and artwork interaction.

### Added

- Add an Experimental features page. New audio controls, vertical taskbars and desktop floating mode are off by default and can be toggled at runtime; related options, search results and commands follow their switches.
- New audio controls provide input-device switching, per-application device routing, device blacklists and advanced switching policies. Turning the experiment off restores the previous output-device and volume interface while retaining experiment settings; existing Windows application-routing preferences are not cleared automatically.
- Add a microphone component and input-device wheel actions to the new audio controls. Input and output buttons can target the system default or foreground application separately for clicks and wheel actions, with optional communication-device switching.
- Support real left- and right-side taskbars, with native vertical titles, artists and lyrics. Quick controls, artwork and the full panel open toward the desktop.
- Add desktop floating mode with horizontal or vertical layout, desktop or always-on-top layers, dragging within each target display's work area and saved positions per display. Taskbar and floating modes are mutually exclusive.
- Allow manual lyric-source priority, sequential or parallel retrieval, and ordered or first-available results.

### Changed

- Unify implemented display modes under Display and placement; collapse unimplemented modes by default. Keep experiment switches and links in Experimental features instead of duplicate configuration controls.
- Edit only the active runtime mode. Share fonts, font size, appearance, lyrics, components and interactions; save display, position, locking, direction, layer, length and thickness separately by mode. Failed switches retain the previous host and editing context.
- Replace ordering arrows with drag handles for components, secondary lyrics, lyric sources, metrics and quick launch. Add live displacement, edge scrolling and Alt+Up/Down keyboard ordering, plus subtle hover feedback for ordinary Settings cards.
- Reduce padding on narrow taskbars and prioritize the current lyric or title when space is limited. Restore secondary information when space returns without changing saved visibility preferences.
- Use a compact application list with separate device details in the new volume mixer, and click application icons to mute. Use scrollable device menus and group managed devices by availability.
- Keep settings schema 2, reading 1.4.0 settings and user-default snapshots. Missing experiment fields default to off. Keep update-manifest schema 1.

### Fixed and improved

- Fix repeated host rebuilding and flicker during initial vertical-taskbar activation, exclude the application's own controls from taskbar occupancy probing, and discard stale foreground-color samples directly.
- Make floating-mode dragging update only window position for translation, avoiding repeated layout, text rendering and settings projection. Save position and refresh color sampling after dragging ends.
- Fix title and lyric-region clipping after orientation changes. Retain hover and full panels through lyric-line and ordinary content-length changes, while continuing popup cleanup for real placement or environment changes.
- Improve vertical lyric ordering and transitions, two-line metrics, spectrum direction, hover materials and artwork stacking.
- Fix enlarged artwork shrinking and expanding again when clicked to play or pause. Keep the full image visible, use a centered 24-DIP artwork-colored icon, and position wheel tooltips outside the artwork.
- Improve interrupted taskbar animations, connection transitions and popup input release, reducing stale state during auto-hide, dragging and host switches.
- Fix context and audio menus closing unexpectedly with auto-hide taskbars, and unify popup dismissal. Restore displayed volume and mute state after failed writes; fix lost application icons and device-menu theming.

## [1.4.0] - 2026-10-12

Reorganize Settings, extend taskbar appearance, artwork, lyrics and metrics, and improve placement, media recovery and shutdown reliability.

### Added

- Customize the frosted taskbar background with opacity and neutral, cool, warm or artwork styles. Control tint, borders, texture and idle background independently; receive a one-time compatibility notice when using a transparent taskbar. Choose an idle-note color, with the accent color remaining the default.
- Choose left or right content arrangement, following taskbar alignment until explicitly selected. Add artwork-edge playback progress, enabled by default, and off, in-place zoom or large-preview artwork hover modes.
- Convert displayed lyrics between Simplified and Traditional Chinese, off by default. Conversion does not change lyric sources or fetch lyrics again.
- Add network upload and download rates, cycling or parallel display, and metric ordering to the performance widget.
- Select spectrum capture from the current output device, all applications or the current media application. Existing settings continue using the current device.
- Add Vietnamese to the interface and installer. Add release history to Application settings; stable promotion appends Chinese highlights while retaining earlier releases.
- Bundle developer and sponsor lists in About, showing local content before an on-demand refresh. Older online snapshots cannot replace newer bundled lists; failed updates retain names and offer a retry.
- Offer lyric-view recovery or restart through system notifications. Add restart confirmation and process handoff, plus notification previews, lyric recovery and diagnostic tools in developer mode, off by default.

### Changed

- Separate application appearance, taskbar appearance, content and layout, and screen and placement in Settings. Retain stable search navigation; support precise slider input and improve page transitions, selection feedback and card interactions.
- Group component visibility and ordering separately. Reset settings according to each page's responsibility, preferring user-default snapshots. Hide the monitor selector for independent configuration that is not implemented yet.
- Center media text in new settings while retaining explicitly saved alignment. Use theme colors consistently for playback feedback and artwork progress.
- Keep settings schema 2 and continue reading 1.3.2 settings and user-default snapshots, using defaults for added fields. Keep update-manifest schema 1, in-app updates for installed copies, and no automatic installation for portable copies.

### Fixed and improved

- Respond more promptly to taskbar icon changes and available space. Limit width or hide when space shrinks, then return to the original position after stable recovery. Coalesce repeated space notices and fix hover-button jitter and placement recovery.
- Fix state conflicts between artwork zoom, pause feedback and dragging, and a pause overlay remaining after media disconnects. Preserve artwork proportions in large previews.
- Measure long titles using their original text and scroll two copies continuously. Limit text-region width and improve clipping and loop seams.
- Coalesce queued media refreshes and prioritize source state. Retain the actual read time for NetEase progress to improve timeline accuracy. Show readable failures for system media controls and startup services.
- Warm up Chinese-conversion dictionaries and convert text in the background, reusing presentation text. Improve WebView2 failure fallback, recovery, cancellation and disposal.
- Fix tray audio-flyout display and disposal timing, show full volume-source names, and remove duplicate media information.
- Protect window closure before shutdown, stop background services asynchronously and bound final cleanup. Improve restart handshakes, reducing the risk of desktop stalls or lingering processes.
- Fix native backdrop changes not refreshing immediately, repeated sidebar clipping updates, and related Settings navigation issues.
- Place the caret at the end on the first click in an exact numeric input, while retaining normal selection during editing. Combine list-update feedback and retry at the top of Developers; retain the button position and prevent repeated clicks while updating.

### Internal and release

- Update settings release registration, third-party notices and sponsors, with regression coverage for Settings, taskbar, media, spectrum and lifecycle behavior.
- Retain draft preview releases, candidate-metadata review and explicit stable promotion, generating checksums from final packages. The production release pipeline does not currently include code signing.

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

[1.4.0]: https://github.com/Fervent-Tempo/AF-Media-Bar/compare/v1.3.2...v1.4.0
[1.3.2]: https://github.com/Fervent-Tempo/AF-Media-Bar/compare/v1.3.1...v1.3.2
[1.3.1]: https://github.com/Fervent-Tempo/AF-Media-Bar/compare/v1.3.0...v1.3.1
[1.3.0]: https://github.com/Fervent-Tempo/AF-Media-Bar/compare/v1.2.1...v1.3.0
[1.2.1]: https://github.com/Fervent-Tempo/AF-Media-Bar/compare/v1.2.0...v1.2.1
[1.2.0]: https://github.com/Fervent-Tempo/AF-Media-Bar/compare/v1.1.1...v1.2.0
[1.1.1]: https://github.com/Fervent-Tempo/AF-Media-Bar/compare/v1.1.0...v1.1.1
[1.1.0]: https://github.com/Fervent-Tempo/AF-Media-Bar/compare/v1.0.1...v1.1.0
[1.0.1]: https://github.com/Fervent-Tempo/AF-Media-Bar/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/Fervent-Tempo/AF-Media-Bar/releases/tag/v1.0.0
