# Rust 重写架构决策

记录 AF Media Bar 从 WPF/.NET 10 全量重写为 Rust 的方向、选型与里程碑。方向已与核心开发者确认（2026-11）；本文档随里程碑推进更新。

## 目标形态

- **全量重写**：Rust 实现达到功能对等后替换 C# 版本；迁移期间 C# 代码保留作对照与回退，移除时机发布前与维护者确认。
- **设置完全兼容**：schema v2、路径 `%LOCALAPPDATA%\AFMediaBar\settings.json`、字段语义/隔离/重置语义不变；老用户无缝升级。
- **已发布 ID 一律不变**：互斥体 `Local\AFMediaBar.SingleInstance`、`AFMediaBar.InstallCoordinator`、管道前缀 `AFMB.Memory.`、Inno AppId、设置路径。

## UI 渲染方案（已选）

**Win32 原生窗口 + Skia 自绘 + WebView2**，与现有架构逐层对应：

| 现有 | Rust 侧 |
|---|---|
| WPF 窗口/控件（栏、浮层、通知、菜单） | 裸 Win32 窗口 + `skia-safe` 自绘 |
| WebView2 歌词（`Web/Lyrics/` 资产） | `webview2-com`，资产直接复用 |
| WPF-UI 设置窗口（30 页） | WebView2 + HTML（M4） |
| MicaWPF / DWM backdrop | DWM 属性直调（`NativeWindowBackdropAdapter` 等价物） |

理由：任务栏 `WS_CHILD` 停靠、`SetWindowRgn` 点击穿透、UIA 占位探测等硬核窗口操作在裸 Win32 层风险最低；WebView2 透明合成托管只保留歌词一处（与现状一致）。

## Workspace 布局

`rust/` 下 Cargo workspace，crate 对应现有服务域（`src/AFMediaBar/Classes/Services/`）：

| crate | 职责 | 里程碑 |
|---|---|---|
| `afmb-core` | 调度器（每线程 Win32 消息循环）、滚动日志、generation/lease、退出看门狗 | M0 ✅ |
| `afmb-settings` | schema v2 根模型 + 持久化 + 隔离 + user-defaults；分组经 `extra` 透传、逐里程碑类型化 | M0 ✅ |
| `afmb-l10n` | 四语言资源（resx→JSON 转换）、语言解析、文案查询 | M0 ✅ |
| `afmb-platform` | Win32/COM/WinRT 绑定层：单实例、任务栏停靠、显示器、DWM、Raw Input、UIA、Core Audio | M0 起逐里程碑 |
| `afmb-media` | SMTC 通道（租约+重建看门狗）、注册表、选择、快照、NetEase/KuGou 源、封面 | M1/M5 |
| `afmb-audio` | 设备目录/切换（IPolicyConfig）、应用音量/路由、环回频谱、蓝牙电量 | M2 |
| `afmb-lyrics` | 提供方、解析、缓存、简繁、WebView2 呈现桥 | M3 |
| `afmb-shell` | 托盘、热键、轨道通知、快速启动、更新、自启动、重启交接 | M4/M5 |
| `afmb-ui-bar` | Skia 自绘：TaskbarWindow、悬浮栏、全屏面板、浮层、菜单、Toast | M1/M2/M4 |
| `afmb-ui-settings` | WebView2 设置窗口 + HTML（30 页移植） | M4 |
| `afmb-app` | 组合根（唯一 bin，对应 `App.xaml.cs`） | M0 起逐里程碑 |
| `afmb-memory-helper` | 提权助手（同管道协议、同计划任务逻辑） | M5 |

## 工程规则映射（承自 AGENTS.md / docs/architecture.md）

- `afmb-app/src/main.rs` 是唯一组合根；构造注入（struct 字段），无全局服务定位器。
- UI 线程 = 主线程消息循环（`afmb_core::Dispatcher`）；后台结果经 `post` 投递，应用前检查 generation/停止状态；停止后 `post` 返回 `Err`（释放后不向 UI 发布结果）。
- `stop()` 幂等 + `Drop` 兜底；后台线程用 `spawn_observed` 启动，panic 必经全局钩子（后台异常必须被观察）。
- UI 线程不同步等待 Task/COM/网络/进程读取；设置写入经去抖后台线程。

## 已知偏差（如实记录）

- 隔离文件名时间戳为 UTC（C# 用本地时间），仅影响文件名可读性。
- `AllowNamedFloatingPointLiterals`（NaN/Infinity 字面量）不支持：含此类字面量的手改设置文件被判 invalid 隔离，C# 可读入并归一化。
- 设置分组（appearance、taskbarExperience 等 19 组）在各自里程碑类型化之前经 `extra` 原样透传：已有文件读写不丢字段；新写文件缺失的分组字段由读取方按内置默认补齐（与 C# 缺失成员回落字段初始化器一致）。
- C# `AppSettings.Normalize` 中悬浮栏 Profile 捕获（floatingBar 分组）待悬浮栏里程碑接管。

## 里程碑

- **M0 基础设施**（已完成）：workspace、afmb-core/settings/l10n、单实例、组合根骨架。验收：冒烟启动/退出（exit 0）、settings.json（schema v2）与日志产物、重复实例 exit 2、41 个单元测试、clippy 零警告。
- **M1 媒体核心 + 任务栏栏**：windows-rs SMTC（租约 + 重建看门狗移植）、TaskbarWindow（`Shell_TrayWnd` 查找、`WS_CHILD` 停靠、DPI、放置计算器、UIA 占位探测）、Skia 栏渲染、SMTC 播放控制。验收：真机可见、可控、多显示器。
- **M2 音频与浮层**：设备枚举/切换、按应用音量/路由、浮层与全屏面板、环回频谱。
- **M3 歌词**：提供方、解析/缓存、WebView2 歌词（复用 `Web/Lyrics/`）、简繁、卡拉OK。
- **M4 设置窗口 + 外观**：WebView2 设置外壳 + 30 页、主题/材质、自适应前景色、托盘/通知、快速启动。
- **M5 独立源与系统集成**：NetEase/KuGou 内存读取、蓝牙电量、指标、内存修剪 + MemoryHelper、更新、重启交接、自启动。
- **M6 对等收尾**：Credits/Home、安装器载荷切换、发布管线、移除 C#（与维护者确认）。

## 开发命令

```powershell
cd rust
cargo test --workspace          # 串行执行（与 C# 侧同一原则）
cargo clippy --workspace --all-targets
cargo fmt
cargo run -p afmb-app -- --afmb-settings-dir=<dir> --afmb-smoke-exit   # 冒烟（测试专用参数）
```

测试专用参数（发布形态无参数）：`--afmb-settings-dir=`（隔离用户数据）、`--afmb-smoke-exit`（启动后立即退出且不弹框）、`--afmb-single-instance-name=`（自动化测试覆盖互斥体名）。

## 验收边界

自动检查不证明桌面体验。任务栏停靠、DPI、Explorer 重启恢复、SMTC 控制、音频切换、退出看门狗、睡眠唤醒须真实 Windows 验收；未真机验证的项标注"待维护者验收"。
