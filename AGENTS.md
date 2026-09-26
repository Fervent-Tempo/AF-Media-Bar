# AF Media Bar AI 协作规则

适用于仓库内的 AI 辅助开发。提交者对代码、PR 描述和验证结论负责；本文件只保留关键约束，开发流程见 `CONTRIBUTING.md`。

## 修改前

- 先检查 `git status`，阅读相关代码、测试和贡献指南；一个 PR 只解决一个问题，保留用户已有修改，不夹带无关重构。
- 优先复用现有接口、服务与纯策略；新增抽象前确认其所有权和生命周期。新功能、设置语义变更，以及任务栏、媒体、音频、歌词等高风险方案先在 Issue 讨论；开放 Issue 不等于方案获准。

## 项目结构与文件职责

- 解决方案入口为 `src/AFMediaBar.slnx`，主项目为 `src/AFMediaBar/AFMediaBar.csproj`；修改前先确定影响的是宿主、呈现、业务服务还是纯策略。
- `src/AFMediaBar/App.xaml.cs`：唯一组合根，负责 DI 注册、启动与退出边界；`Views/Windows/MainWindow.xaml.cs`：隐藏宿主，协调任务栏窗口和 Explorer 恢复，不拥有业务服务。
- `Views/Windows/TaskbarWindow.xaml.cs`：单个任务栏的 HWND、停靠、位置与生命周期；`Components/TaskBarMediaControl.*`：媒体栏呈现、动画和输入意图，不承载媒体来源或歌词取词算法。
- `Classes/Services/<领域>/`：媒体、歌词、音频、任务栏等领域服务与资源所有权，不把新服务直接放在 `Classes/Services/` 根目录；`Classes/Models/`、`Classes/Abstractions/`、`Classes/Interop/` 分别放数据契约、接口和原生互操作；`Classes/Settings/` 与 `Classes/Services/Settings/` 处理设置模型及读写。
- `ViewModels/` 负责可绑定状态与命令；`Views/` 负责 WPF 页面和窗口；`Resources/` 负责共享 XAML、主题和本地化资源；`Web/Lyrics/` 负责 WebView2 歌词文档、样式与脚本。`tests/AFMediaBar.Layout.Tests/` 放纯策略与回归测试，`tools/` 放验证脚本，`installer/` 只处理安装分发。
- 修改必须落在拥有该职责、状态和资源的文件中；保持高内聚、低耦合，不因“现有文件方便访问”就加入跨领域逻辑。确有独立职责或生命周期时，在对应目录新增文件或提取纯策略；不要仅因文件较长就拆分，也不要为一处简单逻辑新建抽象。
- 新文件开头用一两句注释写明文件职责与边界；涉及外部资源时同时说明所有权或释放方式。注释解释原因和约束，不逐行复述实现；新增公开类型仍按项目约定写 XML 文档。

## 架构与运行时

- `App.xaml.cs` 是依赖注入的组合根。普通代码使用构造函数注入，不新增 `App.Services` 查找或手动创建有依赖的服务；不要把服务生命周期交给 `MainWindow`。
- View/可复用控件负责呈现和输入意图；ViewModel 负责状态、命令与异步协调，不引用具体 View、HWND 或 `App.Services`；Service 负责业务与外部资源，不依赖 ViewModel 或具体 View。负责 HWND、Explorer、Popup 的宿主窗口可调用注入的平台服务，但不实现媒体来源、网络、文件或音频算法。
- 不在 UI 线程同步等待 Task、Explorer/UIA、网络、Core Audio 或进程内存读取。后台结果投递到 Dispatcher 时检查取消、代际和释放状态；设置写入必须回到 UI 线程。`async void` 仅用于事件处理器，后台任务必须观察异常。
- 明确 COM 对象、原生句柄、Hook、计时器、事件订阅和窗口的所有权；停止与 `Dispose` 可重复调用，释放后不得再向 UI 发布结果。退出、Explorer 恢复和多屏重建必须考虑并发与调用顺序。
- 保持失败回退与既有设置语义。未经明确讨论，不新增遥测或联网行为，不改 JSON 字段名、组件 ID、枚举值或 schema；schema 变更只在发布批次处理。新增界面文案检查所有受支持语言，不写死可见文字。

## 验证与交付

- 按 `CONTRIBUTING.md` 的顺序串行运行 Windows/.NET 构建和测试，避免 WPF 生成文件竞争；修改 ViewModel 时运行 `tools/verify-architecture.ps1`，修改 XAML 时确认运行时字面量测试通过。新增纯策略应测试会造成崩溃、卡顿、泄漏、数据或布局错误的分支，不为常量、文案和控件排列凑覆盖率。
- 任务栏、Explorer、DPI、Popup、全局 Hook、退出、设置 schema 或跨模块生命周期修改属于高风险：除自动检查外，还需真实 Windows 环境验证启动、退出、资源清理及受影响场景。无法实机验证时标为“待维护者验收”；构建、截图和短时启动不能写成实机通过。
- PR 用通俗语言写明“问题与改动前后”“实际执行的验证”“未验证的风险”；AI 生成的描述必须由提交者核对。失败后重跑成功时，两次结果都要报告，不得虚报测试。
- 仅当用户使用方式或重要限制明显改变时更新中英文 README；开发时可维护中英文 CHANGELOG 的 `[Unreleased]`。非发布任务不改已发布版本条目、`RELEASE_NOTES.md` 或发布清单。
