//! 退出协调器：合并退出请求 + 10 秒看门狗强制终止。
//!
//! 对应 C# `ApplicationExitCoordinator`：
//! - 首个 [`ExitCoordinator::request_exit`] 生效（后续请求合并，退出码取首个）；
//! - 看门狗在首个请求时武装，10 秒后仍存活则循环调用 `TerminateProcess` 直至进程死亡；
//! - 看门狗线程不碰日志锁与调度器（对应"刻意不触碰"的设计），保证卡死时仍能退出。

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Condvar, Mutex};
use std::time::Duration;

use windows::Win32::System::Threading::{GetCurrentProcess, TerminateProcess};

/// 看门狗宽限期：正常退出路径必须在此之前完成。
pub const WATCHDOG_TIMEOUT: Duration = Duration::from_secs(10);

struct ExitInner {
    state: Mutex<Option<i32>>,
    signal: Condvar,
    watchdog_armed: AtomicBool,
    cancelled: AtomicBool,
}

/// 退出协调器。进程生命周期内持有（通常由组合根静态持有）。
pub struct ExitCoordinator {
    inner: Arc<ExitInner>,
}

impl Default for ExitCoordinator {
    fn default() -> Self {
        Self::new()
    }
}

impl ExitCoordinator {
    pub fn new() -> Self {
        Self {
            inner: Arc::new(ExitInner {
                state: Mutex::new(None),
                signal: Condvar::new(),
                watchdog_armed: AtomicBool::new(false),
                cancelled: AtomicBool::new(false),
            }),
        }
    }

    /// 请求退出。首个请求的退出码生效，后续请求合并；同时武装看门狗（仅一次）。
    pub fn request_exit(&self, code: i32) -> i32 {
        let inner = &self.inner;
        let mut state = inner.state.lock().expect("退出状态锁不得中毒");
        let effective = *state.get_or_insert(code);
        let first = state.is_some() && !inner.watchdog_armed.swap(true, Ordering::AcqRel);
        inner.signal.notify_all();
        drop(state);
        if first {
            self.spawn_watchdog();
        }
        effective
    }

    /// 当前是否已请求退出（含生效退出码）。
    pub fn exit_requested(&self) -> Option<i32> {
        *self.inner.state.lock().expect("退出状态锁不得中毒")
    }

    /// 阻塞等待退出请求（供主循环代替轮询），带超时。
    pub fn wait_for_exit_request(&self, timeout: Duration) -> Option<i32> {
        let inner = &self.inner;
        let mut state = inner.state.lock().expect("退出状态锁不得中毒");
        if state.is_none() {
            let (guard, _result) = inner
                .signal
                .wait_timeout_while(state, timeout, |pending| pending.is_none())
                .expect("退出等待不得中毒");
            state = guard;
        }
        *state
    }

    /// 仅测试用：取消看门狗，避免测试进程被强制终止。
    #[doc(hidden)]
    pub fn cancel_watchdog_for_tests(&self) {
        self.inner.cancelled.store(true, Ordering::Release);
        self.inner.signal.notify_all();
    }

    fn spawn_watchdog(&self) {
        let inner = Arc::clone(&self.inner);
        let spawned = std::thread::Builder::new()
            .name("afmb-exit-watchdog".to_owned())
            .spawn(move || {
                // 不持有日志/调度器/任何锁；只等待、检查、终止。
                let guard = inner.state.lock().expect("看门狗等待锁不得中毒");
                let code = guard.unwrap_or(0);
                drop(guard);
                let (state_guard, _timeout) = inner
                    .signal
                    .wait_timeout_while(
                        inner.state.lock().expect("看门狗等待锁不得中毒"),
                        WATCHDOG_TIMEOUT,
                        |_| !inner.cancelled.load(Ordering::Acquire),
                    )
                    .expect("看门狗等待不得中毒");
                drop(state_guard);
                if inner.cancelled.load(Ordering::Acquire) {
                    return;
                }
                loop {
                    unsafe {
                        let _ = TerminateProcess(GetCurrentProcess(), code as u32);
                    }
                    std::thread::sleep(Duration::from_millis(50));
                }
            });
        if spawned.is_err() {
            // 看门狗无法启动：保持原有语义（正常退出路径不依赖看门狗），仅放弃强制兜底。
            self.inner.watchdog_armed.store(false, Ordering::Release);
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn first_request_wins_and_merges() {
        let coordinator = ExitCoordinator::new();
        assert_eq!(coordinator.request_exit(3), 3);
        assert_eq!(coordinator.request_exit(9), 3, "后续请求合并，退出码取首个");
        assert_eq!(coordinator.exit_requested(), Some(3));
        coordinator.cancel_watchdog_for_tests();
    }

    #[test]
    fn wait_unblocks_after_request() {
        let coordinator = Arc::new(ExitCoordinator::new());
        let waiter = Arc::clone(&coordinator);
        let handle =
            std::thread::spawn(move || waiter.wait_for_exit_request(Duration::from_secs(5)));
        std::thread::sleep(Duration::from_millis(50));
        coordinator.request_exit(2);
        let observed = handle.join().expect("等待线程不得 panic");
        assert_eq!(observed, Some(2));
        coordinator.cancel_watchdog_for_tests();
    }

    #[test]
    fn wait_times_out_without_request() {
        let coordinator = ExitCoordinator::new();
        let started = std::time::Instant::now();
        assert_eq!(
            coordinator.wait_for_exit_request(Duration::from_millis(60)),
            None
        );
        assert!(started.elapsed() >= Duration::from_millis(55));
        coordinator.cancel_watchdog_for_tests();
    }
}
