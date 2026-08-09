# AF Media Bar 1.0.0

AF Media Bar 的首个正式版本。它在 Windows 11 任务栏上提供 GSMTC 媒体控制、输出设备切换、当前媒体应用音量、音频可视化和可选系统指标。

## 下载与安装

下载 `AFMediaBar-v1.0.0-win-x64.zip` 并解压，然后运行 `AFMediaBar.exe`。这是推荐的自包含版本，不需要预先安装 .NET 8 Desktop Runtime。

请勿下载 GitHub 自动生成的 Source code 压缩包作为程序使用。可通过同一 Release 中的 `SHA256SUMS.txt` 校验下载文件。

## 主要功能

- 显示媒体封面、标题、作者及上一首、播放/暂停、下一首控制。
- 支持多个 GSMTC 媒体来源并可通过滚轮切换。
- 支持手动任务栏定位、位置锁定、自动隐藏与全屏隐藏。
- 切换 Windows 默认输出设备并调节当前媒体应用音量。
- 可选 WASAPI 九段音频可视化以及内存、CPU、GPU 指标。

## 安装前须知

- 仅支持 Windows 11 x64；当前未提供 ARM64 版本。
- 发布文件尚未进行商业代码签名，Windows SmartScreen 可能显示“未知发布者”。
- AF Media Bar 是贴合任务栏的独立 WPF 顶层浮层，不是 Explorer 插件，也不会向 `explorer.exe` 注入代码。
- 输出设备枚举使用 Windows API，但设置默认设备依赖未公开的 `PolicyConfig` COM 接口。Windows 更新、设备策略或特殊驱动可能导致设备切换不可用。
- 自动定位依赖 Windows UI Automation，第三方任务栏工具或定制布局可能影响识别。
- 当前只跟随主显示器任务栏。

完整安装、卸载、常见问题和隐私说明见 [README](https://github.com/Fervent-Tempo/AF-Media-Bar#readme)。

---

AF Media Bar 1.0.0 is the first public release. Download the self-contained `AFMediaBar-v1.0.0-win-x64.zip`; no separate .NET runtime is required. The app is an independent WPF taskbar overlay, not an Explorer plugin. Default output switching relies on the undocumented Windows `PolicyConfig` COM interface and may be affected by future Windows updates or device policies. See the [English README](https://github.com/Fervent-Tempo/AF-Media-Bar/blob/main/README.en-US.md) for full documentation.
