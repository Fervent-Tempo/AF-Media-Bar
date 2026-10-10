# 媒体、通知与快速启动设置契约

本页由机器清单和查询索引生成；只在修改本专题时读取。字段元数据改机器清单，关联规则改 query-index.json，再运行导出工具。

基线：main 09e9cf1；正式版 v1.3.2；schema 2；核对日 2026-10-10。

[简短入口](../settings-contracts.md)；[完整参考](reference.md)。对象默认看子字段；缺字段测试保留父对象，列表项是固定示例。可空声明不等于JSON一定拒绝null。

## 关联规则

- enabled=false 不过滤；true且空列表不允许任何来源。列表Trim、忽略大小写去重并排序，使用时归一化媒体别名。
- entries=null→空；空目标或代码非法Kind过滤；按Kind+Target忽略大小写去重。空Id生成GUID，空名称取文件名，SourceId空白回null。JSON非法Kind可能先回Executable。
- 媒体ID是开放的SMTC集合。网易云SourceId=cloudmusic、选择键source:cloudmusic；cloudmusic/netease/163music别名不区分大小写，与歌词来源ID不是同一契约。
- 通知由媒体与通知重置；时长夹1000–10000ms。固定显示器丢失时运行回退，不清除用户选择。

## 字段

| JSON路径 | 类型 | 默认 | 缺失后值 | null读取 | 首次发布 |
|---|---|---|---|---|---|
| `settings.quickLaunch` | `QuickLaunchSettings` | 对象，见子字段 | 对象，查询-Details | "读取失败：JsonException" | v1.2.0 |
| `settings.quickLaunch.entries` | `IReadOnlyList<QuickLaunchEntry>?` | [] | [] | [] | v1.2.0 |
| `settings.quickLaunch.entries[].displayName` | `string` | 条目模板 | "fixture" | "fixture" | v1.2.0 |
| `settings.quickLaunch.entries[].id` | `string` | 条目模板 | "生成N格式GUID" | "生成N格式GUID" | v1.2.0 |
| `settings.quickLaunch.entries[].kind` | `QuickLaunchTargetKind` | 条目模板 | "Executable" | "Executable" | v1.2.0 |
| `settings.quickLaunch.entries[].sourceId` | `string?` | 条目模板 | null | null | v1.2.0 |
| `settings.quickLaunch.entries[].target` | `string` | 条目模板 | "条目被过滤" | "条目被过滤" | v1.2.0 |
| `settings.smtcSourceFilter` | `SmtcSourceFilterSettings` | 对象，见子字段 | 对象，查询-Details | "读取失败：JsonException" | v1.2.0 |
| `settings.smtcSourceFilter.allowedSourceIds` | `IReadOnlyList<string>?` | [] | [] | [] | v1.2.0 |
| `settings.smtcSourceFilter.enabled` | `bool` | false | false | "读取失败：JsonException" | v1.2.0 |
| `settings.trackChangeNotification` | `TrackChangeNotificationSettings` | 对象，见子字段 | 对象，查询-Details | "读取失败：JsonException" | v1.2.0 |
| `settings.trackChangeNotification.durationMilliseconds` | `int` | 3000 | 1000 | "读取失败：JsonException" | v1.2.0 |
| `settings.trackChangeNotification.enabled` | `bool` | true | false | "读取失败：JsonException" | v1.2.0 |
| `settings.trackChangeNotification.fixedMonitorDeviceId` | `string?` | null | null | null | v1.2.0 |
| `settings.trackChangeNotification.position` | `TrackChangeNotificationPosition` | "TopCenter" | "BottomLeft" | "TopCenter" | v1.2.0 |
| `settings.trackChangeNotification.showWhenFullscreen` | `bool` | true | false | "读取失败：JsonException" | v1.2.0 |
| `settings.trackChangeNotification.targetMode` | `NotificationTargetMode` | "ForegroundWindow" | "Fixed" | "ForegroundWindow" | v1.2.0 |

## 相关枚举

JSON无效项先采用类型回退，再归一化；成员发布状态独立于字段。

### QuickLaunchTargetKind

JSON无效输入回退：Executable。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Executable` | 0 | v1.2.0 |
| `Shortcut` | 1 | v1.2.0 |
| `AppUserModelId` | 2 | v1.2.0 |

### TrackChangeNotificationPosition

JSON无效输入回退：TopCenter。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `BottomLeft` | 0 | v1.2.0 |
| `TopLeft` | 1 | v1.2.0 |
| `TopCenter` | 2 | v1.2.0 |
| `TopRight` | 3 | v1.2.0 |
| `BottomCenter` | 4 | v1.2.0 |
| `BottomRight` | 5 | v1.2.0 |

### NotificationTargetMode

JSON无效输入回退：ForegroundWindow。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Fixed` | 0 | v1.2.0 |
| `ForegroundWindow` | 1 | v1.2.0 |

源码位置、原始/发布默认和范围：用 query-settings-contracts.ps1 -Path 路径 -Details 查询。不要为查询一个字段全文读取JSON存档。
