//! 后台线程启动 + 异常观察：落实"后台异常必须被观察"规则。
//!
//! 全局未捕获钩子 [`set_uncaught_hook`] 由应用启动时接入 `AppLog`；
//! [`spawn_observed`] 与调度器回调共用该钩子，默认退回 stderr。

use std::panic::{AssertUnwindSafe, catch_unwind};
use std::sync::{Mutex, OnceLock};
use std::thread::JoinHandle;

type UncaughtHook = Box<dyn Fn(&str) + Send + Sync>;

static UNCAUGHT_HOOK: OnceLock<Mutex<Option<UncaughtHook>>> = OnceLock::new();

/// 注册全局未捕获回调钩子（应用启动时接入 `AppLog`）。
pub fn set_uncaught_hook(hook: UncaughtHook) {
    UNCAUGHT_HOOK
        .get_or_init(|| Mutex::new(None))
        .lock()
        .expect("uncaught 钩子锁不得中毒")
        .replace(hook);
}

/// 报告被观察的异常（钩子未注册时退回 stderr）。
pub fn report_uncaught(where_: &str, what: &str) {
    let guard = UNCAUGHT_HOOK.get().and_then(|m| m.lock().ok());
    let delivered = match guard {
        Some(guard) => match guard.as_ref() {
            Some(hook) => {
                hook(&format!("[{where_}] {what}"));
                true
            }
            None => false,
        },
        None => false,
    };
    if !delivered {
        eprintln!("[afmb-core] 未捕获异常 [{where_}]: {what}");
    }
}

/// 把 panic 值转成可读文本。
pub fn panic_message(panic: &(dyn std::any::Any + Send)) -> String {
    panic
        .downcast_ref::<&str>()
        .map(|s| (*s).to_owned())
        .or_else(|| panic.downcast_ref::<String>().cloned())
        .unwrap_or_else(|| "未知 panic".to_owned())
}

/// 测试专用：全局钩子是进程级单例，注册它的测试共用这把锁串行化。
#[cfg(test)]
pub(crate) fn test_hook_lock() -> &'static Mutex<()> {
    static LOCK: Mutex<()> = Mutex::new(());
    &LOCK
}

/// 启动后台线程；闭包 panic 被捕获并转发全局钩子，不会静默丢失。
pub fn spawn_observed<F>(name: &str, f: F) -> std::io::Result<JoinHandle<()>>
where
    F: FnOnce() + Send + 'static,
{
    let name_owned = name.to_owned();
    std::thread::Builder::new()
        .name(name_owned.clone())
        .spawn(move || {
            if let Err(panic) = catch_unwind(AssertUnwindSafe(f)) {
                report_uncaught(&name_owned, &panic_message(&*panic));
            }
        })
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::atomic::{AtomicUsize, Ordering};
    use std::sync::{Arc, Mutex};

    #[test]
    fn observed_thread_reports_panic_via_hook() {
        let _hook_guard = test_hook_lock().lock().unwrap();
        static CALLS: AtomicUsize = AtomicUsize::new(0);
        set_uncaught_hook(Box::new(|_| {
            CALLS.fetch_add(1, Ordering::AcqRel);
        }));
        let handle =
            spawn_observed("test-observed", || panic!("observed-panic-42")).expect("线程应能启动");
        handle.join().expect("panic 已被捕获，join 正常");
        assert!(CALLS.load(Ordering::Acquire) >= 1, "panic 必须经过钩子");

        // 钩子内记录的具体文本（同一测试内二次注册，仍在锁内）。
        let seen = Arc::new(Mutex::new(None::<String>));
        set_uncaught_hook({
            let seen = Arc::clone(&seen);
            Box::new(move |what| *seen.lock().unwrap() = Some(what.to_owned()))
        });
        let handle = spawn_observed("unit-thread", || panic!("specific-99")).expect("线程应能启动");
        handle.join().expect("join 正常");
        let observed = seen.lock().unwrap().clone();
        assert_eq!(observed.as_deref(), Some("[unit-thread] specific-99"));
    }
}
