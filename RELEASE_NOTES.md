# AF Media Bar 1.4.0 — 设置与任务栏体验更新

这次更新重新整理设置页，增加任务栏背景、封面、歌词和性能组件选项，并改善任务栏定位、媒体恢复与退出。

## ⚙️ 设置更易用

- 设置页类别重构；滑杆支持精确数值输入。
- 新增越南语界面与安装向导。

## ✨ 任务栏与封面

- 新增磨砂背景选项；小音符支持自选颜色。
- 支持左右内容排布、封面悬停放大和大图预览；新增封面边缘播放进度。
- 优化任务栏图标避让算法、空间缩窄提醒与恢复、长标题循环滚动。

## 🎵 歌词、频谱与性能

- 歌词可在简体与繁体间转换；歌词显示异常时提供恢复或重启提示。
- 频谱可选择采集当前输出设备、所有应用、当前媒体应用。
- 性能组件增加网络上传和下载速率，支持轮换、并排显示和指标排序。

## 🔧 稳定性

- 改善媒体来源与播放进度更新，修复音频浮窗、窗口材质切换及设置导航问题。
- 改进退出、后台资源停止和重启交接，减少退出卡住桌面或残留进程的风险。

## 下载与安装

系统要求与安装方式见 [README](https://github.com/Fervent-Tempo/AF-Media-Bar#readme)。安装包和便携版均自带 .NET 运行时。

- `AFMediaBar-Setup-v1.4.0-win-x64.exe`：安装版。
- `AFMediaBar-v1.4.0-win-x64.zip`：便携版，解压即用。

请使用同一 Release 的 `SHA256SUMS.txt` 核对下载文件，不要使用 GitHub 自动生成的 Source code 压缩包。

## 已知限制

- 目前仅发布 `win-x64`，支持横向任务栏；竖向任务栏、悬浮模式与按显示器独立配置尚未实现。
- 便携版不自动安装更新。隐私说明与卸载方式见 [README](https://github.com/Fervent-Tempo/AF-Media-Bar#readme)。
- 当前发布包尚未签名，Windows 可能显示未知发布者提示。

## 贡献者

感谢 [@Gorden-86](https://github.com/Gorden-86)、[@Heartestrella](https://github.com/Heartestrella) 和 [@furinamikuvn](https://github.com/furinamikuvn) 为本次改进做出贡献。

## 首次贡献

- [@Heartestrella](https://github.com/Heartestrella) 在 [#139](https://github.com/Fervent-Tempo/AF-Media-Bar/pull/139) 中首次贡献，增加自定义任务栏背景。
- [@furinamikuvn](https://github.com/furinamikuvn) 在 [#177](https://github.com/Fervent-Tempo/AF-Media-Bar/pull/177) 中首次贡献，增加越南语支持。
