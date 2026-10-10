# 歌词设置契约

本页由机器清单和查询索引生成；只在修改本专题时读取。字段元数据改机器清单，关联规则改 query-index.json，再运行导出工具。

基线：main 09e9cf1；正式版 v1.3.2；schema 2；核对日 2026-10-10。

[简短入口](../settings-contracts.md)；[完整参考](reference.md)。对象默认看子字段；缺字段测试保留父对象，列表项是固定示例。可空声明不等于JSON一定拒绝null。

## 关联规则

- ResetLyrics 保留歌词对齐与固定宽度，它们归内容布局重置；简繁、来源、分隔符等仍归歌词重置。
- null 全启用，空数组全关；保存未知ID，使用时忽略。按Ordinal去重，顺序不决定取词阶段。ID为Netease、NeteaseSearch、LRCLIB、QQMusic、Kugou、SodaMusic。
- order=null 使用下一句→翻译→音译；空数组关闭；显式列表保留优先级。JSON非法项先受枚举回退影响。
- 存储保留空白和换行；null由setter变为空串，空串关闭分割。

## 字段

| JSON路径 | 类型 | 默认 | 缺失后值 | null读取 | 首次发布 |
|---|---|---|---|---|---|
| `settings.allowBrowserAndVideoLyrics` | `bool` | false | false | "读取失败：JsonException" | v1.3.0 |
| `settings.lyricsArtistSeparators` | `string` | "/" | "/" | "" | v1.3.2 |
| `settings.lyricsCharacterSpacingPercent` | `int` | 0 | 0 | "读取失败：JsonException" | v1.3.0 |
| `settings.lyricsChineseConversion` | `LyricsChineseConversionMode` | "None" | "None" | "None" | **未发布** |
| `settings.lyricsEnabled` | `bool` | true | true | "读取失败：JsonException" | v1.2.0 |
| `settings.lyricsFixedWidthDip` | `int` | 240 | 240 | "读取失败：JsonException" | v1.3.0 |
| `settings.lyricsFixedWidthEnabled` | `bool` | false | false | "读取失败：JsonException" | v1.3.0 |
| `settings.lyricsInfoLineFilterEnabled` | `bool` | true | true | "读取失败：JsonException" | v1.2.0 |
| `settings.lyricsLineGapPercent` | `int` | 0 | 0 | "读取失败：JsonException" | v1.3.0 |
| `settings.lyricsSecondaryLine` | `LyricsSecondaryLineSettings` | 对象，见子字段 | 对象，查询-Details | "读取失败：JsonException" | v1.2.0 |
| `settings.lyricsSecondaryLine.order` | `IReadOnlyList<LyricsSecondaryLineMode>?` | null | null | null | v1.2.0 |
| `settings.lyricsSource` | `LyricsSourceSettings` | 对象，见子字段 | 对象，查询-Details | "读取失败：JsonException" | v1.2.0 |
| `settings.lyricsSource.enabledSourceIds` | `IReadOnlyList<string>?` | null | null | null | v1.2.0 |
| `settings.lyricsSyllableHighlightEnabled` | `bool` | true | true | "读取失败：JsonException" | v1.2.0 |
| `settings.lyricsTextAlignment` | `LyricsTextAlignment` | "Center" | "Center" | "Center" | v1.2.0 |
| `settings.lyricsUnsungOpacityPercent` | `int` | 45 | 45 | "读取失败：JsonException" | v1.2.0 |
| `settings.twoLineLyricsEnabled` | `bool` | true | true | "读取失败：JsonException" | v1.2.0 |

## 相关枚举

JSON无效项先采用类型回退，再归一化；成员发布状态独立于字段。

### LyricsChineseConversionMode

JSON无效输入回退：None。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `None` | 0 | **未发布** |
| `SimplifiedToTraditional` | 1 | **未发布** |
| `TraditionalToSimplified` | 2 | **未发布** |

### LyricsSecondaryLineMode

JSON无效输入回退：Translation。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `NextLine` | 0 | v1.2.0 |
| `Translation` | 1 | v1.2.0 |
| `Romanization` | 2 | v1.2.0 |

### LyricsTextAlignment

JSON无效输入回退：Center。

| 名称 | 数值 | 首次发布 |
|---|---|---|
| `Left` | 0 | v1.2.0 |
| `Center` | 1 | v1.2.0 |
| `Right` | 2 | v1.2.0 |

源码位置、原始/发布默认和范围：用 query-settings-contracts.ps1 -Path 路径 -Details 查询。不要为查询一个字段全文读取JSON存档。
