# AF Media Bar 1.7.1 — 托盘交互、窗口材质与媒体兼容性

改善托盘和右键菜单的日常操作，统一窗口材质，修复播放器进度与歌词显示问题。

## 🖱️ 托盘与交互

<details>
<summary>原生托盘图标更清晰</summary>

在「托盘操作」中可开启原生托盘图标。图标按系统深浅色和当前单击动作切换，减少透明留白，在托盘中更容易辨认。此选项默认关闭。

</details>

<details>
<summary>切换子菜单时收起旧菜单</summary>

右键菜单跟随应用字体、文字颜色和窗口材质。悬停媒体来源或快速启动可展开子菜单，切换项目时旧分支会收起，并清除残留高亮。

</details>

<details>
<summary>改善托盘滚轮与完整面板拖动</summary>

修复组合滚轮操作后松键可能触发额外点击的问题。完整面板拖动手柄移到媒体栏外侧，按背景切换深浅色。

</details>

## 🎨 窗口材质

<details>
<summary>用同一浓度设置调节窗口材质</summary>

普通窗口采用统一的材质遮罩。曲目通知与子菜单可呈现不同强度的 Acrylic、Mica 和 Mica Alt 近似效果，并跟随材质浓度设置。

</details>

## 🎵 播放器与歌词

<details>
<summary>识别 Folia 并修复酷狗进度</summary>

增加 Folia 来源识别。恢复酷狗音乐 20.1.61.28014 的播放进度读取，并修复重开播放器后歌曲时长可能失效的问题。没有进度时，提示会等待状态稳定后再出现。

</details>

<details>
<summary>更容易配置和保留本地歌词</summary>

本地歌词设置单独显示，文件夹入口更易找到。在线结果匹配明显更弱时，保留已有的本地歌词结果。

</details>

## 🔧 稳定性与体验

<details>
<summary>减少隐藏歌词视图的占用</summary>

竖向媒体栏使用 WPF 绘制曲目信息。歌词视图在持续隐藏时挂起，并在较深的内存剪枝阶段释放，需要显示时再恢复。设置保存失败时会清理临时文件。

</details>

## 下载与安装

系统要求与安装方式见 [README](https://github.com/Fervent-Tempo/AF-Media-Bar#readme)。安装包和便携版均自带 .NET 运行时。

- `AFMediaBar-Setup-v1.7.1-win-x64.exe`：安装版。
- `AFMediaBar-v1.7.1-win-x64.zip`：便携版，解压即用。

请使用同一 Release 的 `SHA256SUMS.txt` 核对下载文件。

## 贡献者

感谢 [Gorden-86](https://github.com/Gorden-86) 和 [Lyrissonare](https://github.com/Lyrissonare) 的贡献，以及反馈问题和支持项目的用户。
