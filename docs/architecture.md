# 架构边界

本文用于职责、依赖、线程及资源所有权变更；权限和通用底线以根目录 AGENTS.md 为准。

## 职责与入口

解决方案为 `src/AFMediaBar.slnx`，主项目为 `src/AFMediaBar/AFMediaBar.csproj`。以下路径相对主项目。

| 所有者 | 职责与边界 |
|---|---|
| `App.xaml.cs` | DI 注册、启动与最终退出；唯一组合根 |
| `Views/Windows/MainWindow.xaml.cs` | 隐藏宿主，协调任务栏窗口、媒体快照转发与 Explorer 恢复；不拥有业务服务 |
| `Views/Windows/TaskbarWindow.xaml.cs` | 单个任务栏的 HWND、停靠、位置及窗口资源生命周期 |
| `Components/TaskBarMediaControl.*` | 呈现、动画和输入意图；不承载媒体来源与歌词算法 |
| `ViewModels/` | 可绑定状态、命令和异步协调；不引用具体 View、HWND 或 App.Services |
| `Classes/Services/<领域>/` | 业务、平台资源、协调器与纯策略；新服务不直接放 Services 根目录 |
| `Classes/Models/`、`Abstractions/`、`Interop/` | 数据契约、接口与原生互操作 |
| `Classes/Settings/`、`Services/Settings/` | 设置模型、读写与持久化 |
| `Views/`、`Resources/`、`Web/Lyrics/` | WPF 页面与宿主、共享样式与语言、内嵌歌词绘制 |
| `tests/AFMediaBar.Layout.Tests/`、`tools/`、`installer/`（仓库根目录） | 回归测试、验证脚本、安装分发 |

代码落在拥有该状态和资源的领域。只在独立职责、复用或生命周期需要时新增文件；不因文件长度或访问方便拆分、跨层添加逻辑。新文件职责说明和公开类型 XML 文档见代码风格规范。

## 依赖与平台宿主

- 普通页面遵循 MVVM；Service 不依赖 ViewModel、具体 View 或窗口存在。Model 与纯策略不执行 I/O、Dispatcher 或 HWND 操作，不暗改全局设置；对无效输入明确失败或回退。
- 宿主窗口可负责 HWND/消息、DPI、Popup 所有权、Z 序、显示关闭和状态转发；不拥有业务算法或跨模块缓存。
- 生命周期按资源所有权决定，不机械统一为 Singleton 或 Transient。容器工厂只在组合根组装依赖；遗留 Service Locator 不作为新代码范例。
- 构造函数不做长时间网络、UIA、音频枚举或同步等待；使用显式启动或异步初始化。改变页面/ViewModel 生命周期前处理事件订阅，关闭窗口不等于释放订阅。
- 通用媒体协调、来源过滤和设置页通过 MediaSourceRegistry 使用来源身份；来源私有实现及共享 SMTC 分工见 [媒体接入](media-adapters.md)。

## 线程与释放

- 不持锁调用 Dispatcher、外部组件、Explorer/UIA 或用户回调。后台结果在排队和应用处核对取消、代际及释放状态，旧结果不能覆盖新状态。
- 设置模型写入与后台文件保存分开：模型事件可能同步通知绑定集合，必须在 UI 线程写入。
- 状态或资源替换前停止旧动画、计时器和请求；关键释放不依赖已经停止的 Dispatcher 续体。
- App.OnExit 是最终退出边界。关闭菜单/Popup/窗口，取消恢复任务，再按依赖顺序停止 Hook、托盘、媒体来源与 Host；ContextMenu 不属于 Application.Windows，须显式关闭。
- Host 停止和释放有超时兜底；需用的服务在容器释放前取得。退出交接只做有界等待，不在 UI 线程重新计算安装包哈希。

具体平台时序与真实场景由受影响功能核实。文本扫描、构建和纯策略测试不能证明完整生命周期安全。
