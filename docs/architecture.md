# 架构边界

本文说明仓库目录、职责、依赖、线程及资源所有权；权限和通用底线以根目录 AGENTS.md 为准。

## 仓库目录

以下列出主要代码与开发目录，路径相对仓库根目录。

```text
AF-Media-Bar/
├── .github/workflows/        # 构建与发布工作流
├── src/AFMediaBar/           # WPF 应用主项目
│   ├── Classes/              # 分层业务代码
│   │   ├── Abstractions/     # 跨模块契约
│   │   ├── Interop/          # Windows API 互操作
│   │   ├── Models/           # 数据模型（含布局 schema）
│   │   ├── Services/         # 按所有权分目录的服务（Media、Lyrics、Audio、Updates…）
│   │   ├── Settings/         # 设置模型与兼容门面
│   │   └── Utils/            # 无状态辅助与有界缓存
│   ├── Components/           # MediaBar 媒体呈现、Settings 设置控件、Common 共用控件
│   ├── Resources/            # 主题、样式与语言文案
│   ├── ViewModels/           # MVVM 视图模型
│   └── Views/                # 页面与宿主窗口
├── src/AFMediaBar.MemoryHelper/ # 固定内存操作的隔离执行端，不加载应用配置或 Host
├── tests/AFMediaBar.Layout.Tests/   # 按领域组织的逻辑、呈现与生命周期回归
├── tools/                    # 设置契约与开发、发布验证脚本
├── installer/                # Inno Setup 安装脚本
└── docs/                     # 项目文档、资源与版本清单
```

## 职责与入口

解决方案为 `src/AFMediaBar.slnx`，主项目为 `src/AFMediaBar/AFMediaBar.csproj`。以下路径相对主项目。

| 所有者 | 职责与边界 |
|---|---|
| `App.xaml.cs` | DI 注册、启动与最终退出；唯一组合根，外观资源交给 ApplicationAppearanceResources 生成 |
| `Views/Windows/MainWindow.xaml.cs` | 隐藏宿主，协调任务栏窗口、媒体快照转发与 Explorer 恢复；不拥有业务服务 |
| `Views/Windows/TaskbarWindow.xaml.cs` | 单个任务栏的 HWND、停靠、位置及窗口资源生命周期 |
| `Views/Windows/FloatingBarWindow.cs` | 单个显示器上的独立工具窗口、拖动、四边折叠、原生命中区域、弹层和呈现租约 |
| `BarRuntimeCoordinator` | 事务切换宿主；全部激活成功后发布实际模式，失败恢复原宿主 |
| `DesktopWidgetZOrderService` | 已注册浮窗的桌面/置顶层级、WinEvent 和观察计时器；最后租约释放时停止观察 |
| `Components/MediaBar/TaskBarMediaControl.*` | 呈现、动画和输入意图；不承载媒体来源与歌词算法 |
| `TaskBarMediaControl.ArtworkHoverController` | 每个控件独有的封面 Popup、重挂、动画代际与回收计时器；挂起/卸载立即回收，宿主最终关闭时释放 |
| `TaskBarMediaControl.BarLyricsPresenter` | 每个控件独有的 WebView、刷新/重试计时器和歌词恢复订阅；卸载可重载，最终释放拒绝迟到结果 |
| `SpectrumPresentationSession` | 每个任务栏或悬浮宿主的采样计时器、缓冲与代际；借用应用级 AudioMonitorService，不释放共享音频服务 |
| `FullPanelWindowHost` | 各显示器完整面板的字典、关闭冷却、复用和最终释放；MainWindow 只表达打开、关闭和上下文意图 |
| `MediaSessionReconcileCoordinator` | SMTC 重连看门狗、取消与退避；借用目录和选择器，不接管它们的释放 |
| `ViewModels/` | 可绑定状态、命令和异步协调；不引用具体 View、HWND 或 App.Services |
| `Classes/Services/<领域>/` | 业务、平台资源、协调器与纯策略；新服务不直接放 Services 根目录 |
| `Classes/Models/`、`Abstractions/`、`Interop/` | 数据契约、接口与原生互操作 |
| `Classes/Settings/`、`Services/Settings/` | 设置模型、读写与持久化 |
| `Views/`、`Resources/`、`Web/Lyrics/` | WPF 页面与宿主、共享样式与语言、内嵌歌词绘制 |
| `tests/AFMediaBar.Layout.Tests/`、`tools/`、`installer/`（仓库根目录） | 回归测试、验证脚本、安装分发 |

布局渲染引擎随媒体呈现放在 Components/MediaBar；字体枚举归 Appearance；封面读取与缓存归 Media/Artwork；设置搜索归 Settings。目录迁移保留既有类型全名，避免夹带命名空间重命名。

AppSettings、设置枚举/事件和 SettingsManager 门面分文件维护；设置契约按交互、媒体栏、频谱、指标及通知归组，不改变序列化名称或数值。

歌词网页通过 LyricsWebViewRenderer 按顺序拼成一个内嵌脚本：context 提供 DOM 与共享布局上下文，word-scan 和 transitions 负责歌词推进，artwork 和 spectrum 以闭包独占各自状态，app 接收原有协议。模块不引入额外联网或加载器。

代码落在拥有该状态和资源的领域。只在独立职责、复用或生命周期需要时新增文件；不因文件长度或访问方便拆分、跨层添加逻辑。新文件职责说明和公开类型 XML 文档见代码风格规范。

系统内存优化使用独立的 `AFMediaBar.MemoryHelper` 执行端。主程序内嵌其执行文件与自带运行库的归档，按需提权；持久授权仅注册指向管理员保护目录的固定任务；执行文件及运行库均在该目录部署，不依赖临时目录自解压，返回结果通过带请求身份的单向管道验证。执行端没有 WPF、普通 Host、用户设置或任意命令入口。

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

## 呈现过渡的所有权

- 宿主保存期望几何，动画只产生绘制帧；中断不能用中间帧覆盖逻辑目标。内容变宽与定位、环境变化分别处理。
- 同一属性同时只由一条过渡控制；位移分支可组合，连接裁剪期间不再启动文字入场。替换动画前先读取当前有效值，再释放旧时钟。
- 完成回调和兜底计时器检查代际及目标状态，旧关闭操作不得清理新展开。立即结束与正常完成共用幂等终态，卸载不再发布结果。
- 任务栏运动或隐藏时停止连接过渡并清理临时视觉资源，保留最新尺寸请求；稳定后先落定位、尺寸和命中区域，再恢复交互。动画回归须覆盖反向、重定向与宿主挂起的组合，真实桌面验收要求不变。

## 设置模式与共用值

设置页仅编辑运行协调器已提交的当前模式；切换失败继续使用原模式，离开上下文先取消页面请求与延迟写入。
字体、字号、外观、歌词呈现、组件和交互使用现有全局配置。任务栏沿用已发布字段；桌面悬浮模式仅以 BarDisplaySettings 保存几何，投影不覆盖共用值。
显示器、位置、锁定、展开方向、长度与厚度按模式保存，朝向不增加配置副本。页面重置恢复共用部分和当前模式几何，不改另一模式几何。
