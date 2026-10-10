# 任务栏内容与布局设置契约

本页由机器清单和查询索引生成；只在修改本专题时读取。字段元数据改机器清单，关联规则改 query-index.json，再运行导出工具。

基线：main 6ba5cf2；正式版 v1.3.2；schema 2；核对日 2026-10-10。

[简短入口](../settings-contracts.md)；[完整参考](reference.md)。对象默认看子字段；缺字段测试保留父对象，列表项是固定示例。可空声明不等于JSON一定拒绝null。

## 关联规则

- ResetContentLayout 恢复任务栏体验但保留媒体字号；字号由 ResetTaskbarAppearance 恢复。旧 ResetDisplayModes 仍会恢复组件显隐等，不等于当前页面重置；两者均保留交互中的封面悬停模式。
- null 时 position=End 解析为 Right，其余为 Left；显式值优先。
- 四项全关时回 Compact：媒体信息和控制开启，音频与性能关闭；不可只孤立判断一个开关。
- null 用默认顺序；封面和媒体文字固定在前，只排序尾部。遗漏的已知尾部会补全，因此空数组不表示隐藏组件。
- null 默认只留 Artwork（无媒体时指快速启动音符）；空数组全部隐藏；显式列表控制无媒体保留项。封面显隐不能替代它。

## 字段

| JSON路径 | 类型 | 默认 | 缺失后值 | null读取 | 首次发布 |
|---|---|---|---|---|---|
| `settings.taskbarExperience` | `TaskbarExperienceSettings` | 对象，见子字段 | 对象，查询-Details | "读取失败：JsonException" | v1.2.0 |
| `settings.taskbarExperience.arrangement` | `TaskbarContentArrangement?` | null | null | null | **未发布** |
| `settings.taskbarExperience.artworkVisible` | `bool` | true | true | "读取失败：JsonException" | v1.3.0 |
| `settings.taskbarExperience.componentSpacingDip` | `double` | 12 | 4 | "读取失败：JsonException" | v1.2.0 |
| `settings.taskbarExperience.contentLayout` | `TaskbarContentLayout` | "AdaptiveStack" | "CompactInline" | "AdaptiveStack" | v1.2.0 |
| `settings.taskbarExperience.density` | `TaskbarInformationDensity` | "Balanced" | "Minimal" | "Balanced" | v1.2.0 |
| `settings.taskbarExperience.fixedLengthDip` | `double` | 360 | 360 | "读取失败：JsonException" | v1.2.0 |
| `settings.taskbarExperience.fullLayerEnabled` | `bool` | true | false | "读取失败：JsonException" | v1.2.0 |
| `settings.taskbarExperience.fullPanel` | `TaskbarFullPanelSettings` | 对象，见子字段 | 对象，查询-Details | "读取失败：JsonException" | v1.2.0 |
| `settings.taskbarExperience.fullPanel.audioControlsVisible` | `bool` | true | false | "读取失败：JsonException" | v1.2.0 |
| `settings.taskbarExperience.fullPanel.mediaControlsVisible` | `bool` | true | false | "读取失败：JsonException" | v1.2.0 |
| `settings.taskbarExperience.fullPanel.mediaInfoVisible` | `bool` | true | false | "读取失败：JsonException" | v1.2.0 |
| `settings.taskbarExperience.fullPanel.performanceVisible` | `bool` | true | false | "读取失败：JsonException" | v1.2.0 |
| `settings.taskbarExperience.fullPanelEntryVisible` | `bool` | true | false | "读取失败：JsonException" | v1.2.0 |
| `settings.taskbarExperience.hoverButtonSpacingDip` | `double` | 2 | 0 | "读取失败：JsonException" | v1.3.0 |
| `settings.taskbarExperience.hoverControls` | `TaskbarHoverControlsSettings` | 对象，见子字段 | 对象，查询-Details | "读取失败：JsonException" | v1.2.0 |
| `settings.taskbarExperience.hoverControls.audioControlVisible` | `bool` | true | false | "读取失败：JsonException" | v1.2.0 |
| `settings.taskbarExperience.hoverControls.outputDeviceVisible` | `bool` | true | false | "读取失败：JsonException" | v1.2.0 |
| `settings.taskbarExperience.hoverControls.playPauseVisible` | `bool` | false | false | "读取失败：JsonException" | v1.2.0 |
| `settings.taskbarExperience.hoverControls.previousNextVisible` | `bool` | false | false | "读取失败：JsonException" | v1.2.0 |
| `settings.taskbarExperience.hoverControls.progressVisible` | `bool` | true | false | "读取失败：JsonException" | v1.2.0 |
| `settings.taskbarExperience.hoverLayerEnabled` | `bool` | true | false | "读取失败：JsonException" | v1.2.0 |
| `settings.taskbarExperience.idleComponents` | `IReadOnlyList<TaskbarRestComponent>?` | null | null | null | v1.2.1 |
| `settings.taskbarExperience.lengthMode` | `TaskbarLengthMode` | "FollowContent" | "FollowContent" | "FollowContent" | v1.2.0 |
| `settings.taskbarExperience.mediaFontSizePercent` | `int` | 100 | 100 | "读取失败：JsonException" | v1.2.0 |
| `settings.taskbarExperience.mediaTextAlignment` | `TaskbarMediaTextAlignment` | "Center" | "Left" | "Left" | v1.2.0 |
| `settings.taskbarExperience.outputDeviceVisible` | `bool` | false | false | "读取失败：JsonException" | v1.2.1 |
| `settings.taskbarExperience.performanceVisible` | `bool` | true | false | "读取失败：JsonException" | v1.2.0 |
| `settings.taskbarExperience.restComponentOrder` | `IReadOnlyList<TaskbarRestComponent>?` | null | null | null | v1.2.1 |
| `settings.taskbarExperience.restProgressVisible` | `bool` | false | false | "读取失败：JsonException" | v1.2.0 |
| `settings.taskbarExperience.spectrumVisible` | `bool` | true | false | "读取失败：JsonException" | v1.2.0 |
| `settings.taskbarExperience.volumeVisible` | `bool` | false | false | "读取失败：JsonException" | v1.2.1 |

## 相关枚举

JSON无效项先采用类型回退，再归一化；成员发布状态独立于字段。

### TaskbarContentArrangement

JSON无效输入回退：Left。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Left` | 0 | **未发布** |
| `Right` | 1 | **未发布** |

### TaskbarContentLayout

JSON无效输入回退：AdaptiveStack。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `CompactInline` | 0 | v1.2.0 |
| `AdaptiveStack` | 1 | v1.2.0 |
| `CenteredStack` | 2 | v1.2.0 |

### TaskbarInformationDensity

JSON无效输入回退：Balanced。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Minimal` | 0 | v1.2.0 |
| `Balanced` | 1 | v1.2.0 |
| `Information` | 2 | v1.2.0 |

### TaskbarRestComponent

JSON无效输入回退：Artwork。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Artwork` | 0 | v1.2.1 |
| `MediaText` | 1 | v1.2.1 |
| `Spectrum` | 2 | v1.2.1 |
| `Performance` | 3 | v1.2.1 |
| `OutputDevice` | 4 | v1.2.1 |
| `Volume` | 5 | v1.2.1 |

### TaskbarLengthMode

JSON无效输入回退：FollowContent。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `FollowContent` | 0 | v1.2.0 |
| `Fixed` | 1 | v1.2.0 |

### TaskbarMediaTextAlignment

JSON无效输入回退：Left。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Left` | 0 | v1.2.0 |
| `Center` | 1 | v1.2.0 |
| `Right` | 2 | v1.2.0 |

源码位置、原始/发布默认和范围：用 settings-contracts.ps1 -Action Query -Path 路径 -Details 查询。不要为查询一个字段全文读取JSON存档。
