# AF Shell · Media Bar

AF Shell 是一个面向 Windows 11 的桌面增强项目，Media Bar 是当前的任务栏媒体控制模块。它读取 Windows 全局系统媒体会话（GSMTC），在任务栏上显示封面、标题和作者，并提供上一首、播放/暂停、下一首和媒体来源切换。

## 为什么不直接嵌入控制中心媒体卡片

Windows 11 控制中心里的媒体卡片不是公开可嵌入的控件，而是 Explorer/Shell 的内部界面。查找内部窗口、`SetParent` 或向 Explorer 注入代码都依赖未公开实现，容易随 Windows 更新失效，也会扩大崩溃和安全风险。

Media Bar 复用控制中心背后的公开接口 `Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager`，再由独立 WPF 浮层渲染任务栏界面。这样可以获得相同的媒体元数据和控制能力，同时不修改 Explorer。

## 功能

- 枚举全部 GSMTC 媒体会话，默认选择 Windows 当前会话，其次选择正在播放的会话
- 适配网易云音乐、QQ 音乐、酷狗音乐、Spotify、Chrome、Edge、Firefox、VLC、PotPlayer、Windows Media Player、mpv 和 foobar2000 等常见来源
- 右键菜单选择媒体来源，或在播放器上滚动鼠标滚轮快速切换
- 显示封面、标题和作者，并按会话能力启用上一首、播放/暂停、下一首
- 点击封面或标题可切回当前媒体应用
- 收起时显示封面、标题和性能指标，悬停时展开控制区与作者
- 性能指标提供总开关，也可分别选择系统内存、CPU、GPU 和 AF Shell 进程内存
- 低配置模式会切换为 WPF 软件渲染，并关闭悬停过渡、文字滚动与指标淡入淡出，同时保留全部控制功能
- 音频监听使用 Windows Core Audio 默认输出峰值计量；收起且鼠标离开时显示低开销圆点/条形可视化
- 标题和作者超过可用宽度时自动往返滚动
- 自动读取任务栏对齐和自动隐藏状态，寻找不会遮挡图标的空白区
- 默认使用手动定位：拖动 Media Bar 到目标位置后勾选“锁定手动位置”；自动避让任务栏图标保留为实验性功能，需要用户主动开启
- 缓存与任务栏宽度、播放器宽度和对齐方式匹配的自动位置，避免启动时先在最左侧闪现再跳动
- 开启自动隐藏时以轻量 16 ms 矩形观察同步任务栏动画，自动与手动位置都适用
- 按 Windows 键打开开始菜单或搜索时，Media Bar 会随自动隐藏任务栏一起出现
- 视频切集导致媒体会话短暂消失时，会保留原来源和媒体信息并等待 6 秒，避免误切到其他播放器
- 右键菜单在点击桌面、任务栏或其他外部区域时立即关闭
- 输出设备按钮可打开活动播放设备列表；点击立即切换，悬停在按钮上滚动可预览设备，并在停止滚动 1 秒后切换
- GPU 指标按 Windows 任务管理器的思路显示当前最繁忙的物理 GPU 引擎
- 原生系统托盘入口、开机启动和全屏自动隐藏

媒体应用必须向 Windows 发布 GSMTC 会话才能被控制。浏览器通常需要网页正在播放媒体；部分桌面播放器需要在设置中启用“系统媒体控制”或“SMTC”。

## 运行和发布

要求 Windows 11 和 .NET 8 Desktop Runtime。

```powershell
dotnet run --project .\TaskbarPlayer.csproj
```

发布为低常驻内存的框架依赖文件夹：

```powershell
dotnet publish .\TaskbarPlayer.csproj -c Release -r win-x64 --self-contained false -o .\dist\AFShell
```

启动 `dist\AFShell\AFShell.exe`。程序会把旧版 `TaskbarPlayer` 的位置、性能指标和开机启动配置迁移到 AF Shell。

## 已知边界

- Windows 11 没有受支持的第三方任务栏工具栏 API，因此 Media Bar 是视觉贴合任务栏的顶层浮层，不是 Explorer 内部插件。
- 只能使用媒体来源通过 GSMTC 声明支持的控制命令；来源不支持的按钮会自动禁用。
- 输出设备切换使用 Windows Core Audio 的 PolicyConfig 接口；这是 Windows 没有公开文档的系统接口，未来系统更新可能限制该能力。
- 当前实例跟随主显示器任务栏，暂不在每个辅助显示器上分别创建控制器。
- 同一浏览器内多个网页是否表现为一个还是多个会话，由浏览器的 GSMTC 实现决定。
