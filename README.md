<p align="center">
  <img src="docs/assets/readme/hero-zh.svg" width="100%" alt="AF Media Bar 布局示意：媒体栏位于 Windows 任务栏左下角，完整媒体弹窗在其正上方，软件图标与项目介绍位于右侧">
</p>

<p align="center">
  <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/releases"><img src="https://img.shields.io/github/v/release/Fervent-Tempo/AF-Media-Bar?style=flat-square" alt="最新版本"></a>
  <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/releases"><img src="https://img.shields.io/github/downloads/Fervent-Tempo/AF-Media-Bar/total?style=flat-square" alt="下载次数"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/Fervent-Tempo/AF-Media-Bar?style=flat-square" alt="MIT 许可证"></a>
  <br>
  简体中文 · <a href="README.en-US.md">English</a>
  <br>
  <a href="#下载安装">下载运行</a> · <a href="#功能一览">功能一览</a> · <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/issues/new?template=bug_report.yml">报告问题</a> · <a href="https://github.com/Fervent-Tempo/AF-Media-Bar/issues/new?template=feature_request.yml">功能建议</a>
</p>

AF Media Bar 是一款便携式 Windows 10/11 任务栏媒体控制器。它从系统媒体会话读取正在播放的内容，让封面、歌词、播放控制和音频设备切换留在桌面边缘。

## 运行演示

<p align="center">
  <img src="docs/assets/readme/展示.gif" width="100%" alt="AF Media Bar 实际运行演示">
</p>

[观看 Bilibili 介绍视频](https://www.bilibili.com/video/BV17yhh6aEgK)

## 下载安装

前往 [GitHub Releases](https://github.com/Fervent-Tempo/AF-Media-Bar/releases)，选择一种方式：

1. **安装程序（推荐）：** 下载 `AFMediaBar-Setup-vX.Y.Z-win-x64.exe`，运行向导并选择语言、安装位置及当前用户/所有用户。默认安装位置为 `%LOCALAPPDATA%\Programs\AFMediaBar`；安装版支持程序内检查、下载与安装更新。
2. **便携版：** 下载 `AFMediaBar-vX.Y.Z-win-x64.zip`，解压到长期保留且可写的目录（如 `D:\AFMediaBar`），运行其中的 `AFMediaBar.exe`。便携版不写注册表，更新时手动替换文件。

**系统要求：** Windows 10 1809（内部版本 17763）或更新的 x64 系统。两种包都自带 .NET 运行时，不需要另行安装。程序用到的系统接口可在 1809 使用，但 **.NET 10 官方仅支持 Windows 10 的长期服务版与企业版**（1809 E、21H2 E）；消费版 Windows 10 不在 Microsoft 的支持范围内。Windows 11 不受此限制。

请下载上述发布包，不要使用 GitHub 自动生成的 Source code 压缩包。程序尚未进行商业代码签名，首次运行时 Windows SmartScreen 可能提示“未知发布者”。

安装程序可选桌面快捷方式，并会创建开始菜单项。国内访问 GitHub 不稳定时，可在 Releases 页面使用 GH-Proxy 加速地址；程序内更新也会在直连失败时尝试加速地址。

## 功能一览

| 场景 | 可以做什么 |
| --- | --- |
| 音乐控制 | 上一首、播放/暂停、下一首、循环；点击或拖动进度条跳转。滚轮切歌每次只切一首，连续滚动需停顿约半秒才能再次切歌 |
| 任务栏歌词 | 显示逐字擦亮的实时歌词，支持译文、音译与双行对齐；按网易云音乐、LRCLIB、QQ 音乐、酷狗音乐、汽水音乐的顺序匹配 |
| 来源与交互 | 切换媒体会话；封面、标题与歌词的点击操作可分别设为播放/暂停、切回媒体应用或打开完整菜单 |
| 音频与系统 | 点击或滚轮切换默认输出设备、调整当前媒体应用音量、查看空间音效；提供四种频谱样式与性能检测组件 |
| 布局与外观 | 自动避让任务栏图标及系统区域，可选目标屏幕、无播放时自动隐藏；可调字体、主题色与窗口材质 |
| 快捷操作 | 悬停显示控制按钮，完整层展示更多信息；无媒体时从音符图标打开快速启动列表；切歌并开始播放时显示通知 |
| 显示模式 | 任务栏与灵动岛可切换并保存；灵动岛为顶部居中的黑色胶囊，长按展开媒体控制，支持独立显示器和等比缩放 |

**使用边界：** 只有向 Windows 发布 GSMTC 媒体会话的播放器才会出现；部分播放器需要在自身设置中启用“系统媒体控制”或“媒体键”。运行模式可在任务栏与灵动岛之间切换并持久化；桌面卡片与悬浮球仍为禁用的未实现选项。

### 灵动岛交互

- 没有媒体时保留静态黑色胶囊；连接媒体后显示小封面和音频活动指示。暂停保留当前媒体，不滑出屏幕，也不因鼠标经过而展开。
- 快速轻点紧凑态可回到媒体应用。按住时岛体从当前形态逐渐展开，约 400 ms 后确认展开；中途取消长按会平滑回弹，不误触打开应用。封面、文字、进度条和按钮分阶段进入，完整展开后松手保持打开。点击外部或按 Esc 收起；进度条支持从轨道任意位置按下并连续拖动。
- 展开与收起由同一岛体连续形变，封面随形状移动和缩放；快速反向操作不会重置动画。切歌只更新当前形态，不强制弹出大面板或第二个切歌通知。
- 可独立选择显示器并在 75%–150% 间等比缩放。岛体固定顶部居中，不提供自由拖动、纵向布局或四边贴靠；旧外观与拖动位置字段仅保留配置文件兼容性。
- 所在显示器出现外部全屏窗口时暂时隐藏，退出全屏后恢复；窗口不抢焦点，透明宿主区域不阻挡桌面点击，并遵循系统减少动画及高对比度设置。
- 活动指示复用现有输出设备的真实音频采样，并非单个媒体应用的独立采集；暂停或没有可用采样时静止。灵动岛默认不显示歌词，任务栏歌词功能保持不变。
- 曲目信息可见不代表播放器提供了控制接口。网易云仅通过内存读取提供信息、但没有发布 Windows 媒体会话时，播放/切歌按钮保持禁用并显示原因；请在播放器中开始播放或开启系统媒体控制集成（若该版本支持）。程序不会广播全局媒体键去控制不确定的目标。

## 工作方式

AF Media Bar 以独立 WPF 进程运行，可将媒体栏挂载为任务栏子窗口或显示为灵动岛浮层。它使用 Windows 的公开 GSMTC 接口读取媒体会话，通过 Core Audio 处理设备、音量与回环采样，不修改或向 `explorer.exe` 注入代码。

```mermaid
flowchart LR
    A[媒体应用] -->|GSMTC 会话| B[AF Media Bar]
    C[Windows Core Audio] -->|设备、音量、回环采样| B
    D[Windows 10/11 任务栏与显示器] -->|位置、DPI 与全屏状态| B
    B --> E[WPF 任务栏子窗口或灵动岛浮层]
```

网易云音乐、QQ 音乐、Spotify、浏览器等应用只要发布系统媒体会话，就可以被发现和控制。Windows 控制中心的媒体卡片不是公开可嵌入的控件；本项目读取其背后的公开接口并自行渲染任务栏界面。

## 更新与卸载

### 更新

程序启动约 20 秒后读取公开版本清单（`docs/latest.json`）。发现新版本时：

- 托盘图标弹出一次系统通知，点击直接打开「应用」页；托盘与媒体栏右键菜单里也会出现随状态变化的更新入口。
- 该页显示版本亮点、下载进度与全部更新操作。
- 下载先直连 GitHub，提供「GitHub」与「加速镜像」两个下载页入口。便携版没有安装记录，只下载不安装。

安装日志在 `%LOCALAPPDATA%\AFMediaBar\updates\install-<版本>.log`；已下载的安装包放在同一目录，并在下次启动时按版本清理。

用户偏好与窗口状态保存在 `%LOCALAPPDATA%\AFMediaBar\settings.json`，同一版本内替换程序文件不会丢失设置；「应用」页可打开设置文件夹。

### 卸载

- 便携版：直接删除程序目录。
- 安装版：在“设置 > 应用 > 已安装的应用”中卸载，或使用开始菜单的卸载项；卸载只删除程序目录与快捷方式，需要使用下面命令或手动删除 `%LOCALAPPDATA%\AFMediaBar`。

```powershell
Remove-Item "$env:LOCALAPPDATA\AFMediaBar" -Recurse -Force
```

## 隐私与安全

- 不包含遥测、广告、账号系统或联网分析代码；媒体信息、系统指标与音量操作全部在本机处理。
- 更新检查只请求两个公开清单端点（`raw.githubusercontent.com` 与 jsDelivr 上的 `docs/latest.json`），不经第三方代理；安装包可能经清单里配置的加速地址下载，而它的 SHA-256 始终来自非代理端点获取的清单。
- 歌词按当前媒体信息向上述五个来源的公开接口请求（每个来源只发一次请求，命中即停止）；这些请求只发送曲名、歌手、专辑与时长，不上传设备信息或用户设置。
- 程序以当前用户权限运行，不请求管理员权限，也不向 Explorer 注入代码。安全问题请按 [SECURITY.md](SECURITY.md) 私下报告。

## 从源码构建

需要 Windows 10 1809 或更高版本、[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 和 PowerShell；仓库通过 `global.json` 固定受支持的 SDK 特性带。

```powershell
git clone https://github.com/Fervent-Tempo/AF-Media-Bar.git
cd AF-Media-Bar
dotnet restore .\src\AFMediaBar.slnx
dotnet build .\src\AFMediaBar.slnx -c Release --no-restore
dotnet test .\src\AFMediaBar.slnx -c Release --no-build
dotnet run --project .\src\AFMediaBar\AFMediaBar.csproj
```

生成供普通用户使用的自包含单文件：

```powershell
dotnet publish .\src\AFMediaBar\AFMediaBar.csproj -c Release -r win-x64 --self-contained true -o .\artifacts\AFMediaBar-win-x64
```

## 项目结构

```text
AF-Media-Bar/
├── .github/workflows/        # 构建与发布工作流
├── src/AFMediaBar/           # WPF 应用主项目
│   ├── Classes/              # 分层业务代码
│   │   ├── Abstractions/     # 跨模块契约
│   │   ├── Interop/          # Windows API 互操作
│   │   ├── Models/           # 数据模型（含布局 schema）
│   │   ├── Services/         # 按所有权分目录的服务（Media、Lyrics、Audio、Updates…）
│   │   ├── Settings/         # 设置模型与兼容门面
│   │   └── Utils/            # 无状态辅助与有界缓存
│   ├── Components/           # 可复用 WPF 控件
│   ├── Resources/            # 主题、样式与三语文案
│   ├── ViewModels/           # MVVM 视图模型
│   └── Views/                # 页面与宿主窗口
├── tests/AFMediaBar.Layout.Tests/   # 纯逻辑、策略与设置测试
├── tools/                    # 架构静态检查等脚本
├── installer/                # Inno Setup 安装脚本
└── docs/                     # 项目文档、资源与版本清单
```

## 参与贡献

提交问题或代码前请阅读 [CONTRIBUTING.md](CONTRIBUTING.md)。错误报告请附 Windows 版本、AF Media Bar 版本、媒体播放器与完整复现步骤。版本变化记录见 [CHANGELOG.md](CHANGELOG.md)。

## License

AF Media Bar 使用 [MIT License](LICENSE) 开源。

<div align="center">

如果 AF Media Bar 对你有帮助，可以给项目一个 Star❤️。

</div>

## 赞助

请作者喝杯咖啡。**大于 10 元的赞助可以进入赞助者名单，请在备注中留下 id。**

<div align="center">

| 微信 | 支付宝 |
| :---: | :---: |
| <img src="src/AFMediaBar/Assets/Sponsor/wechat-pay.png" alt="微信收款码" width="220"> | <img src="src/AFMediaBar/Assets/Sponsor/alipay-pay.png" alt="支付宝收款码" width="220"> |

爱发电赞助链接：[爱发电](https://ifdian.net/a/amorfate)

</div>
