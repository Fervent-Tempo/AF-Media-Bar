# 设置契约清单

涉及设置字段、序列化枚举、默认语义、旧值处理、重置或发布时读取本文的相关章节。页面布局重构以实际可达页面为准；控件位置不决定 JSON 字段归属。

本文记录当前实现及正式发布证据，不把现存兼容处理自动变成永久保留要求。修改已发布契约时需要选择并验证处理方案，允许有意不兼容；未发布契约不为开发中间构建保留迁移。

## 基线与使用规则

| 项目 | 核对结果 |
|---|---|
| 当前 main 功能基线 | `6ba5cf2b763ea6b0faa95f3920fbeeddcbfd59d3` |
| 最新正式发布 | `v1.3.2`，2026-10-06；tag 提交 `a781620ea37df526ab87e62d2f7c51fea289f863` |
| 当前设置文件编号 | `schemaVersion=2`；v1.2.0 至 v1.3.2 同为 2 |
| 核对日期 | 2026-10-10 |
| 机器清单 | [settings-contracts.json](../settings-contracts.json)：字段、默认值、缺失/null 行为、枚举、范围及源码指纹 |

首次发布列表示路径或枚举名称/数值组合首次进入**当前 AppSettings/envelope 契约**的正式 tag，不是功能最早出现的时间。当前结构从 v1.2.0 开始；v1.1.1 及以前使用旧模型/配置结构，不把同名旧属性冒充当前路径。旧结构没有自动迁移，见存储契约。

- 判断发布状态以正式 release 对应的 tag 为准，不以 main、PR、程序集版本号或开发构建是否已分发为准。仅源码版本仍写 1.3.2，不意味着新增设置已进入 v1.3.2 发布产物。
- 未发布字段、成员和默认行为可以直接调整，无需兼容开发阶段的旧值；任务授权、线程与资源边界仍按 AGENTS.md 执行。
- 已发布字段的路径、类型、枚举名称/编号、缺省及 null/空集合含义都属于契约。变更时明确选择保留、映射迁移、归一化、隔离重置或停止支持，说明用户影响并验证；不要求一律保留旧值。
- 字段已发布不等于所有成员已发布，例如 `interfaceLanguage` 已发布，`Vietnamese=4` 尚未发布。既有“只能追加”的序列化注释应结合已发布成员理解。
- 清单缺项或证据不明时，先核对正式 tag，不能直接按“未发布”处理。清单与源码不一致时先定位变化，不照旧表改代码。
- #179 将未发布的封面模式移至 `interaction.artworkHoverMode`，交互页提供三个选项并负责重置；开发旧路径不迁移，缺字段回 Off。

## 当前 main 相对正式版的变化

七个未发布字段：

| JSON 路径 | 当前含义 |
|---|---|
| `settings.appearance.taskbarBackgroundMaterial` | 任务栏透明/磨砂背景 |
| `settings.appearance.taskbarBackgroundOpacityPercent` | 磨砂浓度 |
| `settings.appearance.taskbarFrostedStyle` | 磨砂风格 |
| `settings.lyricsChineseConversion` | 歌词简繁转换方向 |
| `settings.taskbarExperience.arrangement` | 静置内容左右排布；null 时 position=End 取 Right，其余取 Left |
| `settings.interaction.artworkHoverMode` | 封面悬停放大形态 |
| `settings.translucentTbCompatibilityPromptShown` | TranslucentTB 一次性处理状态 |

两个已发布叶子字段的内置默认值从 v1.3.2 的 `Left` 改为 main 的 `Center`：`settings.lyricsTextAlignment` 和 `settings.taskbarExperience.mediaTextAlignment`。明确保存的 Left 不会被强制改为 Center；字段缺失时的实际行为见逐项读取列，不能把两者混同。

未发布枚举成员共 15 个：新增五类枚举的全部成员，以及 `InterfaceLanguage.Vietnamese=4`。名称与数值逐项列在后文。

## 存储与格式契约

| 文件/内容 | 当前行为 | 入口 |
|---|---|---|
| `%LOCALAPPDATA%/AFMediaBar/settings.json` | 主设置；根含 `schemaVersion` 和 `settings` | [SettingsPersistenceService](../../src/AFMediaBar/Classes/Services/Settings/SettingsPersistenceService.cs) |
| `settings.json.bak` | 最近保存前的主文件副本；主文件不可用时尝试读取 | 同上 |
| `user-defaults.json` | 用户默认快照；同一 envelope、字段和 schema 读取规则 | 同上 |
| 未知/缺失 schema | 缺失得到 0；不等于 2 则不读取。主文件、编号不符的快照改名 `.unsupported-时间戳` 留档 | `ReadEnvelope` / `Quarantine` |
| 损坏主文件 | 改名 `.invalid-时间戳` 留档，再读有效备份；都不可用则用内置默认并保存 | `LoadCore` |
| 损坏备份 | 不采用，保留文件并记诊断；不主动隔离备份 | `LoadCore` |
| 损坏用户快照 | 返回 null，不自动删除/隔离；编号不符才隔离 | `LoadUserDefaults` |
| 缺失/null 的整个 settings | 使用新 AppSettings；未知字段被忽略，后续保存不会保留未知字段 | `ReadEnvelope` |
| JSON 命名与数值 | 写出 camelCase、枚举名称、默认和 null 字段；普通反序列化字段名大小写不敏感，枚举读有效名称/整数 | `_jsonOptions` / `LenientEnumConverter` |
| 注释、尾逗号 | 当前前置 JsonNode.Parse 实际拒绝，尽管后续 JsonSerializerOptions 开了相关选项；已用真实读取路径核对 | `ReadEnvelope` |
| 保存 | 300 ms 防抖；主设置临时文件、WriteThrough/Flush、旧文件备份、替换；失败保留内存设置并记诊断 | `SaveCore` |
| 用户快照保存 | 克隆后写独立临时文件并替换，成功才设置内存默认；没有主设置的 WriteThrough/备份过程 | `SaveCurrentAsUserDefaults` |
| 有效文件加载 | 不因缺省或归一化立即重写；后续设置变更/Flush 才写出规范化形式 | `LoadCore` |

两个 envelope 根键不是 AppSettings 属性：`schemaVersion` 为非可空 int、无有效值视为不支持；`settings` 为可空 AppSettings、缺失/null 回内置默认。schema 与本清单自身的 `formatVersion` 是独立编号。

应用使用 Generic Host 的默认配置机制；仓库未提供 `appsettings.json`，也未见将 IConfiguration 绑定到用户设置或 AppConfig 的调用方。[AppConfig](../../src/AFMediaBar/Classes/Models/AppConfig.cs) 的两个路径属性及旧 CanvasConfig/ComponentConfig 模型不属于当前持久化用户设置。`pending.json`、发布亮点/名单缓存、歌词缓存、Web 消息和播放器私有 JSON 是各服务数据协议，不作为 settings 字段；修改这些协议仍需按所属模块处理。

## 缺省、归一化与兼容行为

### 缺字段和显式 null

**内置默认、缺字段、显式 null 是三个不同输入。** AppSettings 的根属性缺失通常保留构造默认；嵌套 record struct 的父对象已存在时，缺少的构造字段或部分 init 属性可能先成为 0/false，再被 Normalize 夹取。比如缺少 `appearance.fontWeight` 得 100，而内置默认是 400；缺少 `update.autoCheckEnabled` 得 false，而内置默认是 true。不能仅凭 Default 或属性初始化器宣称旧文件保持默认。

全部路径的“缺字段后值”和“显式 null 读取”列均通过真实读取核对。CLR 非可空引用类型不等于 JSON 一定拒绝 null；例如 `lyricsArtistSeparators=null` 经 setter 成为空串，`accentColor=null` 回默认色。普通非可空 bool 的 null 则使整个读取失败。

五个旧几何字段有专门的 null 预处理：`layoutLengthScalePercent`、`layoutThicknessScalePercent`、`taskbarBarCrossAxisOffsetDip`、`dynamicIslandLeft`、`dynamicIslandTop`。标准 camelCase envelope 下，显式 null 会被当作缺失删除。预处理的 JsonNode 索引大小写敏感；大写 SETTINGS 下的非可空几何 null 没有同样保障，已验证会失败。

无效枚举的 JSON 输入先变成转换器的回退值，再归一化，未必等同于代码直接传入未定义值。例如第二行 `[999]` 从 JSON 读成 `[Translation]`，排布 `999` 读成 Left；代码直接传未定义成员则可能被过滤或回到 null。逐类 JSON 回退值见枚举表。

### 各设置段

| 设置段 | 归一化与有效含义 |
|---|---|
| 根级定位 | WindowMode 固定归一化为 Taskbar；整体长度/厚度比例 70–125，非有限回 100；跨轴偏移 -20–20 DIP，非有限回 0；灵动岛 left/top 负数或非有限回 null。手动 padding 不在模型统一夹取，运行布局再约束。 |
| 外观 | 枚举按模型默认回退；fontWeight 100–900、100 步长、AwayFromZero；窗口浓度 30–100，null 回 60；任务栏浓度 35–90，null 回 72。浓度只夹取，不强制按 UI 步长吸附。强调色接受带/不带 # 的6位RGB和8位ARGB（大小写不限），Trim 后解析；非法回 `#0078D4`，写成大写 #RRGGBB 并丢弃 alpha。 |
| 字体 | family=null 保留旧预设；空串显式跟随系统；Trim 后含逗号或超过 200 字符回空串。SelectedLatinFontFamily/SelectedCjkFontFamily 是 JsonIgnore 的派生值，不存入文件。 |
| 任务栏体验 | 内容排布、密度、文字对齐、长度模式回合法默认；arrangement 的代码非法值回 null；fixedLength 非有限或小于 120 回 360，否则夹 120–4096 DIP；间距 4–32，按钮间距 0–16，非有限回默认；字号非正回 100，否则夹 80–125，不强制按 UI 步长吸附。 |
| 封面 | ArtworkVisible 的 nullable backing 区分缺失与 false，缺失读 true；ArtworkHoverMode 缺失读 Off。当前 Off 仍有栏内 1.1× 放大，Zoom 为 2×，Preview 为独立预览；字段及三成员尚未发布。 |
| 完整/悬停控制 | 完整面板四项全关时回 Compact（媒体信息+控制）；悬停控制允许全关。初始完整默认 Full；hoverControls 默认仅输出、音频、进度开启。 |
| 静置组件顺序 | 存储列表过滤未定义成员和重复项，保留 null、空数组及顺序；运行排序固定封面/媒体文字在前，只排序尾部，遗漏的已知尾部按默认顺序补全。尾部默认频谱→性能→输出→音量。 |
| 无媒体保留项 | idleComponents=null 默认只留 Artwork（此状态指快速启动音符）；空数组表示全部隐藏；显式列表按选择保留。无媒体不显示媒体文字，Artwork 的静置封面开关不替代该列表。 |
| 交互 | 点击和滚轮枚举归一化；TrayClickAction 只接受 None、OpenSettings、OpenAudioControl、OpenContextMenu、OpenOutputDeviceMenu、OpenCurrentAppVolumeMenu，其余已定义的旧成员也回默认；托盘滚轮只接受 AdjustVolume、SwitchOutputDevice、Disabled。 |
| 基础表面 | taskbarSurface/dynamicIslandSurface 的透明度 0–100；圆角 0–24 DIP，非有限回 6；Style 回合法默认。保存结构不表示两个模式都在当前版本运行。 |
| 歌词呈现 | 未唱不透明度 20–80/步长5、字距0–20/步长1、行距0–24/步长2、固定宽80–600 DIP/步长10，均 AwayFromZero 后夹取。对齐、简繁处理不重新取词；原始歌词文档不被转换改写。 |
| 歌词第二行 | order=null 使用下一句→翻译→音译；空数组关闭第二行来源；显式列表过滤代码非法值及重复项，保留优先级。JSON 非法项先受枚举转换器回退，不能一概称为直接丢弃。 |
| 歌词来源/分隔符 | enabledSourceIds=null 全部启用，空数组全关；非空仅启用列出的已知来源。存储按 Ordinal 去重、Trim、去空白，保留未知 ID；使用时忽略未知，顺序不决定固定取词阶段。分隔符文本保留空白和换行，null/空串关闭分割。 |
| 媒体允许列表 | enabled=false 不过滤；true 且空列表不允许任何来源。列表 null→空，Trim、忽略大小写去重并排序；使用时经来源目录归一化别名。 |
| 快速启动 | entries=null→空；空 Target 或代码非法 Kind 被过滤；按 Kind+Target 忽略大小写去重。Target/Id/名称 Trim，空 Id 生成 N 格式 GUID，空名称取目标文件名无扩展名；SourceId 空白回 null。JSON 无效 Kind 可能先变为 Executable，再参与过滤。 |
| 频谱 | bandCount 9–24，refreshRateHz 5–30；灵敏度 10–400/步长10，高度14–34 DIP/步长2；吸附后夹取，非法风格回 Bars。 |
| 性能 | metrics 过滤代码非法值、去重、按数值排序；null/空/全部无效回 SystemMemory。间隔500–5000 ms/步长500；点击行为开关不改变采样配置。 |
| 曲目通知 | 时长1000–10000 ms；位置/目标枚举回默认；固定显示器空白回 null。运行时缺失固定目标经显示器策略回退，不改用户设置。 |
| 更新 | skippedVersion Trim，空回 null，最长32字符；上次检查时间和结果是状态字段。未来时间不在模型中抹掉，由调度按到期处理；没有自动下载设置。 |

各模型范围和默认常量的完整数值在后文；主要入口为 [SettingsManager](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs)、[AppearanceSettings](../../src/AFMediaBar/Classes/Settings/AppearanceSettings.cs)、[ExperienceSettings](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs)、[LyricsSettings](../../src/AFMediaBar/Classes/Settings/LyricsSettings.cs)。

### 显示器、来源和组件标识

- 目标显示器列表会 Trim、忽略大小写去重，保留已断开的 ID。列表为空且旧单目标为普通 ID 时，Normalize 迁入列表并清空旧单目标。
- 旧单目标字段中的 `{AFMediaBar.AllTaskbars}`（大小写不敏感）保留“所有当前任务栏”解析；不能迁成一次固定列表后丢掉语义。所选目标全部离线时临时回主屏，设置不被清空。[TaskbarTargetPolicy](../../src/AFMediaBar/Classes/Services/Taskbar/TaskbarTargetPolicy.cs)
- 歌词来源的持久化 ID 为 `Netease`、`NeteaseSearch`、`LRCLIB`、`QQMusic`、`Kugou`、`SodaMusic`，六项均在 v1.3.2 中存在；它们的名称大小写与媒体来源 ID 不是同一契约。[LyricsSourceCatalog](../../src/AFMediaBar/Classes/Services/Lyrics/LyricsSourceCatalog.cs)
- 网易云独立媒体 SourceId 为 `cloudmusic`、SelectionKey 为 `source:cloudmusic`，两者也在 v1.3.2 中存在；cloudmusic/netease/163music 标记识别不区分大小写。允许列表和快速启动来源按目录处理，其他来源 ID 为播放器提供的 SMTC 标识，开放集合不能伪造封闭白名单。[NetEaseSourcePolicy](../../src/AFMediaBar/Classes/Services/Media/Sources/NetEase/NetEaseSourcePolicy.cs)
- 持久化组件身份由 TaskbarRestComponent、MetricKind、SpectrumStyle 等枚举表记录。LayoutComponentIds 是运行布局标识；当前不经 AppSettings 写出，不混进设置字段表。

### 已停用字段、运行限制和旧 UI 入口

| 契约/入口 | 当前处理 |
|---|---|
| `appearance.enhancedReadability` | 已发布；仍保存，UI 和呈现效果已移除，值不再影响行为。 |
| 根 `trayWheelBehavior` | 已发布旧字段；仍克隆/归一化/保存。当前托盘输入映射使用 interaction 的两个滚轮字段，没有通用旧值→新字段转接。 |
| `taskbarBarBackgroundBlur`、灵动岛相关位置/表面 | 保留旧模型和序列化；当前可运行模式只有任务栏，WindowMode 被归一化为 Taskbar。不能因字段存在就宣称模式已实现。 |
| `settings.lyricsMatchStrictness` | v1.2.0/v1.2.1 发布过，v1.3.0 起移除；历史成员 Balanced=0、Strict=1、Exact=2。当前按未知字段忽略，不转接、不写回；保留历史记录，不能误标为未发布。 |
| DisplayModesPage | 旧完整页面仍留在源码和 DI/测试中，实际解析映射到 ScreenAndPlacementPage。封面放大控件已移至可达的 InteractionPage，旧页不再重复保留该控件。 |
| ApplicationAppearancePage | 旧类型入口映射到统一 AppearancePage，不创建重复设置页。 |
| 旧页面键/分组 | DisplayModes 的三层分组映射到 Components；其余到 ScreenAndPlacement。旧外观布局组、歌词对齐及组件组映射到当前内容页；旧共享组合键组映射到交互滚轮组。见目录映射。 |
| 模式/显示器/方向编辑上下文 | 缓存按环境隔离，不等于存储隔离；HasIndependentProfiles=false，各任务栏共享当前配置。 |

入口证据：[SettingsPageProvider](../../src/AFMediaBar/Views/Windows/SettingsPageProvider.cs)、[SettingsPageCatalog](../../src/AFMediaBar/Classes/Services/Settings/SettingsPageCatalog.cs)、[设置架构](../settings-architecture.md)。

### 启动兼容状态

`interfaceLanguage` 保存选择而非生效语言。System 按系统 UI 区域性解析：zh 中 Hant/TW/HK/MO/CHT 为繁体，其余中文为简体；vi 为越南语，其余和未知区域为英文。当前 vi 支持未发布；发布状态按成员表判断。[InterfaceLanguagePolicy](../../src/AFMediaBar/Classes/Services/Localization/InterfaceLanguagePolicy.cs)

`launchAtStartup=true` 是内置意图，不保证每次启动写入登记。StartupRegistrationService 启动先读取实际注册及 Windows 禁用状态并同步；未知时保留设置并暴露未知状态。只有用户主动开启才允许解除系统禁用，重置/启动同步不解除；失败回到实际读取状态或上次值。[StartupRegistrationService](../../src/AFMediaBar/Classes/Services/Startup/StartupRegistrationService.cs)

TranslucentTB 被检测到且 `translucentTbCompatibilityPromptShown=false` 时，首次处理会启用任务栏磨砂（若尚未开启）、先标记并落盘，再提示。标记已为 true 后不再自动改动；ResetAll 保留当前标记，不能让保存的默认快照重新触发。该字段及对应背景设置尚未发布。[启动处理](../../src/AFMediaBar/App.xaml.cs)

## 用户默认快照和重置归属

用户快照在 Host 启动前加载并 Normalize；生效 Defaults 为有效用户快照，否则为内置 AppSettings。保存快照不改变当前偏好；删除快照后重置回内置默认。页面缓存、重置枚举和当前 UI 名称不是存储 schema。

| 当前入口 | 恢复范围与保留项 |
|---|---|
| ResetApplicationAppearance | 应用字体、主题、材质；保留媒体文字色、任务栏背景/浓度/风格。 |
| ResetTaskbarAppearance | 媒体文字色、任务栏背景/浓度/风格、媒体字号；保留全局主题字体及布局。 |
| ResetContentLayout | 整段 TaskbarExperience，但保留当前媒体字号；频谱、性能、歌词对齐及固定长度设置；不改位置、取词、文本处理和封面悬停模式。 |
| ResetScreenAndPlacement | 目标列表/旧目标、位置、手动 padding、跨轴偏移、避让、锁定；不恢复各层内容。 |
| ResetLyrics | 歌词启用、双行与来源顺序、浏览器许可、擦亮/不透明度/字距/行距、过滤/简繁、来源/艺术家分隔符；保留已移出的对齐与固定长度。 |
| ResetInteraction | 整段 Interaction，包含封面悬停模式；保留封面显隐。 |
| ResetExtraFeatures | 通知、SMTC 过滤、快速启动。 |
| ResetComponents | 频谱、性能两段；与新内容页整体重置区分。 |
| ResetGeneral | 克隆当前值并发布 General 重置边界，不改变偏好。 |
| ResetAll | 从生效 Defaults 克隆全部设置，但保留当前 TranslucentTB 已提示标记。 |
| 旧 ResetAppearance | 整段外观、两个基础表面，以及字号/密度/文字排列对齐/间距/整体长度/静置顺序；不是新外观页重置。 |
| 旧 ResetDisplayModes | 层和组件显隐、完整/悬停控制、左右排布、无媒体保留项，以及模式/朝向/显示目标/定位和灵动岛表面；保留媒体字号、排序及长度。 |
| 旧 ResetLayout | 根级模式/定位/比例/灵动岛位置等；不是新内容页整体重置。 |

上述为 [SettingsManager](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs) 的实际范围。旧 API 保留不表示新页面仍调用旧重置；改调用方时应核对 ViewModel 和配置上下文。

## 开发与发布维护

1. 新增/调整设置时只更新涉及字段、枚举成员、默认语义及来源证据；未发布项明确标为未发布，已删除发布项进入历史记录。
2. 已发布契约变化需要在变更说明中写明触发条件、旧数据处理选择和用户影响；测试相同 schema 的旧字段、用户快照及相关重置。不要机械添加无用迁移，也不要靠升 schema 隐去未讨论的影响。
3. 每次正式发布核对实际 tag/产物来源，更新 latestRelease、发布证据、正式默认快照，统一把本次进入产物的未发布项标成首次发布版本；草稿和失败发布不算正式发布。schema 仍只在经批准的发布批次调整。
4. 本文与 JSON 清单同步更新。默认值、缺字段及 null 行为用实际序列化/Normalize/ReadEnvelope 核实；不要从 UI 文案或初始化器猜测。列表项的示例输入不记作用户默认。
5. 源码指纹更新前完成字段集合、枚举和相关兼容逻辑核对。验证工具只检查已核对证据是否陈旧，不自行放行契约变更，不联网或改写源文件。


## 配置路径全集

路径相对于文件根；`[]` 表示列表条目。对象行也计入路径数。内置默认值取真实 `new AppSettings().Normalize()`，不代表用户默认快照；原始构造默认值及 v1.3.2 默认值见 JSON 清单。可空列表示 CLR 声明，JSON 的 null 接受性以实际读取结果列为准。

缺字段测试保留父对象及其他默认字段，仅删除目标属性；不能等同于整个父对象缺失。列表条目使用固定示例 `id=contract, displayName=fixture, kind=Executable, target=fixture.exe, sourceId=null`，示例不是用户默认条目。138 个路径均用真实 ReadEnvelope 核对缺失及显式 null，共 276 次读取。

### 根级设置

| JSON 路径 | 类型；可空 | 内置有效默认值 | 缺字段后值 | 显式 null 读取 | 首次发布 | 定义 |
|---|---|---|---|---|---|---|
| `settings.allowBrowserAndVideoLyrics` | `bool`；否 | `false` | 同内置默认 | 读取失败：`JsonException` | v1.3.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L116) |
| `settings.appearance` | `AppearanceSettings`；否 | 对象，见下级字段 | 同内置默认 | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L113) |
| `settings.dynamicIslandBackgroundMode` | `DynamicIslandBackgroundMode`；否 | `"SystemTheme"` | 同内置默认 | `"SystemTheme"` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L147) |
| `settings.dynamicIslandEdge` | `DynamicIslandEdge`；否 | `"Top"` | 同内置默认 | `"Top"` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L153) |
| `settings.dynamicIslandEdgeDocked` | `bool`；否 | `true` | 同内置默认 | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L154) |
| `settings.dynamicIslandLeft` | `double?`；是 | `null` | 同内置默认 | `null` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L151) |
| `settings.dynamicIslandSurface` | `ModeSurfaceSettings`；否 | 对象，见下级字段 | 同内置默认 | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L158) |
| `settings.dynamicIslandTop` | `double?`；是 | `null` | 同内置默认 | `null` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L152) |
| `settings.interaction` | `GlobalInteractionSettings`；否 | 对象，见下级字段 | 同内置默认 | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L156) |
| `settings.interfaceLanguage` | `InterfaceLanguage`；否 | `"System"` | 同内置默认 | `"System"` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L232) |
| `settings.launchAtStartup` | `bool`；否 | `true` | 同内置默认 | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L223) |
| `settings.layoutLengthScalePercent` | `double`；否 | `100` | 同内置默认 | `100` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L145) |
| `settings.layoutOrientationMode` | `LayoutOrientationMode`；否 | `"Auto"` | 同内置默认 | `"Auto"` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L144) |
| `settings.layoutThicknessScalePercent` | `double`；否 | `100` | 同内置默认 | `100` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L146) |
| `settings.lyricsArtistSeparators` | `string`；否 | `"/"` | 同内置默认 | `""` | v1.3.2 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L198) |
| `settings.lyricsCharacterSpacingPercent` | `int`；否 | `0` | 同内置默认 | 读取失败：`JsonException` | v1.3.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L168) |
| `settings.lyricsChineseConversion` | `LyricsChineseConversionMode`；否 | `"None"` | 同内置默认 | `"None"` | **未发布** | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L187) |
| `settings.lyricsEnabled` | `bool`；否 | `true` | 同内置默认 | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L115) |
| `settings.lyricsFixedWidthDip` | `int`；否 | `240` | 同内置默认 | 读取失败：`JsonException` | v1.3.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L177) |
| `settings.lyricsFixedWidthEnabled` | `bool`；否 | `false` | 同内置默认 | 读取失败：`JsonException` | v1.3.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L174) |
| `settings.lyricsInfoLineFilterEnabled` | `bool`；否 | `true` | 同内置默认 | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L180) |
| `settings.lyricsLineGapPercent` | `int`；否 | `0` | 同内置默认 | 读取失败：`JsonException` | v1.3.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L171) |
| `settings.lyricsSecondaryLine` | `LyricsSecondaryLineSettings`；否 | 对象，见下级字段 | 同内置默认 | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L118) |
| `settings.lyricsSource` | `LyricsSourceSettings`；否 | 对象，见下级字段 | 同内置默认 | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L195) |
| `settings.lyricsSyllableHighlightEnabled` | `bool`；否 | `true` | 同内置默认 | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L162) |
| `settings.lyricsTextAlignment` | `LyricsTextAlignment`；否 | `"Center"` | 同内置默认 | `"Center"` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L159) |
| `settings.lyricsUnsungOpacityPercent` | `int`；否 | `45` | 同内置默认 | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L165) |
| `settings.performanceComponent` | `PerformanceComponentSettings`；否 | 对象，见下级字段 | 同内置默认 | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L212) |
| `settings.position` | `TaskbarBarPosition`；否 | `"Start"` | 同内置默认 | `"Start"` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L140) |
| `settings.quickLaunch` | `QuickLaunchSettings`；否 | 对象，见下级字段 | 同内置默认 | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L210) |
| `settings.smtcSourceFilter` | `SmtcSourceFilterSettings`；否 | 对象，见下级字段 | 同内置默认 | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L209) |
| `settings.spectrumComponent` | `SpectrumComponentSettings`；否 | 对象，见下级字段 | 同内置默认 | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L211) |
| `settings.taskbarBarAvoidIcons` | `bool`；否 | `true` | 同内置默认 | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L149) |
| `settings.taskbarBarBackgroundBlur` | `bool`；否 | `false` | 同内置默认 | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L141) |
| `settings.taskbarBarCrossAxisOffsetDip` | `double`；否 | `0` | 同内置默认 | `0` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L148) |
| `settings.taskbarBarEnabled` | `bool`；否 | `true` | 同内置默认 | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L119) |
| `settings.taskbarBarManualPadding` | `int`；否 | `0` | 同内置默认 | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L142) |
| `settings.taskbarBarPositionLocked` | `bool`；否 | `false` | 同内置默认 | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L150) |
| `settings.taskbarExperience` | `TaskbarExperienceSettings`；否 | 对象，见下级字段 | 同内置默认 | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L155) |
| `settings.taskbarSurface` | `ModeSurfaceSettings`；否 | 对象，见下级字段 | 同内置默认 | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L157) |
| `settings.taskbarTargetMonitorDeviceId` | `string?`；是 | `null` | 同内置默认 | `null` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L135) |
| `settings.taskbarTargetMonitorDeviceIds` | `IReadOnlyList<string>?`；是 | `null` | 同内置默认 | `null` | v1.2.1 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L125) |
| `settings.trackChangeNotification` | `TrackChangeNotificationSettings`；否 | 对象，见下级字段 | 同内置默认 | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L204) |
| `settings.translucentTbCompatibilityPromptShown` | `bool`；否 | `false` | 同内置默认 | 读取失败：`JsonException` | **未发布** | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L239) |
| `settings.trayWheelBehavior` | `TrayWheelBehavior`；否 | `"SwitchOutputDevice"` | 同内置默认 | `"SwitchOutputDevice"` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L114) |
| `settings.twoLineLyricsEnabled` | `bool`；否 | `true` | 同内置默认 | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L117) |
| `settings.update` | `UpdateSettings`；否 | 对象，见下级字段 | 同内置默认 | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L215) |
| `settings.windowMode` | `WindowMode`；否 | `"Taskbar"` | 同内置默认 | `"Taskbar"` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L143) |

### appearance

| JSON 路径 | 类型；可空 | 内置有效默认值 | 缺字段后值 | 显式 null 读取 | 首次发布 | 定义 |
|---|---|---|---|---|---|---|
| `settings.appearance.accentColor` | `string`；否 | `"#0078D4"` | 同内置默认 | `"#0078D4"` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/AppearanceSettings.cs#L116) |
| `settings.appearance.accentColorMode` | `AccentColorMode`；否 | `"System"` | 同内置默认 | `"System"` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/AppearanceSettings.cs#L115) |
| `settings.appearance.applicationThemeMode` | `ApplicationThemeMode`；否 | `"Automatic"` | 同内置默认 | `"Automatic"` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/AppearanceSettings.cs#L108) |
| `settings.appearance.backdropMode` | `ApplicationBackdropMode`；否 | `"Mica"` | `"FluentSolid"` | `"Mica"` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/AppearanceSettings.cs#L109) |
| `settings.appearance.backdropTintOpacityPercent` | `int?`；是 | `60` | 同内置默认 | `60` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/AppearanceSettings.cs#L122) |
| `settings.appearance.cjkFont` | `CjkFontPreset`；否 | `"SystemDefault"` | 同内置默认 | `"SystemDefault"` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/AppearanceSettings.cs#L101) |
| `settings.appearance.cjkFontFamily` | `string?`；是 | `null` | 同内置默认 | `null` | v1.3.0 | [源码](../../src/AFMediaBar/Classes/Settings/AppearanceSettings.cs#L124) |
| `settings.appearance.enhancedReadability` | `bool`；否 | `false` | 同内置默认 | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/AppearanceSettings.cs#L107) |
| `settings.appearance.fontWeight` | `int`；否 | `400` | `100` | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/AppearanceSettings.cs#L102) |
| `settings.appearance.latinFont` | `LatinFontPreset`；否 | `"SystemDefault"` | `"SegoeUi"` | `"SystemDefault"` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/AppearanceSettings.cs#L100) |
| `settings.appearance.latinFontFamily` | `string?`；是 | `null` | 同内置默认 | `null` | v1.3.0 | [源码](../../src/AFMediaBar/Classes/Settings/AppearanceSettings.cs#L123) |
| `settings.appearance.playerForegroundMode` | `PlayerForegroundMode`；否 | `"Automatic"` | 同内置默认 | `"Automatic"` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/AppearanceSettings.cs#L103) |
| `settings.appearance.taskbarBackgroundMaterial` | `TaskbarBackgroundMaterial`；否 | `"Transparent"` | 同内置默认 | `"Transparent"` | **未发布** | [源码](../../src/AFMediaBar/Classes/Settings/AppearanceSettings.cs#L125) |
| `settings.appearance.taskbarBackgroundOpacityPercent` | `int?`；是 | `72` | 同内置默认 | `72` | **未发布** | [源码](../../src/AFMediaBar/Classes/Settings/AppearanceSettings.cs#L126) |
| `settings.appearance.taskbarFrostedStyle` | `TaskbarFrostedStyle`；否 | `"Neutral"` | 同内置默认 | `"Neutral"` | **未发布** | [源码](../../src/AFMediaBar/Classes/Settings/AppearanceSettings.cs#L127) |

### dynamicIslandSurface

| JSON 路径 | 类型；可空 | 内置有效默认值 | 缺字段后值 | 显式 null 读取 | 首次发布 | 定义 |
|---|---|---|---|---|---|---|
| `settings.dynamicIslandSurface.backgroundOpacityPercent` | `int`；否 | `100` | `0` | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L747) |
| `settings.dynamicIslandSurface.cornerRadiusDip` | `double`；否 | `6` | `0` | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L748) |
| `settings.dynamicIslandSurface.style` | `PlayerSurfaceStyle`；否 | `"Automatic"` | 同内置默认 | `"Automatic"` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L746) |

### interaction

| JSON 路径 | 类型；可空 | 内置有效默认值 | 缺字段后值 | 显式 null 读取 | 首次发布 | 定义 |
|---|---|---|---|---|---|---|
| `settings.interaction.artworkClickAction` | `PlayerClickAction`；否 | `"TogglePlayPause"` | 同内置默认 | `"TogglePlayPause"` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L685) |
| `settings.interaction.artworkHoverMode` | `ArtworkHoverMode`；否 | `"Off"` | 同内置默认 | `"Off"` | **未发布** | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L702) |
| `settings.interaction.chordWheelAction` | `WheelAction`；否 | `"SwitchMediaSource"` | `"PreviousNext"` | `"PreviousNext"` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L689) |
| `settings.interaction.modifier` | `InteractionModifier`；否 | `"Shift"` | 同内置默认 | `"Shift"` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L688) |
| `settings.interaction.primaryWheelAction` | `WheelAction`；否 | `"PreviousNext"` | 同内置默认 | `"PreviousNext"` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L687) |
| `settings.interaction.showWheelTooltips` | `bool`；否 | `true` | `false` | 读取失败：`JsonException` | v1.3.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L695) |
| `settings.interaction.textClickAction` | `PlayerClickAction`；否 | `"ActivateSource"` | `"TogglePlayPause"` | `"TogglePlayPause"` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L686) |
| `settings.interaction.trayChordWheelAction` | `TrayWheelBehavior`；否 | `"AdjustVolume"` | 同内置默认 | `"SwitchOutputDevice"` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L692) |
| `settings.interaction.trayClickAction` | `TrayClickAction`；否 | `"OpenAudioControl"` | `"None"` | `"OpenAudioControl"` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L690) |
| `settings.interaction.trayPrimaryWheelAction` | `TrayWheelBehavior`；否 | `"SwitchOutputDevice"` | `"AdjustVolume"` | `"SwitchOutputDevice"` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L691) |

### lyricsSecondaryLine

| JSON 路径 | 类型；可空 | 内置有效默认值 | 缺字段后值 | 显式 null 读取 | 首次发布 | 定义 |
|---|---|---|---|---|---|---|
| `settings.lyricsSecondaryLine.order` | `IReadOnlyList<LyricsSecondaryLineMode>?`；是 | `null` | 同内置默认 | `null` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/LyricsSettings.cs#L180) |

### lyricsSource

| JSON 路径 | 类型；可空 | 内置有效默认值 | 缺字段后值 | 显式 null 读取 | 首次发布 | 定义 |
|---|---|---|---|---|---|---|
| `settings.lyricsSource.enabledSourceIds` | `IReadOnlyList<string>?`；是 | `null` | 同内置默认 | `null` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/LyricsSourceSettings.cs#L21) |

### performanceComponent

| JSON 路径 | 类型；可空 | 内置有效默认值 | 缺字段后值 | 显式 null 读取 | 首次发布 | 定义 |
|---|---|---|---|---|---|---|
| `settings.performanceComponent.metrics` | `IReadOnlyList<MetricKind>?`；是 | `["SystemMemory"]` | 同内置默认 | `["SystemMemory"]` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L313) |
| `settings.performanceComponent.openTaskManagerOnClick` | `bool`；否 | `true` | `false` | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L315) |
| `settings.performanceComponent.refreshIntervalMilliseconds` | `int`；否 | `2500` | `500` | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L314) |

### quickLaunch

| JSON 路径 | 类型；可空 | 内置有效默认值 | 缺字段后值 | 显式 null 读取 | 首次发布 | 定义 |
|---|---|---|---|---|---|---|
| `settings.quickLaunch.entries` | `IReadOnlyList<QuickLaunchEntry>?`；是 | `[]` | 同内置默认 | `[]` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L151) |
| `settings.quickLaunch.entries[].displayName` | `string`；否 | 条目模板；列表默认无条目 | `"fixture"` | `"fixture"` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Models/ExtraFeatureModels.cs#L14) |
| `settings.quickLaunch.entries[].id` | `string`；否 | 条目模板；列表默认无条目 | 生成 N 格式 GUID | 生成 N 格式 GUID | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Models/ExtraFeatureModels.cs#L13) |
| `settings.quickLaunch.entries[].kind` | `QuickLaunchTargetKind`；否 | 条目模板；列表默认无条目 | `"Executable"` | `"Executable"` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Models/ExtraFeatureModels.cs#L15) |
| `settings.quickLaunch.entries[].sourceId` | `string?`；是 | 条目模板；列表默认无条目 | `null` | `null` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Models/ExtraFeatureModels.cs#L17) |
| `settings.quickLaunch.entries[].target` | `string`；否 | 条目模板；列表默认无条目 | 条目被过滤 | 条目被过滤 | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Models/ExtraFeatureModels.cs#L16) |

### smtcSourceFilter

| JSON 路径 | 类型；可空 | 内置有效默认值 | 缺字段后值 | 显式 null 读取 | 首次发布 | 定义 |
|---|---|---|---|---|---|---|
| `settings.smtcSourceFilter.allowedSourceIds` | `IReadOnlyList<string>?`；是 | `[]` | 同内置默认 | `[]` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L135) |
| `settings.smtcSourceFilter.enabled` | `bool`；否 | `false` | 同内置默认 | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L135) |

### spectrumComponent

| JSON 路径 | 类型；可空 | 内置有效默认值 | 缺字段后值 | 显式 null 读取 | 首次发布 | 定义 |
|---|---|---|---|---|---|---|
| `settings.spectrumComponent.bandCount` | `int`；否 | `9` | 同内置默认 | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L199) |
| `settings.spectrumComponent.contentHeightDip` | `double`；否 | `26` | `14` | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L248) |
| `settings.spectrumComponent.refreshRateHz` | `int`；否 | `20` | `5` | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L199) |
| `settings.spectrumComponent.sensitivityPercent` | `int`；否 | `100` | `10` | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L199) |
| `settings.spectrumComponent.style` | `SpectrumStyle`；否 | `"Bars"` | 同内置默认 | `"Bars"` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L208) |

### taskbarExperience

| JSON 路径 | 类型；可空 | 内置有效默认值 | 缺字段后值 | 显式 null 读取 | 首次发布 | 定义 |
|---|---|---|---|---|---|---|
| `settings.taskbarExperience.arrangement` | `TaskbarContentArrangement?`；是 | `null` | 同内置默认 | `null` | **未发布** | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L503) |
| `settings.taskbarExperience.artworkVisible` | `bool`；否 | `true` | 同内置默认 | 读取失败：`JsonException` | v1.3.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L509) |
| `settings.taskbarExperience.componentSpacingDip` | `double`；否 | `12` | `4` | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L494) |
| `settings.taskbarExperience.contentLayout` | `TaskbarContentLayout`；否 | `"AdaptiveStack"` | `"CompactInline"` | `"AdaptiveStack"` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L488) |
| `settings.taskbarExperience.density` | `TaskbarInformationDensity`；否 | `"Balanced"` | `"Minimal"` | `"Balanced"` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L487) |
| `settings.taskbarExperience.fixedLengthDip` | `double`；否 | `360` | 同内置默认 | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L491) |
| `settings.taskbarExperience.fullLayerEnabled` | `bool`；否 | `true` | `false` | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L486) |
| `settings.taskbarExperience.fullPanel` | `TaskbarFullPanelSettings`；否 | 对象，见下级字段 | `{"mediaInfoVisible":true,"mediaControlsVisible":true,"audioControlsVisible":false,"performanceVisible":false}` | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L489) |
| `settings.taskbarExperience.fullPanel.audioControlsVisible` | `bool`；否 | `true` | `false` | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L432) |
| `settings.taskbarExperience.fullPanel.mediaControlsVisible` | `bool`；否 | `true` | `false` | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L431) |
| `settings.taskbarExperience.fullPanel.mediaInfoVisible` | `bool`；否 | `true` | `false` | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L430) |
| `settings.taskbarExperience.fullPanel.performanceVisible` | `bool`；否 | `true` | `false` | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L433) |
| `settings.taskbarExperience.fullPanelEntryVisible` | `bool`；否 | `true` | `false` | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L531) |
| `settings.taskbarExperience.hoverButtonSpacingDip` | `double`；否 | `2` | `0` | 读取失败：`JsonException` | v1.3.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L497) |
| `settings.taskbarExperience.hoverControls` | `TaskbarHoverControlsSettings`；否 | 对象，见下级字段 | `{"playPauseVisible":false,"previousNextVisible":false,"outputDeviceVisible":false,"audioControlVisible":false,"progressVisible":false}` | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L534) |
| `settings.taskbarExperience.hoverControls.audioControlVisible` | `bool`；否 | `true` | `false` | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L456) |
| `settings.taskbarExperience.hoverControls.outputDeviceVisible` | `bool`；否 | `true` | `false` | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L455) |
| `settings.taskbarExperience.hoverControls.playPauseVisible` | `bool`；否 | `false` | 同内置默认 | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L453) |
| `settings.taskbarExperience.hoverControls.previousNextVisible` | `bool`；否 | `false` | 同内置默认 | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L454) |
| `settings.taskbarExperience.hoverControls.progressVisible` | `bool`；否 | `true` | `false` | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L457) |
| `settings.taskbarExperience.hoverLayerEnabled` | `bool`；否 | `true` | `false` | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L485) |
| `settings.taskbarExperience.idleComponents` | `IReadOnlyList<TaskbarRestComponent>?`；是 | `null` | 同内置默认 | `null` | v1.2.1 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L575) |
| `settings.taskbarExperience.lengthMode` | `TaskbarLengthMode`；否 | `"FollowContent"` | 同内置默认 | `"FollowContent"` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L490) |
| `settings.taskbarExperience.mediaFontSizePercent` | `int`；否 | `100` | 同内置默认 | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L581) |
| `settings.taskbarExperience.mediaTextAlignment` | `TaskbarMediaTextAlignment`；否 | `"Center"` | `"Left"` | `"Left"` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L500) |
| `settings.taskbarExperience.outputDeviceVisible` | `bool`；否 | `false` | 同内置默认 | 读取失败：`JsonException` | v1.2.1 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L542) |
| `settings.taskbarExperience.performanceVisible` | `bool`；否 | `true` | `false` | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L514) |
| `settings.taskbarExperience.restComponentOrder` | `IReadOnlyList<TaskbarRestComponent>?`；是 | `null` | 同内置默认 | `null` | v1.2.1 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L561) |
| `settings.taskbarExperience.restProgressVisible` | `bool`；否 | `false` | 同内置默认 | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L522) |
| `settings.taskbarExperience.spectrumVisible` | `bool`；否 | `true` | `false` | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L506) |
| `settings.taskbarExperience.volumeVisible` | `bool`；否 | `false` | 同内置默认 | 读取失败：`JsonException` | v1.2.1 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L545) |

### taskbarSurface

| JSON 路径 | 类型；可空 | 内置有效默认值 | 缺字段后值 | 显式 null 读取 | 首次发布 | 定义 |
|---|---|---|---|---|---|---|
| `settings.taskbarSurface.backgroundOpacityPercent` | `int`；否 | `100` | `0` | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L747) |
| `settings.taskbarSurface.cornerRadiusDip` | `double`；否 | `6` | `0` | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L748) |
| `settings.taskbarSurface.style` | `PlayerSurfaceStyle`；否 | `"Automatic"` | 同内置默认 | `"Automatic"` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L746) |

### trackChangeNotification

| JSON 路径 | 类型；可空 | 内置有效默认值 | 缺字段后值 | 显式 null 读取 | 首次发布 | 定义 |
|---|---|---|---|---|---|---|
| `settings.trackChangeNotification.durationMilliseconds` | `int`；否 | `3000` | `1000` | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L394) |
| `settings.trackChangeNotification.enabled` | `bool`；否 | `true` | `false` | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L392) |
| `settings.trackChangeNotification.fixedMonitorDeviceId` | `string?`；是 | `null` | 同内置默认 | `null` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L397) |
| `settings.trackChangeNotification.position` | `TrackChangeNotificationPosition`；否 | `"TopCenter"` | `"BottomLeft"` | `"TopCenter"` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L395) |
| `settings.trackChangeNotification.showWhenFullscreen` | `bool`；否 | `true` | `false` | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L393) |
| `settings.trackChangeNotification.targetMode` | `NotificationTargetMode`；否 | `"ForegroundWindow"` | `"Fixed"` | `"ForegroundWindow"` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L396) |

### update

| JSON 路径 | 类型；可空 | 内置有效默认值 | 缺字段后值 | 显式 null 读取 | 首次发布 | 定义 |
|---|---|---|---|---|---|---|
| `settings.update.autoCheckEnabled` | `bool`；否 | `true` | `false` | 读取失败：`JsonException` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/UpdateSettings.cs#L26) |
| `settings.update.lastCheckSucceeded` | `bool?`；是 | `null` | 同内置默认 | `null` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/UpdateSettings.cs#L29) |
| `settings.update.lastCheckUtc` | `DateTimeOffset?`；是 | `null` | 同内置默认 | `null` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/UpdateSettings.cs#L28) |
| `settings.update.skippedVersion` | `string?`；是 | `null` | 同内置默认 | `null` | v1.2.0 | [源码](../../src/AFMediaBar/Classes/Settings/UpdateSettings.cs#L27) |

## 持久化枚举全集

写出名称字符串；读取接受已定义的名称（大小写不敏感）和整数值。表中无效输入回退由当前真实转换器测得，之后仍会经过字段归一化。成员是否已发布独立于父字段。

### AccentColorMode

[源码](../../src/AFMediaBar/Classes/Settings/AppearanceSettings.cs#L65)；JSON 无效输入回退：`System`。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `System` | 0 | v1.2.0 |
| `Custom` | 1 | v1.2.0 |

### ApplicationBackdropMode

[源码](../../src/AFMediaBar/Classes/Settings/AppearanceSettings.cs#L24)；JSON 无效输入回退：`Mica`。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `FluentSolid` | 0 | v1.2.0 |
| `Mica` | 1 | v1.2.0 |
| `Acrylic` | 2 | v1.2.0 |
| `MicaAlt` | 3 | v1.2.0 |

### ApplicationThemeMode

[源码](../../src/AFMediaBar/Classes/Settings/AppearanceSettings.cs#L16)；JSON 无效输入回退：`Automatic`。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Automatic` | 0 | v1.2.0 |
| `Light` | 1 | v1.2.0 |
| `Dark` | 2 | v1.2.0 |

### ArtworkHoverMode

[源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L474)；JSON 无效输入回退：`Off`。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Off` | 0 | **未发布** |
| `Zoom` | 1 | **未发布** |
| `Preview` | 2 | **未发布** |

### CjkFontPreset

[源码](../../src/AFMediaBar/Classes/Settings/AppearanceSettings.cs#L86)；JSON 无效输入回退：`SystemDefault`。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `SystemDefault` | 0 | v1.2.0 |
| `MicrosoftYaHei` | 1 | v1.2.0 |
| `DengXian` | 2 | v1.2.0 |
| `SimSun` | 3 | v1.2.0 |
| `SimHei` | 4 | v1.2.0 |
| `KaiTi` | 5 | v1.2.0 |
| `FangSong` | 6 | v1.2.0 |

### DynamicIslandBackgroundMode

[源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L14)；JSON 无效输入回退：`SystemTheme`。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `SystemTheme` | 0 | v1.2.0 |
| `Transparent` | 1 | v1.2.0 |

### DynamicIslandEdge

[源码](../../src/AFMediaBar/Classes/Models/Layout/LayoutEnums.cs#L38)；JSON 无效输入回退：`Top`。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Top` | 0 | v1.2.0 |
| `Right` | 1 | v1.2.0 |
| `Bottom` | 2 | v1.2.0 |
| `Left` | 3 | v1.2.0 |

### InteractionModifier

[源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L50)；JSON 无效输入回退：`Shift`。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Shift` | 0 | v1.2.0 |
| `LeftMouseButton` | 1 | v1.2.0 |
| `RightMouseButton` | 2 | v1.2.0 |

### InterfaceLanguage

[源码](../../src/AFMediaBar/Classes/Services/Localization/InterfaceLanguage.cs#L17)；JSON 无效输入回退：`System`。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `System` | 0 | v1.2.0 |
| `SimplifiedChinese` | 1 | v1.2.0 |
| `TraditionalChinese` | 2 | v1.2.0 |
| `English` | 3 | v1.2.0 |
| `Vietnamese` | 4 | **未发布** |

### LatinFontPreset

[源码](../../src/AFMediaBar/Classes/Settings/AppearanceSettings.cs#L74)；JSON 无效输入回退：`SystemDefault`。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `SegoeUi` | 0 | v1.2.0 |
| `Arial` | 1 | v1.2.0 |
| `Calibri` | 2 | v1.2.0 |
| `Verdana` | 3 | v1.2.0 |
| `Consolas` | 4 | v1.2.0 |
| `TimesNewRoman` | 5 | v1.2.0 |
| `SystemDefault` | 6 | v1.3.0 |

### LayoutOrientationMode

[源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L12)；JSON 无效输入回退：`Auto`。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Auto` | 0 | v1.2.0 |
| `Horizontal` | 1 | v1.2.0 |
| `Vertical` | 2 | v1.2.0 |

### LyricsChineseConversionMode

[源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L43)；JSON 无效输入回退：`None`。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `None` | 0 | **未发布** |
| `SimplifiedToTraditional` | 1 | **未发布** |
| `TraditionalToSimplified` | 2 | **未发布** |

### LyricsSecondaryLineMode

[源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L21)；JSON 无效输入回退：`Translation`。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `NextLine` | 0 | v1.2.0 |
| `Translation` | 1 | v1.2.0 |
| `Romanization` | 2 | v1.2.0 |

### LyricsTextAlignment

[源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L129)；JSON 无效输入回退：`Center`。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Left` | 0 | v1.2.0 |
| `Center` | 1 | v1.2.0 |
| `Right` | 2 | v1.2.0 |

### MetricKind

[源码](../../src/AFMediaBar/Classes/Models/ExtraFeatureModels.cs#L32)；JSON 无效输入回退：`SystemMemory`。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `SystemMemory` | 0 | v1.2.0 |
| `SystemCpu` | 1 | v1.2.0 |
| `SystemGpu` | 2 | v1.2.0 |
| `ProcessMemory` | 3 | v1.2.0 |

### NotificationTargetMode

[源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L386)；JSON 无效输入回退：`ForegroundWindow`。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Fixed` | 0 | v1.2.0 |
| `ForegroundWindow` | 1 | v1.2.0 |

### PlayerClickAction

[源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L37)；JSON 无效输入回退：`TogglePlayPause`。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `TogglePlayPause` | 0 | v1.2.0 |
| `ActivateSource` | 1 | v1.2.0 |
| `OpenFullPanel` | 2 | v1.2.0 |
| `Disabled` | 3 | v1.2.0 |

### PlayerForegroundMode

[源码](../../src/AFMediaBar/Classes/Settings/AppearanceSettings.cs#L8)；JSON 无效输入回退：`Automatic`。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Automatic` | 0 | v1.2.0 |
| `LightText` | 1 | v1.2.0 |
| `DarkText` | 2 | v1.2.0 |

### PlayerSurfaceStyle

[源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L121)；JSON 无效输入回退：`Automatic`。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Automatic` | 0 | v1.2.0 |
| `Solid` | 1 | v1.2.0 |
| `ThemeTint` | 2 | v1.2.0 |

### QuickLaunchTargetKind

[源码](../../src/AFMediaBar/Classes/Models/ExtraFeatureModels.cs#L6)；JSON 无效输入回退：`Executable`。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Executable` | 0 | v1.2.0 |
| `Shortcut` | 1 | v1.2.0 |
| `AppUserModelId` | 2 | v1.2.0 |

### SpectrumStyle

[源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L186)；JSON 无效输入回退：`Bars`。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Bars` | 0 | v1.2.0 |
| `Waveform` | 1 | v1.2.0 |
| `PixelBars` | 2 | v1.2.0 |
| `MirroredBars` | 3 | v1.2.0 |

### TaskbarBackgroundMaterial

[源码](../../src/AFMediaBar/Classes/Settings/AppearanceSettings.cs#L39)；JSON 无效输入回退：`Transparent`。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Transparent` | 0 | **未发布** |
| `Frosted` | 1 | **未发布** |

### TaskbarBarPosition

[源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L10)；JSON 无效输入回退：`Start`。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Start` | 0 | v1.2.0 |
| `Center` | 1 | v1.2.0 |
| `End` | 2 | v1.2.0 |

### TaskbarContentArrangement

[源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L82)；JSON 无效输入回退：`Left`。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Left` | 0 | **未发布** |
| `Right` | 1 | **未发布** |

### TaskbarContentLayout

[源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L66)；JSON 无效输入回退：`AdaptiveStack`。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `CompactInline` | 0 | v1.2.0 |
| `AdaptiveStack` | 1 | v1.2.0 |
| `CenteredStack` | 2 | v1.2.0 |

### TaskbarFrostedStyle

[源码](../../src/AFMediaBar/Classes/Settings/AppearanceSettings.cs#L49)；JSON 无效输入回退：`Neutral`。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Neutral` | 0 | **未发布** |
| `Cool` | 1 | **未发布** |
| `Warm` | 2 | **未发布** |
| `Artwork` | 3 | **未发布** |

### TaskbarInformationDensity

[源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L58)；JSON 无效输入回退：`Balanced`。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Minimal` | 0 | v1.2.0 |
| `Balanced` | 1 | v1.2.0 |
| `Information` | 2 | v1.2.0 |

### TaskbarLengthMode

[源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L89)；JSON 无效输入回退：`FollowContent`。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `FollowContent` | 0 | v1.2.0 |
| `Fixed` | 1 | v1.2.0 |

### TaskbarMediaTextAlignment

[源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L74)；JSON 无效输入回退：`Left`。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Left` | 0 | v1.2.0 |
| `Center` | 1 | v1.2.0 |
| `Right` | 2 | v1.2.0 |

### TaskbarRestComponent

[源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L100)；JSON 无效输入回退：`Artwork`。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Artwork` | 0 | v1.2.1 |
| `MediaText` | 1 | v1.2.1 |
| `Spectrum` | 2 | v1.2.1 |
| `Performance` | 3 | v1.2.1 |
| `OutputDevice` | 4 | v1.2.1 |
| `Volume` | 5 | v1.2.1 |

### TrackChangeNotificationPosition

[源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L375)；JSON 无效输入回退：`TopCenter`。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `BottomLeft` | 0 | v1.2.0 |
| `TopLeft` | 1 | v1.2.0 |
| `TopCenter` | 2 | v1.2.0 |
| `TopRight` | 3 | v1.2.0 |
| `BottomCenter` | 4 | v1.2.0 |
| `BottomRight` | 5 | v1.2.0 |

### TrayClickAction

[源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L22)；JSON 无效输入回退：`OpenAudioControl`。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `None` | 0 | v1.2.0 |
| `OpenSettings` | 1 | v1.2.0 |
| `OpenAudioControl` | 2 | v1.2.0 |
| `OpenContextMenu` | 3 | v1.2.0 |
| `OpenOutputDeviceMenu` | 4 | v1.2.0 |
| `OpenCurrentAppVolumeMenu` | 5 | v1.2.0 |

### TrayWheelBehavior

[源码](../../src/AFMediaBar/Classes/Settings/SettingsManager.cs#L16)；JSON 无效输入回退：`SwitchOutputDevice`。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `AdjustVolume` | 0 | v1.2.0 |
| `SwitchOutputDevice` | 1 | v1.2.0 |
| `Disabled` | 2 | v1.2.0 |

### WheelAction

[源码](../../src/AFMediaBar/Classes/Settings/ExperienceSettings.cs#L9)；JSON 无效输入回退：`PreviousNext`。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `PreviousNext` | 0 | v1.2.0 |
| `CurrentApplicationVolume` | 1 | v1.2.0 |
| `OutputDevice` | 2 | v1.2.0 |
| `SwitchMediaSource` | 3 | v1.2.0 |
| `Disabled` | 4 | v1.2.0 |

### WindowMode

[源码](../../src/AFMediaBar/Classes/Models/Layout/LayoutEnums.cs#L29)；JSON 无效输入回退：`Taskbar`。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Taskbar` | 0 | v1.2.0 |
| `DynamicIsland` | 1 | v1.2.0 |

## 范围、步长与模型常量

以下为当前模型公开常量的完整快照。实际归一化规则见兼容章节；有的范围只夹取、不按 UI 步长吸附，不能仅根据 Step 名称推定。

### AppSettings

| 常量 | 值 |
|---|---|
| `MinimumTaskbarCrossAxisOffsetDip` | `-20` |
| `MaximumTaskbarCrossAxisOffsetDip` | `20` |

### AppearanceSettings

| 常量 | 值 |
|---|---|
| `MinimumFontWeight` | `100` |
| `MaximumFontWeight` | `900` |
| `FontWeightStep` | `100` |
| `MinimumBackdropTintOpacityPercent` | `30` |
| `MaximumBackdropTintOpacityPercent` | `100` |
| `BackdropTintOpacityStep` | `5` |
| `DefaultBackdropTintOpacityPercent` | `60` |
| `MinimumTaskbarBackgroundOpacityPercent` | `35` |
| `MaximumTaskbarBackgroundOpacityPercent` | `90` |
| `TaskbarBackgroundOpacityStep` | `1` |
| `DefaultTaskbarBackgroundOpacityPercent` | `72` |
| `DefaultAccentColorHex` | `"#0078D4"` |

### LyricsCharacterSpacing

| 常量 | 值 |
|---|---|
| `MinimumPercent` | `0` |
| `MaximumPercent` | `20` |
| `StepPercent` | `1` |
| `DefaultPercent` | `0` |

### LyricsFixedWidth

| 常量 | 值 |
|---|---|
| `MinimumDip` | `80` |
| `MaximumDip` | `600` |
| `StepDip` | `10` |
| `DefaultDip` | `240` |

### LyricsLineGap

| 常量 | 值 |
|---|---|
| `MinimumPercent` | `0` |
| `MaximumPercent` | `24` |
| `StepPercent` | `2` |
| `DefaultPercent` | `0` |

### LyricsUnsungOpacity

| 常量 | 值 |
|---|---|
| `MinimumPercent` | `20` |
| `MaximumPercent` | `80` |
| `StepPercent` | `5` |
| `DefaultPercent` | `45` |

### PerformanceComponentSettings

| 常量 | 值 |
|---|---|
| `MinimumRefreshIntervalMilliseconds` | `500` |
| `MaximumRefreshIntervalMilliseconds` | `5000` |
| `DefaultRefreshIntervalMilliseconds` | `2500` |
| `RefreshIntervalStepMilliseconds` | `500` |

### SpectrumComponentSettings

| 常量 | 值 |
|---|---|
| `MinimumBandCount` | `9` |
| `MaximumBandCount` | `24` |
| `DefaultBandCount` | `9` |
| `MinimumRefreshRateHz` | `5` |
| `MaximumRefreshRateHz` | `30` |
| `MinimumSensitivityPercent` | `10` |
| `MaximumSensitivityPercent` | `400` |
| `MinimumContentHeightDip` | `14` |
| `MaximumContentHeightDip` | `34` |
| `ContentHeightStepDip` | `2` |
| `DefaultContentHeightDip` | `26` |
| `SensitivityStepPercent` | `10` |

### TaskbarExperienceSettings

| 常量 | 值 |
|---|---|
| `MinimumComponentSpacingDip` | `4` |
| `MaximumComponentSpacingDip` | `32` |
| `MaximumHoverButtonSpacingDip` | `16` |
| `MinimumMediaFontSizePercent` | `80` |
| `MaximumMediaFontSizePercent` | `125` |
| `MediaFontSizeStepPercent` | `5` |
| `MinimumStoredFixedLengthDip` | `120` |
| `MaximumStoredFixedLengthDip` | `4096` |

### TrackChangeNotificationSettings

| 常量 | 值 |
|---|---|
| `MinimumDurationMilliseconds` | `1000` |
| `MaximumDurationMilliseconds` | `10000` |

### UpdateSettings

| 常量 | 值 |
|---|---|
| `MaximumSkippedVersionLength` | `32` |

## 快照覆盖与验证

当前共有 138 个配置路径（含对象和列表条目模板）、48 个根级属性、35 类枚举、125 个枚举成员。与 v1.3.2 对比新增 7 个字段、15 个枚举成员；两个已发布叶子字段的内置默认值改变，已在前文记录。

字段与枚举来自真实 main 程序集的 System.Text.Json 元数据、实际私有枚举转换器及模型归一化；再以 C# 语法树独立核对字段集合。v1.3.2 使用 tag 源码的独立构建核对默认值，v1.2.0 至 v1.3.2 按发布 tag 核对路径及枚举成员。历史发布路径差集也已核对，唯一已发布后删除项为 lyricsMatchStrictness。读取边界使用真实 ReadEnvelope 与独立临时文件核对，不读取或写入用户配置。

执行 `pwsh -NoProfile -File tools/settings-contracts.ps1 -Action Verify` 核对清单结构、文档覆盖、发布 tag 及已核对源码指纹。它不运行应用、不构建、不访问网络，也不代替默认语义或桌面验收；源码指纹变化意味着需要重新调查并更新清单，不能只重写哈希消除提示。
