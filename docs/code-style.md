# AF Media Bar 代码风格规范

> 本文统一注释、命名与基本格式；适用于手写代码，生成代码和外部接口保留其约定。新代码遵循本规范，旧代码随相关改动逐步整理，不批量重命名或格式化。

## 注释

- 中文优先，语气简洁、专业，不强制中英双语；必要的 API、路径和常用技术名称保持原文。
- 新文件开头用一两句说明职责与边界；涉及句柄、COM、窗口、计时器或订阅时，说明所有者与释放方式。
- 公开类型使用 `/// <summary>`。方法和属性仅在需要说明重要行为时补 XML 文档；按需注明返回、错误回退、线程、取消或资源要求，不为每个参数填空模板。
- 普通 `//` 注释解释原因或必要约束，不复述语句。历史修复过程、讨论和长篇设计说明放文档，不留在代码旁。
- 实现变化时同步核对相关注释；旧双语注释无需单独全量改写。`#region` 仅用于帮助定位较长文件，不替代合理的职责划分。

```csharp
// 没有有效像素时返回空集合，由调用方使用当前主题的强调色。
```

## C# 命名

| 对象 | 规则 | 示例 |
|---|---|---|
| 类型、方法、属性、事件、枚举成员 | `PascalCase`，单词首字母大写 | `MediaSnapshot`、`ApplySnapshot`、`IsPlaying` |
| 接口 | `I` 加 `PascalCase` | `IMediaSourceProvider` |
| 参数、局部变量 | `camelCase`，首单词小写 | `sourceId`、`pixelCount` |
| 私有字段，包括静态和 `readonly` 字段 | `_camelCase` | `_mediaSessions`、`_thumbnailCache` |
| `const` 常量，包括局部常量 | `PascalCase` | `MinimumAspect` |
| 类型参数 | `T` 或 `T` 加含义 | `T`、`TResult` |
| 普通异步方法 | `PascalCase` 加 `Async` 后缀 | `VerifyAsync` |
| 事件处理器 | 保持事件约定，不强制 `Async` 后缀 | `OnSettingsChanged` |

布尔名称优先使用 `Is`、`Has`、`Can`、`Should` 表达含义；字段仍带 `_`，例如 `_isDisposed`。涉及尺寸或时间时明确单位，例如 `heightDip`、`durationSeconds`，避免含义不明的 `value1`、`data2`。简单循环计数和明确的像素通道可用短名称，不机械地展开所有缩写。

普通构造参数用 `camelCase`；会生成公开属性的 `record` 位置参数用 `PascalCase`。新代码统一使用 `Taskbar` 等项目通用词形，旧类型名不因大小写调整顺手改动。

## 项目例外

- WPF 依赖属性、只读属性键和路由事件标识保留 `TitleProperty`、`IsLivePropertyKey`、`ClickEvent` 等约定，即使字段为私有也如此；控件名称沿用 `PascalCase`。
- `[ObservableProperty]` 字段用 `_camelCase`，生成属性用 `PascalCase`。修改字段前核对生成属性、命令名和 XAML 绑定。
- Win32/COM 声明保留原生名称，如 `WM_DPICHANGED`、`RECT`、`cbSize`；包装服务仍按普通 C# 规则命名。
- JSON 字段、设置键、组件 ID、来源标识、更新数据格式及其他外部名称遵循已有兼容要求，不为风格改变其值或格式。私有名称也可能被反射测试引用，重命名前须搜索使用处。
- 自动生成代码不手动整理；已有事件处理器、接口实现和框架要求的名称保留原契约。
- 歌词网页的 JavaScript 使用 `camelCase`，CSS 与 HTML 保留网页自身约定，不套用 C# 字段前缀。格式配置分别处理这些文件。

## 配置与执行

根目录 [.editorconfig](../.editorconfig) 统一缩进、编码、行尾和基础命名提示。C#、XAML 与 PowerShell 使用四个空格；JavaScript、CSS、JSON 与 YAML 使用两个空格。文件使用 UTF-8、CRLF 行尾并保留末尾换行；不为切换行尾批量改写旧文件。

命名提示覆盖类型、接口、方法、属性、事件、公开字段、常量、私有字段、参数和局部变量；规则先采用编辑器建议，不新增构建错误门槛。注释质量、布尔含义、单位、异步后缀和兼容例外由审查确认，配置不能证明这些要求全部满足。

`.editorconfig` 无法按字段类型区分普通静态只读字段与 WPF 标识，因此对当前四个含私有 WPF 标识的文件暂停静态只读字段提示，其余私有字段仍检查。这些文件的静态只读字段按上文人工复核；新增类似例外时说明原因，避免扩大到整个目录。原生声明与生成代码不应用普通命名检查。

需要检查某个文件时，在有效还原产物的基础上执行：

```powershell
dotnet format style .\src\AFMediaBar.slnx --diagnostics IDE1006 --severity info --verify-no-changes --no-restore --include src/AFMediaBar/Classes/Services/Example.cs
```

将示例路径替换为实际修改文件；仅检查，不自动改写。旧代码可能已有提示，修正本次涉及的命名，避免夹带无关重命名。代码验证仍按 [贡献指南](../CONTRIBUTING.md) 执行；需要编写或调整测试时读取 [测试制定规范](testing-guidelines.md)。
