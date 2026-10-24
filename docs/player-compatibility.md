# 播放器兼容性参考

在“设置 → 快速开始 → 播放器支持”查看各播放器的能力与安装建议。版本亮点保留独立页面，可从快速开始、应用更新区域或设置搜索进入，使用页头“返回”回到上一页。

本文资料核对至 2026-10-24。源码包含某项实现不等于发行安装包已通过 Windows 验收；上游插件能补功能，也不代表当前所有播放器版本都需要安装。以下播放器均待维护者实机验收。

## 判断是否需要插件

先使用播放器内置媒体控制和 AF 已有适配，再检查仍缺少的功能。歌曲名称与封面、时长与进度、播放暂停与切歌、进度跳转分别验证，不能只凭系统中出现媒体卡片判定完整支持。随机、循环和服务歌曲 ID 单独检查，不作为全部播放器必须提供的基础信息。

AF 对网易云有独立的信息和进度读取；控制能力仍取决于同来源 SMTC。AF 对酷狗补充进度，元数据、封面及控制仍来自 SMTC。因而 AF 显示正常不能证明播放器原生 SMTC 完整，原生存在缺项也不一定需要安装插件。

## 国内客户端

| 播放器 | 已有依据及限制 | 插件建议 |
|---|---|---|
| 网易云音乐 | 3.1.41 原生能力待实测；[InfLink-rs](https://github.com/apoint123/inflink-rs) 声明提供信息、进度、跳转与循环控制，测试范围为 3.1.20–3.1.41、2.10.13，依赖 BetterNCM | 暂不展示下载；先确认 AF 的实际缺项 |
| QQ 音乐 | [qqmusic-smtc-seek](https://github.com/zhuzh3378/qqmusic-smtc-seek) 作者在 22.71 验证原生有信息和进度但缺少跳转；不能据此推断 22.61 或所有新版本 | 需要跳转时可评估 v0.1.0；仅 x86，实验性 DLL 注入，需核对兼容与还原步骤 |
| 酷狗音乐 | AF 已有进度补全，20.x 原生能力仍需按版本验收；[旧插件](https://github.com/AshenAshes/kugou8-smtc-plugin) 仅适配 8.3.97.21592，且无跳转 | 不向当前客户端提供旧插件 |
| 汽水音乐 | 尚无充分的版本化证据证明信息、时间轴及跳转均完整 | 待验证，不推荐插件 |
| 酷我音乐 | 未找到足够的版本化 SMTC 资料和适合直接收录的扩展；不能由此认定不支持 | 待验证，不推荐插件 |
| 咪咕音乐 | 网页与桌面版本须分别检查；网页媒体会话同时取决于网站和浏览器 | 待验证，不推荐插件 |
| 千千音乐 | 当前 Windows 客户端完整 SMTC 能力待验证 | 不推荐插件 |
| 波点音乐 | [BodianSMTCPlugin](https://github.com/HChenX/BodianSMTCPlugin) 提供信息、封面、进度与控制，但当前客户端是否仍需补充尚未确认 | 暂不展示需替换 DLL 的插件 |

## 国内开源播放器

下列核对的是指定发行版本中的实现，不将开发分支代码推断为所有发行版能力。Electron/浏览器的 Media Session 最终如何映射到 Windows SMTC，仍须测试对应安装包。

| 播放器及版本 | 源码依据 | 建议 |
|---|---|---|
| 洛雪音乐桌面版 v2.12.6 | [useMediaSessionInfo.ts](https://github.com/lyswhut/lx-music-desktop/blob/v2.12.6/src/renderer/core/useApp/usePlayer/useMediaSessionInfo.ts)：元数据、位置上报、播放暂停、切歌与 seekto | 使用内置接入，不提供额外 SMTC 插件 |
| YesPlayMusic v0.4.11 | [Player.js](https://github.com/qier222/YesPlayMusic/blob/v0.4.11/src/utils/Player.js)：元数据、位置上报与播放、切歌、跳转处理 | 使用内置接入 |
| MoeKoeMusic v1.7.1 | [MediaSession.js](https://github.com/MoeKoeMusic/MoeKoeMusic/blob/v1.7.1/src/components/player/MediaSession.js)：元数据、位置状态和跳转处理 | 使用内置接入 |
| Listen 1 桌面版 v2.33.0 | 桌面版本引用的 [player_thread.js](https://github.com/listen1/listen1_chrome_extension/blob/3f24efa045125875a609dcb0f3f3f0be4edb8b37/js/player_thread.js)：元数据、位置状态与跳转 | 使用内置接入；桌面、网页及浏览器扩展分别验收 |
| SPlayer v3.1.1 | [MediaSessionManager.ts](https://github.com/SPlayer-Dev/SPlayer/blob/v3.1.1/src/core/player/MediaSessionManager.ts)：桌面原生媒体控制与网页 Media Session 回退、位置和跳转 | 开启播放器 SMTC 选项，无需再装独立桥接 |
| AlgerMusicPlayer v5.1.0 | [audioService.ts](https://github.com/algerkong/AlgerMusicPlayer/blob/v5.1.0/src/renderer/services/audioService.ts)：元数据、位置上报、播放、切歌与跳转 | 使用内置接入 |
| MusicFree Desktop v0.0.8 | [audio-controller.ts](https://github.com/maotoumao/MusicFreeDesktop/blob/v0.0.8/src/renderer/core/track-player/controller/audio-controller.ts) 包含元数据及播放状态，播放器入口包含切歌；尚未确认时间轴和跳转完整性 | 待验证，不把音源插件当作 SMTC 扩展推荐 |

## 其他常用播放器

- **foobar2000**：其 SMTC 消费端的[使用指引](https://docs.musicpresence.app/setup/media-player/)指出内置模式缺少专辑和时长。需要这些信息时可使用 `foo_mediacontrol v1.4`，仍需核对当前播放器版本。
- **MusicBee**：`mb_MediaControl 1.0` 作者[说明](https://github.com/HenryPDT/mb_MediaControl)包含时间轴与跳转；缺少媒体会话或现有组件不足时再安装，避免多个同类组件同时启用。
- **VLC**：提供 [vlc-win10smtc v5.0.0](https://github.com/spmn/vlc-win10smtc) 作为 VLC 3.0.x 接入方案；4.0 开发版按具体构建核对。附件及 VC++ 运行库须匹配 VLC 架构。
- **iTunes**：[iTunes-SMTC](https://github.com/thewizrd/iTunes-SMTC) 是独立桥接工具，不将其视为现代 Apple Music 的必需项。
- **mpv**：[v0.41.0 源码](https://github.com/mpv-player/mpv/blob/v0.41.0/osdep/win32/smtc.cpp)有元数据、时间轴及跳转实现。需要时在 `mpv.conf` 设置 `media-controls=yes`；libmpv 与第三方前端分别确认。
- **PotPlayer**：[桥接项目](https://github.com/Barrryy/potplayer-smtc-bridge)主要补元数据，作者说明原有时间轴和控制已提供。26.08.19 未实测，暂不推荐注入工具。
- **AIMP**：[旧桥接作者](https://github.com/NimiGames68/aimp-smtc)因原生支持停止维护，但稳定版和测试版覆盖范围仍需核实，不笼统推荐旧桥接。
- **MPC-HC**：使用维护中的 [clsid2 分支](https://github.com/clsid2/mpc-hc)，检查 Windows 集成的 SMTC 选项；完整能力待验收。
- **Spotify、Apple Music、TIDAL**：本次没有足够的当前版本实测依据，不声明完整支持，也不提供未经验证的修复插件。

## 本机确认方法

先停用已有 SMTC 桥接，记录播放器版本、架构、相关开关及 Windows 版本。逐款播放两首有完整标签的歌曲，检查切歌后的名称、艺术家、专辑与封面是否更新；持续播放并暂停，核对总时长、位置及暂停后是否停止推进。

从 AF 实际执行播放暂停、上一首、下一首及跳转，并核对播放器确实执行，不能只检查能力标志或 API 返回值。原始 SMTC 与 AF 经过适配后的表现分开记录。确有缺项时，再按上游兼容范围验证插件安装、重启、升级和还原。
