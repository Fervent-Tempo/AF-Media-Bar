# 网易云任务栏播放器

一个贴在 Windows 11 主任务栏左侧的网易云音乐迷你控制器。它读取 Windows 全局系统媒体会话（GSMTC），显示歌曲名、歌手和封面，并提供上一首、播放/暂停、下一首操作。

## 为什么不直接使用控制中心

Windows 11 控制中心的媒体卡片不是可嵌入的公共控件。它本身是 Explorer/Shell 的界面，强行查找内部窗口、`SetParent` 或注入 Explorer 都依赖未公开实现，Windows 更新后很容易失效。

本项目复用控制中心背后的公开数据源：`Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager`。任务栏部分则使用独立、无边框、置顶且不出现在 Alt+Tab 的 WPF 窗口覆盖在任务栏左侧。这样既能获得相同的媒体信息和控制能力，又不修改 Explorer 进程。

## 运行

要求 Windows 11 和 .NET 8 Desktop Runtime。

```powershell
dotnet run --project .\TaskbarPlayer.csproj
```

程序启动后会自动寻找来源标识包含 `cloudmusic`、`netease` 或 `163music` 的系统媒体会话。右键播放器可重新连接、设置开机启动或退出；点击封面/歌曲信息会切回网易云音乐。

进入全屏游戏或演示模式时，播放器会与任务栏一样自动隐藏；退出全屏后会自动恢复。

如果一直显示“等待网易云音乐”，先在网易云音乐中实际播放一首歌曲。部分网易云版本还需要在设置中启用“系统媒体控制/显示系统媒体信息”一类选项。

## 构建发布版

```powershell
dotnet publish .\TaskbarPlayer.csproj -c Release -r win-x64 --self-contained false -o .\dist
```

输出为 `dist\TaskbarPlayer.exe`。

## 已知边界

- Windows 11 没有受支持的第三方任务栏工具栏 API，所以这里是视觉贴合任务栏的浮层，不是 Explorer 内部插件。
- 默认占用任务栏最左侧约 348 DIP；若左侧已有 Widgets 等按钮，会被播放器覆盖。
- 只有网易云音乐向 GSMTC 发布的能力才能使用。例如某个客户端版本不允许“下一首”，对应按钮会自动禁用。
- 当前实现跟随主显示器任务栏；辅助显示器任务栏暂未放置第二个实例。
