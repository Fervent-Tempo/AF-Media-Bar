//! AF Media Bar 基础设施：调度器、滚动日志、生命周期原语、退出看门狗。
//!
//! 对应 C# 版基础设施：
//! - [`dispatcher::Dispatcher`] ← WPF `Dispatcher`（每线程 Win32 消息循环 + 回调投递）
//! - [`log::AppLog`] ← `AppLogService` + `RollingLogFile`（有界队列、5 MiB × 5 滚动）
//! - [`generation::Generation`] / [`lease::ReplaceableResource`] ← 代际检查 / `ReplaceableResource` 租约
//! - [`exit::ExitCoordinator`] ← `ApplicationExitCoordinator`（10 s 看门狗 → `TerminateProcess`）
//! - [`thread::spawn_observed`] / [`thread::set_uncaught_hook`] ← "后台异常必须被观察"规则
//!
//! 线程规则（承自 `docs/architecture.md`）：
//! - UI/宿主线程上的后台结果必须经 [`Dispatcher::post`] 投递，应用前检查代际与停止状态；
//! - 停止后的 [`Dispatcher::post`] 返回 `Err`（释放后不向 UI 发布结果）。

pub mod dispatcher;
pub mod exit;
pub mod generation;
pub mod lease;
pub mod log;
pub mod thread;

pub mod prelude {
    pub use crate::dispatcher::{DispatchError, Dispatcher};
    pub use crate::exit::ExitCoordinator;
    pub use crate::generation::Generation;
    pub use crate::lease::ReplaceableResource;
    pub use crate::log::{AppLog, Level};
    pub use crate::thread::spawn_observed;
}
