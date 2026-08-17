> The English release notes are provided in the second half of this document.

# AF Media Bar 1.1.1

这是 AF Media Bar 的维护更新，重点修复窗口恢复、媒体切换和封面刷新问题，并改进多语言、外观与设置体验。

## 下载与安装

发布后下载 `AFMediaBar-v1.1.1-win-x64.zip` 并解压，然后运行其中唯一的 `AFMediaBar.exe`。这是自包含版本，不需要预先安装 .NET 8 Desktop Runtime。

请勿将 GitHub 自动生成的 Source code 压缩包作为程序使用。可通过同一 Release 中的 `SHA256SUMS.txt` 校验下载文件。

## 国内下载镜像

- 夸克网盘：[下载地址](https://pan.quark.cn/s/6987e4945b16)
- 百度网盘：[下载地址](https://pan.baidu.com/s/1zUQtZ_N1tnRTjJKd9kKREA?pwd=6ddc)，提取码：`6ddc`
- 蓝奏云：[下载地址](https://amorfate.lanzoue.com/b01eupanbg)，密码：`zzzz`

国内镜像中的压缩包应与 GitHub Release 中的文件完全一致，可使用 SHA-256 校验值进行核对。

## 本次亮点

- 修复悬浮窗口、托盘唤醒和桌面边缘尺寸恢复相关问题。
- 修复浏览器媒体封面刷新、断线期媒体切换和暂停后的自动切源。
- 完善简体中文、繁体中文和英文的即时界面切换。
- 改进设置窗口、诊断日志、字体预设及多项尺寸与外观调节。
- 增加资源监控区域的任务管理器快捷操作。

## 已知限制

- 当前仅发布 `win-x64` 版本，尚未提供 ARM64 构建。
- 发布文件尚未进行商业代码签名，Windows SmartScreen 可能显示“未知发布者”。
- 当前仅跟随主显示器任务栏。
- 设置默认输出设备依赖未公开的 `PolicyConfig` COM 接口，Windows 更新或设备策略可能影响其可用性。

本版本没有已确认的阻断性问题。完整安装、卸载、常见问题和隐私说明见 [README](https://github.com/Fervent-Tempo/AF-Media-Bar#readme)。

---

# AF Media Bar 1.1.1


AF Media Bar 1.1.1 is a maintenance update focused on window recovery, media switching, and artwork-refresh fixes, with refinements to localization, appearance, and settings. The release is self-contained for `win-x64` and does not require a separate .NET runtime. No blocking issues are currently known. See the [English README](https://github.com/Fervent-Tempo/AF-Media-Bar/blob/main/README.en-US.md) for full documentation.
