# AF Media Bar 1.0.1

这是 AF Media Bar 的维护版本，重点改善 Windows 11 自动隐藏任务栏的跟随响应，并将发布包改为单文件形式。

## 下载与安装

下载 `AFMediaBar-v1.0.1-win-x64.zip` 并解压，然后运行其中唯一的 `AFMediaBar.exe`。这是自包含版本，不需要预先安装 .NET 8 Desktop Runtime，也不再包含数百个需要用户翻找的运行时文件。

请勿下载 GitHub 自动生成的 Source code 压缩包作为程序使用。可通过同一 Release 中的 `SHA256SUMS.txt` 校验下载文件。

## 本次改进

- 结合 Shell 事件、任务栏原始位置监测和 WPF 合成帧更新，缩短自动隐藏任务栏出现与收回时的跟随延迟。
- 普通自动隐藏过程中保持窗口存活，减少窗口重建；全屏隐藏行为保持不变。
- 使用新的 AF Media Bar 应用图标和 README 品牌图片。
- `win-x64` 自包含发布包改为单个约 75 MB 的 `AFMediaBar.exe`。

## 已知限制

- 推荐在 Windows 11 设置中关闭“自动隐藏任务栏”后使用。当前版本已经支持自动隐藏任务栏，但跟随动画流畅度仍有提升空间。
- 仅支持 Windows 11 x64；当前未提供 ARM64 版本。
- 发布文件尚未进行商业代码签名，Windows SmartScreen 可能显示“未知发布者”。
- AF Media Bar 是独立 WPF 顶层浮层，不是 Explorer 插件，也不会向 `explorer.exe` 注入代码。
- 设置默认输出设备依赖未公开的 `PolicyConfig` COM 接口，Windows 更新或设备策略可能影响可用性。
- 当前只跟随主显示器任务栏。

完整安装、卸载、常见问题和隐私说明见 [README](https://github.com/Fervent-Tempo/AF-Media-Bar#readme)。

---

AF Media Bar 1.0.1 improves Windows 11 auto-hide taskbar tracking and changes the self-contained `win-x64` package to a single `AFMediaBar.exe`. No separate .NET runtime is required. Disabling taskbar auto-hide is still recommended for the smoothest experience. See the [English README](https://github.com/Fervent-Tempo/AF-Media-Bar/blob/main/README.en-US.md) for full documentation.
