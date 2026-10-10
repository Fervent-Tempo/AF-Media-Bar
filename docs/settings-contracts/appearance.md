# 外观与基础表面设置契约

本页由机器清单和查询索引生成；只在修改本专题时读取。字段元数据改机器清单，关联规则改 query-index.json，再运行导出工具。

基线：main 09e9cf1；正式版 v1.3.2；schema 2；核对日 2026-10-10。

[简短入口](../settings-contracts.md)；[完整参考](reference.md)。对象默认看子字段；缺字段测试保留父对象，列表项是固定示例。可空声明不等于JSON一定拒绝null。

## 关联规则

- family=null 保留旧预设，空串跟随系统；Trim 后含逗号或超过200字符回空串。派生的 SelectedFontFamily 不序列化。
- 接受带/不带#的6位RGB和8位ARGB；规范化成大写#RRGGBB并丢弃alpha，非法值回#0078D4。
- 应用外观重置保留媒体文字色和任务栏背景；任务栏外观重置保留全局主题字体。旧 ResetAppearance 还覆盖布局，须核对实际调用方。
- 检测到TranslucentTB且未提示时可开启磨砂、先标记落盘再提示；ResetAll保留当前提示标记，用户默认快照不能重新触发。
- 新ResetScreenAndPlacement仅恢复目标、位置、padding、跨轴偏移、避让、锁定；比例、模式、barEnabled等旧字段须核对旧ResetLayout/DisplayModes，不能按页名推定。
- 当前只运行任务栏，WindowMode归一化为Taskbar；保留灵动岛字段和表面不等于该模式已实现。
- 这些旧字段仍写入文件。EnhancedReadability无呈现效果；托盘使用interaction映射，未通用转接根滚轮字段；删除前仍按发布证据判断。

## 字段

| JSON路径 | 类型 | 默认 | 缺失后值 | null读取 | 首次发布 |
|---|---|---|---|---|---|
| `settings.appearance` | `AppearanceSettings` | 对象，见子字段 | 对象，查询-Details | "读取失败：JsonException" | v1.2.0 |
| `settings.appearance.accentColor` | `string` | "#0078D4" | "#0078D4" | "#0078D4" | v1.2.0 |
| `settings.appearance.accentColorMode` | `AccentColorMode` | "System" | "System" | "System" | v1.2.0 |
| `settings.appearance.applicationThemeMode` | `ApplicationThemeMode` | "Automatic" | "Automatic" | "Automatic" | v1.2.0 |
| `settings.appearance.backdropMode` | `ApplicationBackdropMode` | "Mica" | "FluentSolid" | "Mica" | v1.2.0 |
| `settings.appearance.backdropTintOpacityPercent` | `int?` | 60 | 60 | 60 | v1.2.0 |
| `settings.appearance.cjkFont` | `CjkFontPreset` | "SystemDefault" | "SystemDefault" | "SystemDefault" | v1.2.0 |
| `settings.appearance.cjkFontFamily` | `string?` | null | null | null | v1.3.0 |
| `settings.appearance.enhancedReadability` | `bool` | false | false | "读取失败：JsonException" | v1.2.0 |
| `settings.appearance.fontWeight` | `int` | 400 | 100 | "读取失败：JsonException" | v1.2.0 |
| `settings.appearance.latinFont` | `LatinFontPreset` | "SystemDefault" | "SegoeUi" | "SystemDefault" | v1.2.0 |
| `settings.appearance.latinFontFamily` | `string?` | null | null | null | v1.3.0 |
| `settings.appearance.playerForegroundMode` | `PlayerForegroundMode` | "Automatic" | "Automatic" | "Automatic" | v1.2.0 |
| `settings.appearance.taskbarBackgroundMaterial` | `TaskbarBackgroundMaterial` | "Transparent" | "Transparent" | "Transparent" | **未发布** |
| `settings.appearance.taskbarBackgroundOpacityPercent` | `int?` | 72 | 72 | 72 | **未发布** |
| `settings.appearance.taskbarFrostedStyle` | `TaskbarFrostedStyle` | "Neutral" | "Neutral" | "Neutral" | **未发布** |
| `settings.dynamicIslandSurface` | `ModeSurfaceSettings` | 对象，见子字段 | 对象，查询-Details | "读取失败：JsonException" | v1.2.0 |
| `settings.dynamicIslandSurface.backgroundOpacityPercent` | `int` | 100 | 0 | "读取失败：JsonException" | v1.2.0 |
| `settings.dynamicIslandSurface.cornerRadiusDip` | `double` | 6 | 0 | "读取失败：JsonException" | v1.2.0 |
| `settings.dynamicIslandSurface.style` | `PlayerSurfaceStyle` | "Automatic" | "Automatic" | "Automatic" | v1.2.0 |
| `settings.taskbarSurface` | `ModeSurfaceSettings` | 对象，见子字段 | 对象，查询-Details | "读取失败：JsonException" | v1.2.0 |
| `settings.taskbarSurface.backgroundOpacityPercent` | `int` | 100 | 0 | "读取失败：JsonException" | v1.2.0 |
| `settings.taskbarSurface.cornerRadiusDip` | `double` | 6 | 0 | "读取失败：JsonException" | v1.2.0 |
| `settings.taskbarSurface.style` | `PlayerSurfaceStyle` | "Automatic" | "Automatic" | "Automatic" | v1.2.0 |

## 相关枚举

JSON无效项先采用类型回退，再归一化；成员发布状态独立于字段。

### AccentColorMode

JSON无效输入回退：System。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `System` | 0 | v1.2.0 |
| `Custom` | 1 | v1.2.0 |

### ApplicationThemeMode

JSON无效输入回退：Automatic。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Automatic` | 0 | v1.2.0 |
| `Light` | 1 | v1.2.0 |
| `Dark` | 2 | v1.2.0 |

### ApplicationBackdropMode

JSON无效输入回退：Mica。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `FluentSolid` | 0 | v1.2.0 |
| `Mica` | 1 | v1.2.0 |
| `Acrylic` | 2 | v1.2.0 |
| `MicaAlt` | 3 | v1.2.0 |

### CjkFontPreset

JSON无效输入回退：SystemDefault。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `SystemDefault` | 0 | v1.2.0 |
| `MicrosoftYaHei` | 1 | v1.2.0 |
| `DengXian` | 2 | v1.2.0 |
| `SimSun` | 3 | v1.2.0 |
| `SimHei` | 4 | v1.2.0 |
| `KaiTi` | 5 | v1.2.0 |
| `FangSong` | 6 | v1.2.0 |

### LatinFontPreset

JSON无效输入回退：SystemDefault。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `SegoeUi` | 0 | v1.2.0 |
| `Arial` | 1 | v1.2.0 |
| `Calibri` | 2 | v1.2.0 |
| `Verdana` | 3 | v1.2.0 |
| `Consolas` | 4 | v1.2.0 |
| `TimesNewRoman` | 5 | v1.2.0 |
| `SystemDefault` | 6 | v1.3.0 |

### PlayerForegroundMode

JSON无效输入回退：Automatic。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Automatic` | 0 | v1.2.0 |
| `LightText` | 1 | v1.2.0 |
| `DarkText` | 2 | v1.2.0 |

### TaskbarBackgroundMaterial

JSON无效输入回退：Transparent。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Transparent` | 0 | **未发布** |
| `Frosted` | 1 | **未发布** |

### TaskbarFrostedStyle

JSON无效输入回退：Neutral。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Neutral` | 0 | **未发布** |
| `Cool` | 1 | **未发布** |
| `Warm` | 2 | **未发布** |
| `Artwork` | 3 | **未发布** |

### PlayerSurfaceStyle

JSON无效输入回退：Automatic。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Automatic` | 0 | v1.2.0 |
| `Solid` | 1 | v1.2.0 |
| `ThemeTint` | 2 | v1.2.0 |

源码位置、原始/发布默认和范围：用 settings-contracts.ps1 -Action Query -Path 路径 -Details 查询。不要为查询一个字段全文读取JSON存档。
