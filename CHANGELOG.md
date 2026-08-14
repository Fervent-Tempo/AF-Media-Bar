# 更新日志 / Changelog

此文件记录 AF Media Bar 的所有重要变更。

All notable changes to AF Media Bar are documented in this file.

格式遵循 [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)，项目使用 [Semantic Versioning](https://semver.org/spec/v2.0.0.html)。

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### 计划 / Planned

- 进一步改进自动隐藏任务栏动画跟踪。
  Further improve auto-hide taskbar animation tracking.
- 完成对任务栏图标的自动避让。
  Complete automatic avoidance of taskbar icons.
- 评估 Windows on ARM 支持和多显示器独立任务栏。
  Evaluate Windows on ARM support and per-monitor taskbars.

## [1.1.0] - 2026-08-14

### 新增 / Added

- 增加 Windows 10 兼容支持。
  Added compatibility support for Windows 10.
- 增加系统主题自动跟随和独立主题设置。
  Added automatic system-theme matching and independent theme settings.
- 增加横向与竖向任务栏播放器布局。
  Added horizontal and vertical taskbar player layouts.
- 增加浮动窗口、边缘收起和窗口可见性选项。
  Added floating-window, edge-collapse, and window visibility options.
- 增加自动更新检查、备用版本清单源和跳过版本功能。
  Added automatic update checks, fallback manifest sources, and version skipping.

### 变更 / Changed

- 重构设置窗口和设置菜单。
  Refactored the settings window and settings menu.
- 重构任务栏窗口托管，提高不同任务栏布局下的适配能力。
  Refactored taskbar window hosting to improve adaptation across taskbar layouts.
- 改进双语社区和项目文档。
  Improved bilingual community and project documentation.

### 修复 / Fixed

- 修复右键菜单层级问题。
  Fixed context-menu z-order behavior.

## [1.0.1] - 2026-08-10

### 修复 / Fixed

- 通过 Shell 事件跟踪、原始任务栏几何观察和合成帧更新，降低自动隐藏任务栏展开与收回的延迟。
  Reduced taskbar auto-hide reveal and retract lag with Shell event tracking, raw taskbar geometry observation, and composition-frame updates.
- 保留全屏隐藏行为，同时避免在正常任务栏自动隐藏过渡期间不必要地销毁窗口。
  Preserved fullscreen hiding while avoiding unnecessary window destruction during normal taskbar auto-hide transitions.

### 变更 / Changed

- 替换应用程序和 README 品牌图标。
  Replaced the application and README branding icon.
- 将自包含 `win-x64` Release 包改为单个可执行文件，不再包含数百个运行时文件。
  Changed the self-contained `win-x64` Release package to a single executable instead of hundreds of runtime files.
- 记录当前自动隐藏动画限制，并推荐使用固定任务栏配置。
  Documented the current auto-hide animation limitation and recommended fixed-taskbar configuration.

## [1.0.0] - 2026-08-09

### 新增 / Added

- GSMTC 媒体发现、来源切换、元数据、封面图和播放控制。
  GSMTC media discovery, source switching, metadata, artwork, and transport controls.
- Windows 11 任务栏定位、自动隐藏跟踪、全屏隐藏和托盘集成。
  Windows 11 taskbar placement, auto-hide tracking, fullscreen hiding, and tray integration.
- 默认输出设备切换，以及所选媒体应用音量控制。
  Default output device switching and selected media application volume control.
- WASAPI 回环音频可视化，以及可选的系统/进程指标。
  WASAPI loopback audio visualizer and optional system/process metrics.
- 低配置渲染模式和开机启动支持。
  Low-spec rendering mode and startup support.
- 中文和英文文档，以及自包含 `win-x64` 发布自动化。
  Chinese and English documentation plus self-contained `win-x64` release automation.

### 变更 / Changed

- 将产品重命名为 AF Media Bar，并将可执行文件命名为 `AFMediaBar.exe`。
  Renamed the product to AF Media Bar and the executable to `AFMediaBar.exe`.
- 限制封面图缓冲和长期运行的媒体音量来源缓存。
  Bounded artwork buffering and long-running media-volume source caches.

### 安全 / Security

- 将原生库查找限制到 System32。
  Restricted native library lookup to System32.
- 移除对媒体提供的 `.exe` 来源标识符的通用执行。
  Removed generic execution of media-provided `.exe` source identifiers.

[Unreleased]: https://github.com/Fervent-Tempo/AF-Media-Bar/compare/v1.1.0...HEAD
[1.1.0]: https://github.com/Fervent-Tempo/AF-Media-Bar/compare/v1.0.1...v1.1.0
[1.0.1]: https://github.com/Fervent-Tempo/AF-Media-Bar/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/Fervent-Tempo/AF-Media-Bar/releases/tag/v1.0.0
