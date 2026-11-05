<!-- owner: AF Media Bar maintainers | verified_at: 791d2319 | review_by: 2027-03-05 -->

# 设置与持久化

修改设置字段、默认值、读取、重置、兼容、持久化或设置页组织时读本文。字段发布状态以 [settings-contracts.md](../settings-contracts.md) 和正式 release/tag 为准。

## 模型与持久化

- `AppSettings` 是可变、逐属性通知的根模型；各设置段使用不可变记录并 `Normalize()`。`SettingsManager` 只管理内存、领域事件和重置，不自行做文件 I/O。
- 模型写入在 UI 线程完成，因为事件会同步更新绑定集合。磁盘工作归 `SettingsPersistenceService`，保存采用防抖、临时文件、备份和原子替换；退出前 `Flush()` 落盘。
- 数据位于 `%LOCALAPPDATA%\AFMediaBar`：`settings.json`、`settings.json.bak`、`user-defaults.json`。schema 只在发布批次调整；损坏或不支持编号的主文件隔离后尝试有效备份，再回内置默认。
- 有效文件加载后不立即重写；备份同样通过编号与内容校验。旧几何字段的显式 `null` 需要兼容读取再归一化。
- 保存失败必须保留可恢复数据并提供可信反馈；临时文件要清理，不能把失败写成成功。

## 兼容与语义

- 未发布字段不为开发中间构建保留兼容；已发布字段变更必须说明旧数据处理、用户影响和验证，不要求一律迁移。不能把进入 main 当成已发布。
- 新增字段在声明处提供默认值并归一化；保留 `null`、空集合和显式值的不同语义，不能随意合并。
- `LyricsSource.EnabledSourceIds`：`null` 全部默认来源，空数组全关；排列不代表取词优先级，手动顺序另存 `PrioritySourceIds`。
- `LyricsSecondaryLineSettings.Order`：`null` 使用“下一句 → 翻译 → 音译”，空数组不用第二行，非空按顺序选择有内容的项。
- `LatinFontFamily` / `CjkFontFamily`：`null` 保留旧预设含义，空字符串跟随系统，非空为本机字体名；归一化去除首尾空白并拒绝逗号或过长值。
- `RestComponentOrder`：封面与媒体文字是固定前缀，只排尾部；遗漏的已知组件补到末尾，等于默认尾部时写回 `null`。
- `IdleComponents`：`null` 默认仅快速启动小音符，空列表使无媒体时整条隐藏；显隐表与顺序表不能合并。
- 通知新增偏移、标题/歌手字号与颜色、拖动定位、预设色和试发；已有合法值和用户默认快照继续沿用，非法值回默认。

## 重置归属

- 重置只修改所属范围，不能顺手改相邻页面。`ResetApplicationAppearance` 管全局主题与字体，保留媒体文字色、任务栏背景和小音符颜色；`ResetTaskbarAppearance` 管媒体文字色、任务栏背景、小音符颜色和媒体字号，保留全局主题字体。
- `ResetContentLayout` 管任务栏体验（保留媒体字号）、频谱/性能、歌词对齐和固定宽度；`ResetScreenAndPlacement` 管显示目标、位置、留白、跨轴偏移、避让和锁定。
- `ResetExtraFeatures` 管曲目通知、媒体过滤和快速启动；`ResetInteraction` 与 `ResetLyrics` 分别恢复交互段和歌词取词/呈现偏好；`ResetTrackNotifications` 只恢复通知段。
- 有用户默认快照时所有重置以快照为准；无快照时用内置默认。未实现模式只改页面选择，不写运行模式或重建宿主。
- 旧 `ResetAppearance`、`ResetDisplayModes`、`ResetLayout`、`ResetComponents` 仍可能存在，不等于当前页面入口；按实际调用方判断。

## 设置页

- 导航由 `SettingsPageCatalog` 生成；当前是显示与位置、内容与布局、外观、歌词、交互、媒体与通知、应用七个分区，窗口默认进入 QuickStart，显式导航决定最终落地页。
- 页面通过 `SettingsPageProvider` 解析，`SettingsPageScopeCache` 拥有独立 DI 作用域；全局页面按键缓存，模式页面按环境缓存。离开上下文先取消请求和延迟写入，旧上下文不得写模型。
- 搜索按当前语言和上下文过滤稳定目标，跳过环境隐藏的分组；命中后导航、展开、滚动并高亮，不叠加第二次过滤。
- 任务栏与悬浮模式目前共享持久化；缓存、草稿和取消令牌隔离不等于配置隔离，不能宣称独立 profile 已实现。
- 界面文案使用现有本地化资源并检查全部支持语言；深色/高对比度、长文本、前置条件和至少保留一项等实际条件不能因精简文案被删。

## 验证

用兼容基线的主设置与用户默认快照验证读取、缺少新增字段、旧 JSON、非法值回退、重置范围、保存失败和退出落盘。真实 Windows 还要验证开机登记、磁盘失败和重启恢复；自动测试不能证明这些场景。
