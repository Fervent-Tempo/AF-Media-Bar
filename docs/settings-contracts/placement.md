# 显示目标与旧定位字段设置契约

本页由机器清单和查询索引生成；只在修改本专题时读取。字段元数据改机器清单，关联规则改 query-index.json，再运行导出工具。

基线：main 09e9cf1；正式版 v1.3.2；schema 2；核对日 2026-10-10。

[简短入口](../settings-contracts.md)；[完整参考](reference.md)。对象默认看子字段；缺字段测试保留父对象，列表项是固定示例。可空声明不等于JSON一定拒绝null。

## 关联规则

- 列表为空且旧普通单目标非空时迁入列表并清旧字段；旧单目标AllTaskbars保留全部当前目标语义。离线ID保留，全部离线时临时回主屏。
- 新ResetScreenAndPlacement仅恢复目标、位置、padding、跨轴偏移、避让、锁定；比例、模式、barEnabled等旧字段须核对旧ResetLayout/DisplayModes，不能按页名推定。
- 五项旧null预处理只识别标准camelCase envelope；大写SETTINGS下非可空几何null仍可导致读取失败。
- 当前只运行任务栏，WindowMode归一化为Taskbar；保留灵动岛字段和表面不等于该模式已实现。
- 这些旧字段仍写入文件。EnhancedReadability无呈现效果；托盘使用interaction映射，未通用转接根滚轮字段；删除前仍按发布证据判断。

## 字段

| JSON路径 | 类型 | 默认 | 缺失后值 | null读取 | 首次发布 |
|---|---|---|---|---|---|
| `settings.dynamicIslandBackgroundMode` | `DynamicIslandBackgroundMode` | "SystemTheme" | "SystemTheme" | "SystemTheme" | v1.2.0 |
| `settings.dynamicIslandEdge` | `DynamicIslandEdge` | "Top" | "Top" | "Top" | v1.2.0 |
| `settings.dynamicIslandEdgeDocked` | `bool` | true | true | "读取失败：JsonException" | v1.2.0 |
| `settings.dynamicIslandLeft` | `double?` | null | null | null | v1.2.0 |
| `settings.dynamicIslandTop` | `double?` | null | null | null | v1.2.0 |
| `settings.layoutLengthScalePercent` | `double` | 100 | 100 | 100 | v1.2.0 |
| `settings.layoutOrientationMode` | `LayoutOrientationMode` | "Auto" | "Auto" | "Auto" | v1.2.0 |
| `settings.layoutThicknessScalePercent` | `double` | 100 | 100 | 100 | v1.2.0 |
| `settings.position` | `TaskbarBarPosition` | "Start" | "Start" | "Start" | v1.2.0 |
| `settings.taskbarBarAvoidIcons` | `bool` | true | true | "读取失败：JsonException" | v1.2.0 |
| `settings.taskbarBarBackgroundBlur` | `bool` | false | false | "读取失败：JsonException" | v1.2.0 |
| `settings.taskbarBarCrossAxisOffsetDip` | `double` | 0 | 0 | 0 | v1.2.0 |
| `settings.taskbarBarEnabled` | `bool` | true | true | "读取失败：JsonException" | v1.2.0 |
| `settings.taskbarBarManualPadding` | `int` | 0 | 0 | "读取失败：JsonException" | v1.2.0 |
| `settings.taskbarBarPositionLocked` | `bool` | false | false | "读取失败：JsonException" | v1.2.0 |
| `settings.taskbarTargetMonitorDeviceId` | `string?` | null | null | null | v1.2.0 |
| `settings.taskbarTargetMonitorDeviceIds` | `IReadOnlyList<string>?` | null | null | null | v1.2.1 |
| `settings.windowMode` | `WindowMode` | "Taskbar" | "Taskbar" | "Taskbar" | v1.2.0 |

## 相关枚举

JSON无效项先采用类型回退，再归一化；成员发布状态独立于字段。

### DynamicIslandBackgroundMode

JSON无效输入回退：SystemTheme。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `SystemTheme` | 0 | v1.2.0 |
| `Transparent` | 1 | v1.2.0 |

### DynamicIslandEdge

JSON无效输入回退：Top。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Top` | 0 | v1.2.0 |
| `Right` | 1 | v1.2.0 |
| `Bottom` | 2 | v1.2.0 |
| `Left` | 3 | v1.2.0 |

### LayoutOrientationMode

JSON无效输入回退：Auto。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Auto` | 0 | v1.2.0 |
| `Horizontal` | 1 | v1.2.0 |
| `Vertical` | 2 | v1.2.0 |

### TaskbarBarPosition

JSON无效输入回退：Start。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Start` | 0 | v1.2.0 |
| `Center` | 1 | v1.2.0 |
| `End` | 2 | v1.2.0 |

### WindowMode

JSON无效输入回退：Taskbar。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Taskbar` | 0 | v1.2.0 |
| `DynamicIsland` | 1 | v1.2.0 |

源码位置、原始/发布默认和范围：用 settings-contracts.ps1 -Action Query -Path 路径 -Details 查询。不要为查询一个字段全文读取JSON存档。
