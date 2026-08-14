# AF Media Bar 1.1.0

这是 AF Media Bar 的正式功能更新，扩展了 Windows 10/11 兼容性、任务栏布局、外观设置和窗口行为，并加入自动更新检查。

## 下载与安装

下载 `AFMediaBar-v1.1.0-win-x64.zip` 并解压，然后运行其中唯一的 `AFMediaBar.exe`。这是自包含版本，不需要预先安装 .NET 8 Desktop Runtime。

请勿将 GitHub 自动生成的 Source code 压缩包作为程序使用。可通过同一 Release 中的 `SHA256SUMS.txt` 校验下载文件。

## 国内下载镜像

- 夸克网盘：[下载地址](https://pan.quark.cn/s/6987e4945b16)
- 百度网盘：[下载地址](https://pan.baidu.com/s/1zUQtZ_N1tnRTjJKd9kKREA?pwd=6ddc)，提取码：`6ddc`
- 蓝奏云：[下载地址](https://amorfate.lanzoue.com/b01eupanbg)，密码：`zzzz`

国内镜像中的压缩包应与 GitHub Release 中的文件完全一致，可使用 SHA-256 校验值进行核对。

## 本次亮点

- 支持 Windows 10，并继续支持 Windows 11。
- 支持系统主题自动跟随和独立主题设置。
- 支持横向与竖向任务栏播放器布局。
- 增加浮动窗口、边缘收起和窗口可见性选项。
- 重构设置窗口、设置菜单和任务栏窗口托管。
- 增加自动更新检查、备用版本清单源和跳过版本功能。
- 修复右键菜单层级问题。
- 改进双语社区和项目文档。

## 已知限制

- 当前仅发布 `win-x64` 版本，尚未提供 ARM64 构建。
- 发布文件尚未进行商业代码签名，Windows SmartScreen 可能显示“未知发布者”。
- 当前仅跟随主显示器任务栏。
- 设置默认输出设备依赖未公开的 `PolicyConfig` COM 接口，Windows 更新或设备策略可能影响其可用性。

本版本没有已确认的阻断性问题。完整安装、卸载、常见问题和隐私说明见 [README](https://github.com/Fervent-Tempo/AF-Media-Bar#readme)。

---

AF Media Bar 1.1.0 is a stable feature release with Windows 10/11 support, automatic system-theme matching, horizontal and vertical taskbar layouts, expanded window behavior, redesigned settings, and update checks with fallback manifest sources. The release is self-contained for `win-x64` and does not require a separate .NET runtime. No blocking issues are currently known. See the [English README](https://github.com/Fervent-Tempo/AF-Media-Bar/blob/main/README.en-US.md) for full documentation.
