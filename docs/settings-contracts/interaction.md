# 交互与托盘设置契约

本页由机器清单和查询索引生成；只在修改本专题时读取。字段元数据改机器清单，关联规则改 query-index.json，再运行导出工具。

基线：main 09e9cf1；正式版 v1.3.2；schema 2；核对日 2026-10-10。

[简短入口](../settings-contracts.md)；[完整参考](reference.md)。对象默认看子字段；缺字段测试保留父对象，列表项是固定示例。可空声明不等于JSON一定拒绝null。

## 关联规则

- ResetInteraction 恢复整段。托盘点击仅接受模型白名单，其他已定义旧值也可回默认；两个滚轮字段不要用根级旧字段代替。
- 这些旧字段仍写入文件。EnhancedReadability无呈现效果；托盘使用interaction映射，未通用转接根滚轮字段；删除前仍按发布证据判断。

## 字段

| JSON路径 | 类型 | 默认 | 缺失后值 | null读取 | 首次发布 |
|---|---|---|---|---|---|
| `settings.interaction` | `GlobalInteractionSettings` | 对象，见子字段 | 对象，查询-Details | "读取失败：JsonException" | v1.2.0 |
| `settings.interaction.artworkClickAction` | `PlayerClickAction` | "TogglePlayPause" | "TogglePlayPause" | "TogglePlayPause" | v1.2.0 |
| `settings.interaction.chordWheelAction` | `WheelAction` | "SwitchMediaSource" | "PreviousNext" | "PreviousNext" | v1.2.0 |
| `settings.interaction.modifier` | `InteractionModifier` | "Shift" | "Shift" | "Shift" | v1.2.0 |
| `settings.interaction.primaryWheelAction` | `WheelAction` | "PreviousNext" | "PreviousNext" | "PreviousNext" | v1.2.0 |
| `settings.interaction.showWheelTooltips` | `bool` | true | false | "读取失败：JsonException" | v1.3.0 |
| `settings.interaction.textClickAction` | `PlayerClickAction` | "ActivateSource" | "TogglePlayPause" | "TogglePlayPause" | v1.2.0 |
| `settings.interaction.trayChordWheelAction` | `TrayWheelBehavior` | "AdjustVolume" | "AdjustVolume" | "SwitchOutputDevice" | v1.2.0 |
| `settings.interaction.trayClickAction` | `TrayClickAction` | "OpenAudioControl" | "None" | "OpenAudioControl" | v1.2.0 |
| `settings.interaction.trayPrimaryWheelAction` | `TrayWheelBehavior` | "SwitchOutputDevice" | "AdjustVolume" | "SwitchOutputDevice" | v1.2.0 |
| `settings.trayWheelBehavior` | `TrayWheelBehavior` | "SwitchOutputDevice" | "SwitchOutputDevice" | "SwitchOutputDevice" | v1.2.0 |

## 相关枚举

JSON无效项先采用类型回退，再归一化；成员发布状态独立于字段。

### PlayerClickAction

JSON无效输入回退：TogglePlayPause。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `TogglePlayPause` | 0 | v1.2.0 |
| `ActivateSource` | 1 | v1.2.0 |
| `OpenFullPanel` | 2 | v1.2.0 |
| `Disabled` | 3 | v1.2.0 |

### WheelAction

JSON无效输入回退：PreviousNext。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `PreviousNext` | 0 | v1.2.0 |
| `CurrentApplicationVolume` | 1 | v1.2.0 |
| `OutputDevice` | 2 | v1.2.0 |
| `SwitchMediaSource` | 3 | v1.2.0 |
| `Disabled` | 4 | v1.2.0 |

### InteractionModifier

JSON无效输入回退：Shift。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Shift` | 0 | v1.2.0 |
| `LeftMouseButton` | 1 | v1.2.0 |
| `RightMouseButton` | 2 | v1.2.0 |

### TrayWheelBehavior

JSON无效输入回退：SwitchOutputDevice。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `AdjustVolume` | 0 | v1.2.0 |
| `SwitchOutputDevice` | 1 | v1.2.0 |
| `Disabled` | 2 | v1.2.0 |

### TrayClickAction

JSON无效输入回退：OpenAudioControl。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `None` | 0 | v1.2.0 |
| `OpenSettings` | 1 | v1.2.0 |
| `OpenAudioControl` | 2 | v1.2.0 |
| `OpenContextMenu` | 3 | v1.2.0 |
| `OpenOutputDeviceMenu` | 4 | v1.2.0 |
| `OpenCurrentAppVolumeMenu` | 5 | v1.2.0 |

源码位置、原始/发布默认和范围：用 settings-contracts.ps1 -Action Query -Path 路径 -Details 查询。不要为查询一个字段全文读取JSON存档。
