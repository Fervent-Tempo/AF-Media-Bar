# 开发者模式与手动测试工具

开发者模式在“应用 → 诊断”手动开启，Debug 和 Release 均可使用，默认关闭。开启不自动执行动作。设置参与用户默认快照；关闭取消开发者请求，不清除生产恢复预算或会话停用状态。

## 实施范围

1. 增加设置、取消代际和设置搜索入口，验证持久化与重置语义。
2. 增加独立工具窗口，以同一动作目录驱动按钮、命令建议和帮助；接入通知预览、WebView2 故障注入、普通重启、任务栏重载和状态快照。
3. 补充预览与生产请求隔离、取消、过期结果和四语言回归，并完成串行构建、测试及设置契约校验。

通知预览不消耗生产去重额度，不改变更新、TranslucentTB、媒体或定位状态。开发者歌词通知有独立点击目标。重启确认预览与生产请求共用呈现组件，但不得合并请求；预览永不调用重启服务。

故障注入标记为 `DeveloperInjected`，进入生产事件解析后的共同处理流程，不伪造堆栈或制造系统资源不足。注入会影响当前会话并使用真实预算；异常分类由自动测试独立覆盖。真实进程崩溃、电源模拟和虚拟媒体来源不在第一版范围。

动作结果区分已提交、已排队、完成、条件不满足、取消及失败。通知提交不能证明用户看到通知，恢复排队不能证明歌词已恢复。结果记录有界，不持久化命令历史。状态快照不默认包含歌曲标题、歌词或用户路径。

## 验收

通知实际显示与点击、Debug/Release 入口、多屏/DPI、显示器关闭恢复、Explorer 恢复、歌词降级、确认与取消重启、正常退出资源清理：待维护者验收。故障注入验证应用处理流程，不能证明重现了驱动故障或修复了 Explorer 自身重启。

## 使用入口与命令

在“应用 → 诊断”开启开发者模式，再点击“打开测试工具”。输入框提供建议；选择建议只填入命令，点击执行或按 Enter 才执行。按钮和输入共用动作目录。重复打开复用窗口，关闭窗口取消本次请求并释放样例通知；关闭模式也忽略旧开发者通知点击。

| 命令 | 实际行为 |
|---|---|
| `notify update` | 更新通知预览，点击打开应用设置，不检查或下载 |
| `notify taskbar moved`、`notify taskbar hidden` | 空间不足提示预览，不改变定位或可见性 |
| `notify background` | TranslucentTB 提示预览，点击打开背景设置，不改背景或已提示标记 |
| `notify lyrics` | 独立歌词故障通知，点击仅预览确认 |
| `notify restart busy`、`notify restart failed` | 结果通知预览，不取得更新／重启预约 |
| `notify track` | 独立窗口显示固定样例，使用当前通知布局 |
| `restart confirm` | 只记录确认选择，不重启 |
| `app restart` | 确认后普通重启，保留现有更新互斥与进程交接 |
| `webview rebuild` | 所选宿主按现有预算排队重建 |
| `webview fail renderer` | 注入主渲染进程失败，进入现有失败与重试流程 |
| `webview fail unresponsive` | 注入一次不响应；暂停投递时拒绝，成功脚本会清零 |
| `webview fail gpu` | 注入 GPU 退出诊断，不主动重建 |
| `webview disable` | 确认后停用全部会话歌词，跨宿主与 Explorer 重建保留 |
| `taskbar reload` | 使用现有安全重载，不终止 Explorer |
| `state` | 显示 AF、.NET、可用及活动 WebView2 版本、显示器/DPI、各宿主代际与预算、电源和内存 |
| `memory trim` | 使用现有手动整理入口，显示请求是否被接受 |
| `logs open`、`help` | 打开现有日志目录或列出动作说明 |

宿主重建会改变测试身份。确认期间所选宿主消失时拒绝旧请求，不改为操作另一宿主。预览和生产确认不跨用途合并；已有另一用途的确认时拒绝新请求。普通重启成功交接后仍按生产退出流程处理，关闭工具不能撤销已经提交的重启。

诊断内存和可用 Runtime 版本在后台读取；活动 Runtime 来自各渲染器缓存。输出只表示采样时刻。内存整理和图形恢复返回接受／排队时，不宣称完成。关闭模式不恢复已停用的歌词，不清除正式通知额度；需要重启 AF 才能重新测试这些会话状态。

## 自动验证与实机边界

自动回归覆盖固定命令白名单、按钮与命令入口一致、有界结果、预览和正式通知额度隔离、原生点击标识及 Explorer 后恢复、跨用途确认、确认期间取消、旧宿主目标、关闭后的迟到结果及四语言资源。独立 WPF 测试进程加载工具窗口并检查绑定；它不运行真实 AF 或触发桌面测试动作。

Release/Debug 串行构建与测试、设置契约 Verify 和差异检查用于工程验证。任务栏、通知、恢复、DPI 和重启的完整 Windows 场景仍按上文验收，不以自动检查替代。

## 本次本地验证记录

2026-10-10：Debug 和 Release 串行构建通过，各自全量测试通过 235 项。隔离 WPF 资源与绑定检查通过；IDE1006 命名检查通过，新增 ViewModel 依赖边界已人工核对。设置契约 Verify 通过 139 个路径、35 类枚举、125 个成员；schema 仍为 2。

验证中修正了测试 API 用法和新增字段的契约文档遗漏。执行器释放后的迟到确认回归先失败，补齐服务自身取消源后通过。Debug 增量构建曾缺少 WPF 生成的 `.g.cs` 文件，串行 Rebuild 后构建与全量测试通过；未宣称修复增量构建根因。构建保留原有 16 条可空引用警告（WPF 临时项目与主项目重复报告）。

完整 Windows 场景仍待维护者验收。没有推送、创建 PR、修改 README 或发布文档。

## 调研依据

- [EarTrumpet Debug 菜单与模拟设备](https://github.com/File-New-Project/EarTrumpet/blob/154980811087fb7c423d806e2d9816733db6a7a5/EarTrumpet/DebugHelpers.cs)：采用明确场景入口，不照搬其反射注入或 Debug 限制。
- [Flow Launcher 系统命令目录](https://github.com/Flow-Launcher/Flow.Launcher/blob/c68afbcc05f37e9d1c27bd0338313b48fb0782a6/Plugins/Flow.Launcher.Plugin.Sys/Main.cs)：名称、说明、搜索与动作共用目录。
- [ScreenToGif 独立诊断窗口](https://github.com/NickeManarin/ScreenToGif/blob/a4d0a67c2131cd048ceec86cd40afc2f1a06f2fd/ScreenToGif/Windows/Other/Troubleshoot.xaml.cs)：显示实际环境并在关闭时退订。
- [WebView2 官方 WPF 场景命令](https://github.com/MicrosoftEdge/WebView2Samples/blob/main/SampleApps/WebView2WpfBrowser/MainWindow.xaml)：参考可用条件和场景组织，第一版不使用真实崩溃地址。
