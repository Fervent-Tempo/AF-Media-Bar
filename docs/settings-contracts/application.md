# 应用、更新与启动状态设置契约

本页由机器清单和查询索引生成；只在修改本专题时读取。字段元数据改机器清单，关联规则改 query-index.json，再运行导出工具。

基线：main 09e9cf1；正式版 v1.3.2；schema 2；核对日 2026-10-10。

[简短入口](../settings-contracts.md)；[完整参考](reference.md)。对象默认看子字段；缺字段测试保留父对象，列表项是固定示例。可空声明不等于JSON一定拒绝null。

## 关联规则

- 跳过版本Trim、空回null、最长32字符；未来检查时间由调度按到期处理。检查记录是状态字段，没有自动下载开关。
- 启动先读实际注册/Windows禁用状态同步；未知时保留意图。只有用户主动开启才允许解除禁用，失败回实际状态或上次值。
- 检测到TranslucentTB且未提示时可开启磨砂、先标记落盘再提示；ResetAll保留当前提示标记，用户默认快照不能重新触发。
- System保存选择而非生效语言：zh的Hant/TW/HK/MO/CHT为繁体，其他中文为简体；vi为越南语，其他或未知为英文。越南语成员尚未发布。

## 字段

| JSON路径 | 类型 | 默认 | 缺失后值 | null读取 | 首次发布 |
|---|---|---|---|---|---|
| `settings.interfaceLanguage` | `InterfaceLanguage` | "System" | "System" | "System" | v1.2.0 |
| `settings.launchAtStartup` | `bool` | true | true | "读取失败：JsonException" | v1.2.0 |
| `settings.translucentTbCompatibilityPromptShown` | `bool` | false | false | "读取失败：JsonException" | **未发布** |
| `settings.update` | `UpdateSettings` | 对象，见子字段 | 对象，查询-Details | "读取失败：JsonException" | v1.2.0 |
| `settings.update.autoCheckEnabled` | `bool` | true | false | "读取失败：JsonException" | v1.2.0 |
| `settings.update.lastCheckSucceeded` | `bool?` | null | null | null | v1.2.0 |
| `settings.update.lastCheckUtc` | `DateTimeOffset?` | null | null | null | v1.2.0 |
| `settings.update.skippedVersion` | `string?` | null | null | null | v1.2.0 |

## 相关枚举

JSON无效项先采用类型回退，再归一化；成员发布状态独立于字段。

### InterfaceLanguage

JSON无效输入回退：System。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `System` | 0 | v1.2.0 |
| `SimplifiedChinese` | 1 | v1.2.0 |
| `TraditionalChinese` | 2 | v1.2.0 |
| `English` | 3 | v1.2.0 |
| `Vietnamese` | 4 | **未发布** |

源码位置、原始/发布默认和范围：用 settings-contracts.ps1 -Action Query -Path 路径 -Details 查询。不要为查询一个字段全文读取JSON存档。
