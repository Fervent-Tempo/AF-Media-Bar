# 开发者工具

在“应用 → 诊断”开启开发者模式，点击“打开测试工具”。Debug 和 Release 均可使用，默认关闭。选择命令建议只填入输入框，按 Enter 或点击执行才生效。

## 命令

| 命令 | 用途 |
|---|---|
| `notify update` | 更新通知预览，点击打开设置，不检查或下载更新 |
| `notify taskbar moved`、`notify taskbar hidden` | 任务栏空间不足提示预览 |
| `notify background` | TranslucentTB 提示预览，点击打开背景设置 |
| `notify lyrics` | 歌词故障通知预览，点击仅预览重启确认 |
| `notify restart busy`、`notify restart failed` | 重启结果提示预览 |
| `notify track` | 使用固定歌曲样例预览切歌通知 |
| `restart confirm` | 预览确认窗口，不重启 |
| `app restart` | 确认后普通重启 |
| `webview rebuild` | 按恢复预算排队重建所选歌词宿主 |
| `webview fail renderer`、`webview fail unresponsive` | 注入渲染进程失败或一次不响应，按现有阈值和恢复预算处理 |
| `webview fail gpu` | 注入 GPU 退出诊断，不主动重建 |
| `webview disable` | 确认后停用当前会话的全部歌词 |
| `taskbar reload` | 安全重载任务栏宿主，不终止 Explorer |
| `taskbar surface` | 注入宿主失绘，检查自动重建 |
| `state` | 查看版本、显示器、歌词宿主、恢复预算、电源及内存状态 |
| `resources baseline`、`resources compare` | 保存资源基线，或比较当前读数与基线 |
| `memory trim` | 请求手动整理内存 |
| `logs open`、`help` | 打开日志目录，或查看完整命令说明 |

关闭窗口取消未完成的开发者请求并释放通知预览；关闭开发者模式还会清除资源基线。已提交的重启、歌词停用状态和正式恢复预算不随窗口关闭撤销。确认期间所选宿主消失时，旧请求失效。

通知预览不消耗正式通知额度。故障注入标记为 `DeveloperInjected`，使用真实恢复预算，可能影响当前会话；重启 AF 后可重新测试。通知提交成功不代表用户看到通知，恢复请求已排队也不代表歌词已恢复。

## 资源对比

先执行 `resources baseline`，复现重载、恢复等操作后执行 `resources compare`。可重复比较，再次保存基线会覆盖原值。

读数包含 AF 主进程的工作集、私有字节、托管堆、句柄、线程及 GC 次数，不含 WebView2 子进程。采样不触发 GC 或内存整理，增长本身不能证明泄漏。关闭工具窗口保留基线，关闭开发者模式则清除；清空输出不影响基线或执行中的动作。

## 日志与持续记录

普通日志位于 `%LOCALAPPDATA%\AFMediaBar\logs`。`app.log` 后台追加，单份 5 MiB，连同四份历史文件总量约 25 MiB，记录剪枝、WebView2、通知及异常的时间和关联身份。

持续性能记录仅在 Debug 中启动时自动开启，不依赖开发者模式或工具窗口。数据包含系统及 AF／WebView2 资源、GPU 进程与引擎、GC 暂停、界面和鼠标 Hook 延迟；不记录歌曲、歌词、命令行或用户文件路径。

| Debug 命令 | 用途 |
|---|---|
| `performance status` | 查看状态、目录、最近采样及丢弃数量 |
| `performance start`、`performance stop` | 启停本次进程的持续记录 |
| `performance mark` | 标记刚才发生的异常并请求刷新文件 |
| `performance open` | 打开性能记录目录 |

性能文件保存在日志目录的 `performance` 子目录。活动时每秒采样，空闲时四秒；熄屏、锁屏或睡眠暂停，恢复后重建差分基线。每两秒后台刷新，单份 10 MiB，最多五份。显式停止后本次进程保持停止，下次 Debug 启动恢复；写盘失败时停止，可手动重试。

JSONL 格式版本为 2：`context` 声明会话和编号作用域，`process`、`gpuEngine` 保存身份，采样通过编号引用，常规数值位于 `payload.measurements`。读取器遇到 `context` 应清空编号缓存；轮转文件自带身份表，旧记录原样保留。

全零、无关的 GPU 引擎不写入，未知值保留为 `null`；`process: null` 表示只有 PID、身份未确认。百分比和耗时仅在输出时保留两位小数，异常判断仍使用原始精度。异常退出可能丢失末尾记录，读取时忽略不完整尾行。

排查时记录发生时间并执行 `performance mark`，结合普通日志与性能文件分析。采样空档不能直接证明 GC 或驱动故障，整个桌面冻结可能还需 Windows 性能跟踪。

## 实机验收

Debug／Release 入口、通知显示与点击、歌词降级和重建、Explorer 恢复、多屏／DPI、熄屏及睡眠恢复、正常退出的资源清理：待维护者验收。构建和自动测试不能替代这些检查，故障注入也不能证明复现了驱动故障。
