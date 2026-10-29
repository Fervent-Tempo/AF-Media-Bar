# 插件下载

“设置 → 播放与设备 → 插件下载”提供播放器 SMTC 扩展的固定版本下载和手动安装指引，也可从“媒体来源”进入。可按插件、播放器或作者名称搜索。AF 本体的插件加载接口尚未提供。

## 下载与安装

1. 阅读卡片中的兼容条件。多架构附件默认选中 x64，安装前仍须核对**播放器**架构；需要时选择 x86。目录不提供 ARM64，不按 Windows 或 AF 的架构推断播放器架构。
2. 点击下载。AF 优先尝试镜像，再尝试 GitHub 官方附件，按内置目录中的文件大小和 SHA-256 校验。
3. 完成后选择“在文件夹中显示”，展开安装指引，由你手动安装并重启播放器。
4. 播放歌曲，检查 Windows 媒体面板与 AF 中的曲目信息、播放进度和控制能力。

文件保存在 `%LOCALAPPDATA%\AFMediaBar\PluginDownloads\<插件ID>\<版本>\`。全局同时下载一个附件，离开页面或关闭设置后继续，退出 AF 时取消。可取消或重试；再次下载相同附件时会重新校验现有文件，校验通过则复用。

“已下载并校验”不代表已经安装，也不代表已通过播放器兼容性验收。AF 不自动解压、运行安装程序、修改播放器文件、申请管理员权限或设置桥接工具自启。安装前保留所需备份，卸载时按指引还原。

## 内置目录

先查看快速开始页面中的播放器支持说明。已有内置接入或 AF 补全足够使用时，不需要再安装插件。资料不足的候选不提供下载。

| 播放器 | 上游项目及固定版本 | 前置条件与限制 |
|---|---|---|
| foobar2000 | [foo_mediacontrol v1.4](https://github.com/ungive/foo_mediacontrol/releases/tag/v1.4) | x86、x64；补全专辑与时间轴 |
| MusicBee | [mb_MediaControl 1.0](https://github.com/HenryPDT/mb_MediaControl/releases/tag/1.0) | AnyCPU，.NET Framework 4.8；避免多个 SMTC 插件同时启用 |
| VLC | [vlc-win10smtc v5.0.0](https://github.com/spmn/vlc-win10smtc/releases/tag/v5.0.0) | 匹配 VLC 架构及 VC++ 2022 或更新运行库；上游声明修复进度上报 |
| iTunes | [iTunes-SMTC v1.3_release](https://github.com/thewizrd/iTunes-SMTC/releases/tag/v1.3_release) | 独立桥接工具；不要当作 DLL 插件安装 |
| QQ 音乐 | [qqmusic-smtc-seek v0.1.0](https://github.com/zhuzh3378/qqmusic-smtc-seek/releases/tag/v0.1.0) | 实验性，x86；主要验证 22.71，使用进程内代理 DLL |

能力和兼容条件依据上游资料，不能推断所有播放器版本均适用。未核对的能力以“不保证”处理；上游插件的更新和支持由各项目负责。

## 目录维护

目录定义在 `PluginCatalog`，附件固定到具体 release，不在运行时请求最新版本。维护者更新条目时应核对该版本的附件内容、大小、SHA-256、实际架构、前置条件与安装还原步骤；缺少上游摘要时，从官方附件计算摘要后再登记。不要将来自同一镜像的摘要作为可信依据。

插件说明与界面文字同步简体、繁体、英文及越南语资源。新增目录项不等于新增 AF 媒体适配器，不改变既有来源身份，也不授予自动执行安装的权限。

插件下载采用镜像优先、GitHub 官方兜底。镜像可达性会变化，自动回退不能保证所有网络始终可用。
