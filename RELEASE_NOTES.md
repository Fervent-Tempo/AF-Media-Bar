> The English release notes are provided in the second half of this document.

# AF Media Bar 1.2.1

1.2.0 的修复版本：任务栏自动隐藏动画、截图错位与多显示器显示。

## 本次修复

- 任务栏自动隐藏动画：触边收起时的卡顿与跳位。
- 截图或录屏时媒体栏不再错位、不再折叠。
- 可在所有已启用的任务栏上同时显示。
- 新增「逐字高亮」开关：关闭后歌词仍按时间轴滚动，轨迹不变。
- 修复切换显示器后性能监测组件失效。
- 修复后台内存整理与日志队列释放的竞争。
- 修复设置页在说明条资源缺失时崩溃，以及若干窗口稳定性问题。
- 滚轮手势与提示收窄到封面与文字区；完整层不再省略信息。
- 网易云私人 FM 取词、静置层组件顺序与无媒体播放时的显示调整。

## 下载与安装

系统要求、两种安装方式与更新说明：请见 [README](https://github.com/Fervent-Tempo/AF-Media-Bar#readme)（安装程序与便携版都自带 .NET 运行时，无需另装）。

- `AFMediaBar-Setup-v1.2.1-win-x64.exe`：安装程序，支持程序内静默更新。
- `AFMediaBar-v1.2.1-win-x64.zip`：便携版，解压即用。

请勿使用 GitHub 自动生成的 Source code 压缩包；可用同一 Release 的 `SHA256SUMS.txt` 校验。

## 国内下载镜像

- 夸克网盘：[下载地址](https://pan.quark.cn/s/6987e4945b16)
- 百度网盘：[下载地址](https://pan.baidu.com/s/1zUQtZ_N1tnRTjJKd9kKREA?pwd=6ddc)，提取码：`6ddc`
- 蓝奏云：[下载地址](https://amorfate.lanzoue.com/b01eupanbg)，密码：`zzzz`

## 已知限制

- 竖向任务栏与悬浮模式仍未实现，设置中的对应入口是占位选项。
- **从 1.1.1 及更早版本升级会重置设置**（旧文件改名留档为 `settings.json.unsupported-<时间戳>`，可手工找回）；1.2.0 → 1.2.1 不受影响。
- 隐私说明与卸载方式：请见 [README](https://github.com/Fervent-Tempo/AF-Media-Bar#readme)。

本版本没有已确认的阻断性问题。

---

# AF Media Bar 1.2.1

A fix release on top of 1.2.0: the taskbar auto-hide animation, misplacement while capturing the screen, and multi-monitor display.

## Fixes

- Taskbar auto-hide animation: stutter and jumping when the bar hides against the screen edge.
- The bar no longer moves or collapses while a screenshot or screen recorder is open.
- The bar can be shown on every enabled taskbar at once.
- A new syllable-highlight switch: turning it off keeps lyrics scrolling on the same timeline and trajectory.
- Fixed the performance-metrics component going dead after switching displays.
- Fixed a race between background memory pruning and releasing the log queue.
- Fixed the settings page crashing when a callout resource is missing, plus several window-stability issues.
- Wheel gestures and their tooltip are scoped to the artwork and text region; the full layer no longer trims information.
- NetEase private-FM metadata, rest-layer component order, and what is shown while nothing is playing.

## Download and install

Requirements, both installation options, and updating: see the [README](https://github.com/Fervent-Tempo/AF-Media-Bar/blob/main/README.en-US.md#readme) (both files are self-contained and need no separate .NET runtime).

- `AFMediaBar-Setup-v1.2.1-win-x64.exe` — the installer, with in-app silent updates.
- `AFMediaBar-v1.2.1-win-x64.zip` — the portable build, unzip and run.

Do not use GitHub's generated source archives; verify against the `SHA256SUMS.txt` in the same release.

## Mirrors in mainland China

- Quark: [download](https://pan.quark.cn/s/6987e4945b16)
- Baidu: [download](https://pan.baidu.com/s/1zUQtZ_N1tnRTjJKd9kKREA?pwd=6ddc), code `6ddc`
- Lanzou: [download](https://amorfate.lanzoue.com/b01eupanbg), password `zzzz`

## Known limitations

- Vertical taskbars and floating mode are still not implemented; the matching entries in settings are placeholders.
- **Upgrading from 1.1.1 or earlier resets your settings** (the old file is renamed to `settings.json.unsupported-<timestamp>` and kept, recoverable by hand); 1.2.0 → 1.2.1 is unaffected.
- Privacy and uninstalling: see the [English README](https://github.com/Fervent-Tempo/AF-Media-Bar/blob/main/README.en-US.md#readme).

No blocking issues are currently known.
