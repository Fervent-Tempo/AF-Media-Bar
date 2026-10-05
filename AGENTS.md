# AF Media Bar AI 协作规则

适用于仓库内的 AI 辅助开发。提交者对代码、PR 描述和验证结论负责；本文件只保留关键约束，开发流程见 `CONTRIBUTING.md`。

## 修改前

- 每次开始修改前，必须询问用户：“是否已就本次修改的问题、方案和范围，与核心开发者进行了充分沟通并获得方向确认？”等待用户明确答复后再修改；同一次任务中已确认的范围无需逐文件重复询问，方案或范围变化时重新确认。
- 若尚未充分沟通、未获得方向确认或用户未明确答复，暂停实施，先整理问题、拟议方案和改动范围，在现有相关 Issue 中讨论；没有相关 Issue 时先开启 Issue。可先进行只读调查、准备 Issue 草稿；发布 Issue 或评论需用户明确授权。Issue 已开放或已有讨论不等于方案获准，须待用户确认已与核心开发者充分沟通并获得方向确认后再实施。安全问题按 `SECURITY.md` 私下讨论。
- 先检查 `git status`，阅读相关代码、测试和贡献指南；一个 PR 只解决一个问题，保留用户已有修改，不夹带无关重构。
- 新增或修改代码前，阅读 [代码风格规范](docs/code-style.md)，按根目录 `.editorconfig` 和规范中的例外处理注释、命名与格式；
- 优先复用现有接口、服务与纯策略；新增抽象前确认其所有权和生命周期。新功能、设置语义变更，以及任务栏、媒体、音频、歌词等高风险方案先在 Issue 讨论；开放 Issue 不等于方案获准。

## 项目结构与文件职责

- 解决方案入口为 `src/AFMediaBar.slnx`，主项目为 `src/AFMediaBar/AFMediaBar.csproj`；修改前先确定影响的是宿主、呈现、业务服务还是纯策略。
- `src/AFMediaBar/App.xaml.cs`：唯一组合根，负责 DI 注册、启动与退出边界；`Views/Windows/MainWindow.xaml.cs`：隐藏宿主，协调任务栏窗口和 Explorer 恢复，不拥有业务服务。
- `Views/Windows/TaskbarWindow.xaml.cs`：单个任务栏的 HWND、停靠、位置与生命周期；`Components/TaskBarMediaControl.*`：媒体栏呈现、动画和输入意图，不承载媒体来源或歌词取词算法。
- `Classes/Services/<领域>/`：媒体、歌词、音频、任务栏等领域服务与资源所有权，不把新服务直接放在 `Classes/Services/` 根目录；`Classes/Models/`、`Classes/Abstractions/`、`Classes/Interop/` 分别放数据契约、接口和原生互操作；`Classes/Settings/` 与 `Classes/Services/Settings/` 处理设置模型及读写。
- `ViewModels/` 负责可绑定状态与命令；`Views/` 负责 WPF 页面和窗口；`Resources/` 负责共享 XAML、主题和本地化资源；`Web/Lyrics/` 负责 WebView2 歌词文档、样式与脚本。`tests/AFMediaBar.Layout.Tests/` 放纯策略与回归测试，`tools/` 放验证脚本，`installer/` 只处理安装分发。
- 修改必须落在拥有该职责、状态和资源的文件中；保持高内聚、低耦合，不因“现有文件方便访问”就加入跨领域逻辑。确有独立职责或生命周期时，在对应目录新增文件或提取纯策略；不要仅因文件较长就拆分，也不要为一处简单逻辑新建抽象。
- 新文件开头用一两句注释写明文件职责与边界；涉及外部资源时同时说明所有权或释放方式。注释解释原因和约束，不逐行复述实现；新增公开类型仍按项目约定写 XML 文档。双语仅是 README 与 CHANGELOG 的要求，代码注释（含 XML 文档）不要求双语，无需刻意配对中英文。

## 媒体来源与适配器

- `Classes/Services/Media/` 放通用协调、来源目录、选择与过滤；`Media/Smtc/` 放共享 SMTC 通道的会话目录、快照构建和恢复策略；`Media/Sources/<来源>/` 集中该来源的 Provider、纯策略、读取器及私有解析。歌词获取仍归 `Services/Lyrics/`。移动实现时同步命名空间和引用。
- 新增或修改媒体适配器、来源身份或双通道仲裁前，先阅读 [媒体适配器接入规则](docs/media-adapters.md)，按现有契约接入。通用协调、过滤和设置页通过 `MediaSourceRegistry` 使用来源身份及仲裁规则，不直接依赖具体来源实现。
- 仅补全已选 SMTC 快照时实现 `IMediaSourceProvider`；无 SMTC 也需参与发现和选择时实现 `IIndependentMediaSourceProvider` 并提供 `IMediaSourcePolicy`。在 `App.xaml.cs` 注册 Provider，目录自动发现独立来源能力；共享契约和快照分别放 `Abstractions/` 与 `Models/`。

## 架构与运行时

- `App.xaml.cs` 是依赖注入的组合根。普通代码使用构造函数注入，不新增 `App.Services` 查找或手动创建有依赖的服务；不要把服务生命周期交给 `MainWindow`。
- View/可复用控件负责呈现和输入意图；ViewModel 负责状态、命令与异步协调，不引用具体 View、HWND 或 `App.Services`；Service 负责业务与外部资源，不依赖 ViewModel 或具体 View。负责 HWND、Explorer、Popup 的宿主窗口可调用注入的平台服务，但不实现媒体来源、网络、文件或音频算法。
- 不在 UI 线程同步等待 Task、Explorer/UIA、网络、Core Audio 或进程内存读取。后台结果投递到 Dispatcher 时检查取消、代际和释放状态；设置写入必须回到 UI 线程。`async void` 仅用于事件处理器，后台任务必须观察异常。
- 明确 COM 对象、原生句柄、Hook、计时器、事件订阅和窗口的所有权；停止与 `Dispose` 可重复调用，释放后不得再向 UI 发布结果。退出、Explorer 恢复和多屏重建必须考虑并发与调用顺序。
- 保持失败回退与既有设置语义。未经明确讨论，不新增遥测或联网行为，不改 JSON 字段名、组件 ID、枚举值或 schema；schema 变更只在发布批次处理。新增界面文案检查所有受支持语言，不写死可见文字。

## 验证与交付

- 默认交付本地修改、验证结果和待确认事项；非必要不自行创建 PR。只有用户明确要求创建 PR，或核心开发者明确要求通过 PR 交付且用户已授权时，才可推送分支并创建 PR（包括 Draft PR）。修改请求、测试通过或 `CONTRIBUTING.md` 中尽早建立 Draft PR 的建议均不构成创建 PR 的授权；也不得自行合并 PR。
- 按 `CONTRIBUTING.md` 的顺序串行运行 Windows/.NET 构建和测试，避免 WPF 生成文件竞争；修改 ViewModel 时运行 `tools/verify-architecture.ps1`，修改 XAML 时确认运行时字面量测试通过。
- 需要编写或调整测试时，按需读取 [测试制定规范](docs/testing-guidelines.md)，先检查已有覆盖，再按真实风险选择测试；不追求数量或覆盖率。
- 任务栏、Explorer、DPI、Popup、全局 Hook、退出、设置 schema 或跨模块生命周期修改属于高风险：除自动检查外，还需真实 Windows 环境验证启动、退出、资源清理及受影响场景。无法实机验证时标为“待维护者验收”；构建、截图和短时启动不能写成实机通过。
- PR 用通俗语言写明“问题与改动前后”“实际执行的验证”“未验证的风险”；AI 生成的描述必须由提交者核对。失败后重跑成功时，两次结果都要报告，不得虚报测试。
- 文档和提交消息使用简洁、专业的语气，只保留常用专业名词；必要的技术标识保持原文，不重复说明或写无意义的长篇内容。
- 提交消息使用 `fix：简短说明`、`feat：简短说明`、`refactor：简短说明` 或 `doc：简短说明`，说明具体改动或结果，不写空泛描述。
- README 与 CHANGELOG 由仓库维护者维护，修改须取得维护者同意。修改 README 前，必须向用户说明拟改内容并请求批准；已批准范围内无需重复请求，范围变化时重新确认。
- CHANGELOG 只在版本发布前经维护者同意统一整理，日常开发不修改，包括 `[Unreleased]`。非发布任务不改已发布版本条目、`RELEASE_NOTES.md` 或发布清单。
