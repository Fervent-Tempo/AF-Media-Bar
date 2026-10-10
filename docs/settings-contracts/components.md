# 频谱与性能设置契约

本页由机器清单和查询索引生成；只在修改本专题时读取。字段元数据改机器清单，关联规则改 query-index.json，再运行导出工具。

基线：main 09e9cf1；正式版 v1.3.2；schema 2；核对日 2026-10-10。

[简短入口](../settings-contracts.md)；[完整参考](reference.md)。对象默认看子字段；缺字段测试保留父对象，列表项是固定示例。可空声明不等于JSON一定拒绝null。

## 关联规则

- 频谱及性能由 ResetComponents/ResetContentLayout 恢复。性能metrics空或全无效回SystemMemory；模型范围和吸附规则按所属类型核对，UI步长不保证都在Normalize吸附。

## 字段

| JSON路径 | 类型 | 默认 | 缺失后值 | null读取 | 首次发布 |
|---|---|---|---|---|---|
| `settings.performanceComponent` | `PerformanceComponentSettings` | 对象，见子字段 | 对象，查询-Details | "读取失败：JsonException" | v1.2.0 |
| `settings.performanceComponent.metrics` | `IReadOnlyList<MetricKind>?` | ["SystemMemory"] | ["SystemMemory"] | ["SystemMemory"] | v1.2.0 |
| `settings.performanceComponent.openTaskManagerOnClick` | `bool` | true | false | "读取失败：JsonException" | v1.2.0 |
| `settings.performanceComponent.refreshIntervalMilliseconds` | `int` | 2500 | 500 | "读取失败：JsonException" | v1.2.0 |
| `settings.spectrumComponent` | `SpectrumComponentSettings` | 对象，见子字段 | 对象，查询-Details | "读取失败：JsonException" | v1.2.0 |
| `settings.spectrumComponent.bandCount` | `int` | 9 | 9 | "读取失败：JsonException" | v1.2.0 |
| `settings.spectrumComponent.contentHeightDip` | `double` | 26 | 14 | "读取失败：JsonException" | v1.2.0 |
| `settings.spectrumComponent.refreshRateHz` | `int` | 20 | 5 | "读取失败：JsonException" | v1.2.0 |
| `settings.spectrumComponent.sensitivityPercent` | `int` | 100 | 10 | "读取失败：JsonException" | v1.2.0 |
| `settings.spectrumComponent.style` | `SpectrumStyle` | "Bars" | "Bars" | "Bars" | v1.2.0 |

## 相关枚举

JSON无效项先采用类型回退，再归一化；成员发布状态独立于字段。

### MetricKind

JSON无效输入回退：SystemMemory。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `SystemMemory` | 0 | v1.2.0 |
| `SystemCpu` | 1 | v1.2.0 |
| `SystemGpu` | 2 | v1.2.0 |
| `ProcessMemory` | 3 | v1.2.0 |

### SpectrumStyle

JSON无效输入回退：Bars。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Bars` | 0 | v1.2.0 |
| `Waveform` | 1 | v1.2.0 |
| `PixelBars` | 2 | v1.2.0 |
| `MirroredBars` | 3 | v1.2.0 |

源码位置、原始/发布默认和范围：用 settings-contracts.ps1 -Action Query -Path 路径 -Details 查询。不要为查询一个字段全文读取JSON存档。
