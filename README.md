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

AF Media Bar 是一款 Windows 10/11 任务栏媒体控制器。它从系统媒体会话读取正在播放的内容，让封面、歌词、播放控制和音频设备切换留在桌面边缘。

## 运行演示

<p align="center">
  <img src="docs/assets/readme/展示.gif" width="100%" alt="AF Media Bar 实际运行演示">
</p>

[观看 Bilibili 介绍视频](https://www.bilibili.com/video/BV17yhh6aEgK)

## 下载安装

前往 [GitHub Releases](https://github.com/Fervent-Tempo/AF-Media-Bar/releases)，选择一种方式：

1. **安装程序（推荐）：** 下载 `AFMediaBar-Setup-vX.Y.Z-win-x64.exe`，按向导安装。
2. **便携版：** 下载 `AFMediaBar-vX.Y.Z-win-x64.zip`，解压到可写目录，运行 `AFMediaBar.exe`。

**系统要求：** Windows 10 1809 或更新的 x64 系统，需要 Microsoft Edge WebView2 Runtime；缺失时先安装 Evergreen Runtime。发布包自带 .NET 运行时。**.NET 10 对 Windows 10 的官方支持仅限长期服务版与企业版**，消费版不在支持范围内；Windows 11 不受此限制。

请下载发布包，不要使用 Source code 压缩包。程序尚未进行商业代码签名，首次运行时 Windows SmartScreen 可能提示“未知发布者”。

## 功能一览

| 功能 | 描述 |
| --- | --- |
| 播放与来源 | 查看封面与曲目信息，切换媒体来源；支持上一首、播放/暂停、下一首、循环及进度跳转。 |
| 实时歌词 | 自动获取歌词，支持逐字高亮、译文、音译与双行显示，让歌词随播放进度同步呈现。 |
| 音频控制 | 快速切换默认输出设备，调整当前媒体应用的音量，查看空间音效状态。 |
| 频谱与指标 | 提供四种音频频谱样式，可在任务栏显示 CPU、GPU 和内存等性能指标。 |
| 布局与外观 | 支持靠左、居中、靠右对齐，内容可选左侧／右侧排布，悬停按钮可独立对齐；自动避让任务栏图标及系统区域，选择显示屏、无播放时自动隐藏；可调整字体、主题色与窗口材质。 |
| 快捷操作 | 悬停显示控制按钮，打开完整媒体弹窗查看更多信息；支持自定义点击操作、快速启动常用播放器与托盘菜单。 |

## 基本使用

- 启动播放器并播放歌曲，媒体栏会显示当前曲目。部分播放器需先启用“系统媒体控制”或“媒体键”。
- 悬停媒体栏显示控制按钮；点击或拖动进度条可跳转播放位置。
- 通过托盘右键菜单打开设置、快速启动列表或退出程序。

**兼容说明：** 大多数播放器依赖 Windows 系统媒体会话。网易云音乐可独立识别曲目信息，但封面和播放控制仍需其系统媒体控制支持。当前仅支持任务栏模式，其他显示模式尚未开放。

## 更新与卸载

- **更新：** 发现新版本时会提示，可在「应用」页检查更新。安装版支持程序内下载与安装；便携版需手动替换程序文件。
- **设置：** 保存在 `%LOCALAPPDATA%\AFMediaBar\settings.json`，可从「应用」页打开设置文件夹。
- **卸载：** 安装版通过 Windows“已安装的应用”或开始菜单卸载；便携版直接删除程序目录。若要清除设置与缓存，再手动删除 `%LOCALAPPDATA%\AFMediaBar`。

## 隐私与安全

- 不含遥测、广告或账号系统；媒体控制与音量操作在本机处理。
- 更新检查与在线歌词检索会联网；歌词检索使用曲名、歌手等信息，可在设置中关闭歌词来源。
- 程序以当前用户权限运行。安全问题请按 [SECURITY.md](SECURITY.md) 私下报告。

## 从源码构建

需要 Windows 10 1809 或更高版本、[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 和 PowerShell；仓库通过 `global.json` 固定受支持的 SDK 特性带。

```powershell
git clone https://github.com/Fervent-Tempo/AF-Media-Bar.git
cd AF-Media-Bar
dotnet restore .\src\AFMediaBar.slnx -r win-x64
dotnet build .\src\AFMediaBar.slnx -c Release --no-restore -p:ContinuousIntegrationBuild=true -p:BuildInParallel=false
dotnet test .\tests\AFMediaBar.Layout.Tests\AFMediaBar.Layout.Tests.csproj -c Release --no-build --no-restore
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

参与贡献前请阅读 [贡献指南](CONTRIBUTING.md)，架构边界见 [架构文档](docs/architecture.md)。

报告问题时请附 Windows 版本、程序版本、播放器与复现步骤。版本记录见 [CHANGELOG.md](CHANGELOG.md)。

## 致谢

感谢所有参与贡献的开发者。

感谢以下开源项目：

- [FluentFlyout](https://github.com/unchihugo/FluentFlyout)：简单、现代的 Windows 音量控制弹窗软件。
- [Lyricify-Lyrics-Helper](https://github.com/WXRIW/Lyricify-Lyrics-Helper)：Lyricify 歌词库，提供歌词解析、生成、搜索、解密和优化处理。
- [TaskbarLyrics](https://github.com/ANYNC/TaskbarLyrics)：Windows 任务栏歌词工具。


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

</div>
