//! 每线程调度器：Win32 消息循环 + 闭包投递，替代 WPF `Dispatcher`。
//!
//! - [`Dispatcher::spawn_named`]：启动专用宿主线程（托盘、Raw Input 等各自独立）。
//! - [`Dispatcher::for_current_thread`] + [`Dispatcher::run`]：主线程作 UI 线程。
//! - [`Dispatcher::post`]：跨线程投递 `FnOnce`；停止后返回 [`DispatchError::Stopped`]，
//!   落实"释放后不向 UI 发布结果"。
//! - 回调 panic 被 [`catch_unwind`] 捕获并转发全局钩子（后台异常必须被观察），
//!   默认输出 stderr，应用层用 `set_uncaught_hook` 接入日志。

use std::cell::RefCell;
use std::panic::{AssertUnwindSafe, catch_unwind};
use std::sync::atomic::{AtomicBool, AtomicIsize, AtomicU32, Ordering};
use std::sync::{Arc, OnceLock};

use windows::Win32::Foundation::{HWND, LPARAM, LRESULT, WPARAM};
use windows::Win32::System::LibraryLoader::GetModuleHandleW;
use windows::Win32::System::Threading::GetCurrentThreadId;
use windows::Win32::UI::WindowsAndMessaging::{
    CreateWindowExW, DefWindowProcW, DestroyWindow, DispatchMessageW, GetMessageW, HWND_MESSAGE,
    MSG, PEEK_MESSAGE_REMOVE_TYPE, PeekMessageW, PostMessageW, PostThreadMessageW, RegisterClassW,
    TranslateMessage, WINDOW_EX_STYLE, WINDOW_STYLE, WM_APP, WM_QUIT, WNDCLASSW,
};

/// PeekMessageW 的 PM_REMOVE。
const PM_REMOVE: PEEK_MESSAGE_REMOVE_TYPE = PEEK_MESSAGE_REMOVE_TYPE(1);
use windows::core::{PCWSTR, w};

use crate::thread::{panic_message, report_uncaught};

/// 投递回调消息：WM_APP 区段内自用值。
const WM_APP_DISPATCH: u32 = WM_APP + 0x4D42;
const CLASS_NAME: PCWSTR = w!("AFMB.Dispatcher");

static CLASS_REGISTERED: OnceLock<()> = OnceLock::new();

thread_local! {
    static CURRENT: RefCell<Option<Arc<Inner>>> = const { RefCell::new(None) };
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum DispatchError {
    /// 调度器已停止：按架构规则，停止后不得再发布结果。
    Stopped,
    /// 宿主线程/消息窗口创建失败。
    Startup(String),
}

#[derive(Debug, Default)]
struct Inner {
    thread_id: AtomicU32,
    hwnd: AtomicIsize,
    running: AtomicBool,
    stopped: AtomicBool,
}

impl Inner {
    fn hwnd(&self) -> Option<HWND> {
        let raw = self.hwnd.load(Ordering::Acquire);
        if raw == 0 {
            None
        } else {
            Some(HWND(raw as *mut core::ffi::c_void))
        }
    }
}

/// 线程调度器句柄。克隆廉价；`Inner` 存活至所有引用消失。
#[derive(Clone)]
pub struct Dispatcher {
    inner: Arc<Inner>,
}

impl std::fmt::Debug for Dispatcher {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Dispatcher")
            .field("thread_id", &self.inner.thread_id.load(Ordering::Acquire))
            .field("stopped", &self.inner.stopped.load(Ordering::Acquire))
            .finish()
    }
}

impl Dispatcher {
    /// 为当前线程创建调度器（消息窗口立即创建，消息循环随后由 [`Dispatcher::run`] 进入）。
    pub fn for_current_thread() -> Result<Self, DispatchError> {
        let inner = Arc::new(Inner::default());
        let hwnd = create_message_window().map_err(DispatchError::Startup)?;
        inner
            .thread_id
            .store(unsafe { GetCurrentThreadId() }, Ordering::Release);
        inner.hwnd.store(hwnd.0 as isize, Ordering::Release);
        let dispatcher = Self { inner };
        CURRENT.with(|slot| *slot.borrow_mut() = Some(Arc::clone(&dispatcher.inner)));
        Ok(dispatcher)
    }

    /// 在专用线程上启动调度器，返回就绪的句柄（窗口已创建）。
    pub fn spawn_named(name: &str) -> Result<Self, DispatchError> {
        let inner = Arc::new(Inner::default());
        let (ready_tx, ready_rx) = std::sync::mpsc::channel();
        let name_owned = name.to_owned();
        let thread_inner = Arc::clone(&inner);
        std::thread::Builder::new()
            .name(name_owned.clone())
            .spawn(move || {
                let outcome = enter_thread(&thread_inner);
                let ready = outcome.is_ok();
                let _ = ready_tx.send(outcome);
                if ready {
                    run_message_loop(&thread_inner);
                }
            })
            .map_err(|_| DispatchError::Startup(format!("线程 {name_owned} 创建失败")))?;
        ready_rx
            .recv()
            .map_err(|_| DispatchError::Startup(format!("调度线程 {name_owned} 提前退出")))?
            .map(|_| Self { inner })
    }

    /// 进入当前线程的消息循环，直至 [`Dispatcher::stop`]。仅用于 `for_current_thread`。
    pub fn run(&self) {
        run_message_loop(&self.inner);
    }

    /// 投递闭包到调度线程。已停止时返回 [`DispatchError::Stopped`]。
    pub fn post<F>(&self, job: F) -> Result<(), DispatchError>
    where
        F: FnOnce() + Send + 'static,
    {
        if self.inner.stopped.load(Ordering::Acquire) {
            return Err(DispatchError::Stopped);
        }
        let Some(hwnd) = self.inner.hwnd() else {
            return Err(DispatchError::Stopped);
        };
        let boxed: Box<Box<dyn FnOnce() + Send>> = Box::new(Box::new(job));
        let raw = Box::into_raw(boxed);
        let posted =
            unsafe { PostMessageW(Some(hwnd), WM_APP_DISPATCH, WPARAM(0), LPARAM(raw as isize)) };
        if posted.is_err() {
            // 队列不可用：就地回收，避免泄漏；语义等同"未发布"。
            drop(unsafe { Box::from_raw(raw) });
            self.inner.stopped.store(true, Ordering::Release);
            return Err(DispatchError::Stopped);
        }
        Ok(())
    }

    /// 请求停止（幂等）。已在队列中的回调仍会执行，之后消息循环退出。
    pub fn stop(&self) {
        if self.inner.stopped.swap(true, Ordering::AcqRel) {
            return;
        }
        let thread_id = self.inner.thread_id.load(Ordering::Acquire);
        if thread_id != 0 {
            // 重试数次应对线程队列尚未就绪的窗口期。
            for _ in 0..50 {
                let posted =
                    unsafe { PostThreadMessageW(thread_id, WM_QUIT, WPARAM(0), LPARAM(0)) };
                if posted.is_ok() {
                    break;
                }
                std::thread::sleep(std::time::Duration::from_millis(2));
            }
        }
    }

    pub fn is_stopped(&self) -> bool {
        self.inner.stopped.load(Ordering::Acquire)
    }

    /// 当前线程的调度器（仅该线程上有效）。
    pub fn current() -> Option<Self> {
        CURRENT.with(|slot| {
            slot.borrow().as_ref().map(|inner| Self {
                inner: Arc::clone(inner),
            })
        })
    }

    /// 宿主线程 ID（未就绪时为 0）。
    pub fn thread_id(&self) -> u32 {
        self.inner.thread_id.load(Ordering::Acquire)
    }
}

impl Drop for Inner {
    fn drop(&mut self) {
        let raw = self.hwnd.load(Ordering::Acquire);
        if raw != 0 {
            let _ = unsafe { DestroyWindow(HWND(raw as *mut core::ffi::c_void)) };
            self.hwnd.store(0, Ordering::Release);
        }
    }
}

/// 在当前线程创建消息窗口并登记线程状态（不进入消息循环）。
fn enter_thread(inner: &Arc<Inner>) -> Result<(), DispatchError> {
    let hwnd = create_message_window().map_err(DispatchError::Startup)?;
    inner
        .thread_id
        .store(unsafe { GetCurrentThreadId() }, Ordering::Release);
    inner.hwnd.store(hwnd.0 as isize, Ordering::Release);
    CURRENT.with(|slot| *slot.borrow_mut() = Some(Arc::clone(inner)));
    Ok(())
}

fn run_message_loop(inner: &Arc<Inner>) {
    inner.running.store(true, Ordering::Release);
    let mut msg = MSG::default();
    // GetMessageW 返回 0 即 WM_QUIT（-1 为错误，按 C# Dispatcher 同样忽略继续）。
    unsafe {
        while GetMessageW(&mut msg, None, 0, 0).0 != 0 {
            let _ = TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
    }
    // WM_QUIT 与投递消息存在竞态：WM_QUIT 之后再入队的 WM_APP_DISPATCH 不会被
    // GetMessage 派发。按"已在队列中的回调仍会执行"的语义就地排空（执行并回收 Box，
    // 不执行则泄漏）。此处 hwnd 过滤 + 消息段过滤，只取本窗口的投递回调。
    if let Some(hwnd) = inner.hwnd() {
        let mut pending = MSG::default();
        unsafe {
            while PeekMessageW(
                &mut pending,
                Some(hwnd),
                WM_APP_DISPATCH,
                WM_APP_DISPATCH,
                PM_REMOVE,
            )
            .as_bool()
            {
                run_dispatch_payload(pending.lParam);
            }
        }
    }
    inner.running.store(false, Ordering::Release);
    inner.stopped.store(true, Ordering::Release);
    CURRENT.with(|slot| *slot.borrow_mut() = None);
}

/// 执行一个投递回调（回收 Box、观察 panic）；wndproc 与排空共用。
fn run_dispatch_payload(lparam: LPARAM) {
    let raw = lparam.0 as *mut Box<dyn FnOnce() + Send>;
    if !raw.is_null() {
        let job = unsafe { Box::from_raw(raw) };
        if let Err(panic) = catch_unwind(AssertUnwindSafe(job)) {
            report_uncaught("dispatcher", &panic_message(&*panic));
        }
    }
}

fn create_message_window() -> Result<HWND, String> {
    register_class_once()?;
    let instance =
        unsafe { GetModuleHandleW(None) }.map_err(|e| format!("GetModuleHandleW 失败: {e}"))?;
    let hwnd = unsafe {
        CreateWindowExW(
            WINDOW_EX_STYLE(0),
            CLASS_NAME,
            w!(""),
            WINDOW_STYLE(0),
            0,
            0,
            0,
            0,
            Some(HWND_MESSAGE),
            None,
            Some(instance.into()),
            None,
        )
    }
    .map_err(|e| format!("CreateWindowExW 失败: {e}"))?;
    Ok(hwnd)
}

fn register_class_once() -> Result<(), String> {
    if CLASS_REGISTERED.get().is_some() {
        return Ok(());
    }
    let class_name: &'static [u16] = Box::leak(
        "AFMB.Dispatcher\0"
            .encode_utf16()
            .collect::<Vec<u16>>()
            .into_boxed_slice(),
    );
    let instance =
        unsafe { GetModuleHandleW(None) }.map_err(|e| format!("GetModuleHandleW 失败: {e}"))?;
    let class = WNDCLASSW {
        lpfnWndProc: Some(dispatch_wndproc),
        hInstance: instance.into(),
        lpszClassName: PCWSTR(class_name.as_ptr()),
        ..Default::default()
    };
    let atom = unsafe { RegisterClassW(&class) };
    if atom == 0 {
        // 并发注册竞态下的"已存在"视为成功；真实失败向上报告。
        let err = unsafe { windows::Win32::Foundation::GetLastError() };
        const ERROR_CLASS_ALREADY_EXISTS: u32 = 1410;
        if err.0 != ERROR_CLASS_ALREADY_EXISTS {
            return Err(format!("RegisterClassW 失败: {}", err.0));
        }
    }
    let _ = CLASS_REGISTERED.set(());
    Ok(())
}

unsafe extern "system" fn dispatch_wndproc(
    hwnd: HWND,
    msg: u32,
    wparam: WPARAM,
    lparam: LPARAM,
) -> LRESULT {
    if msg == WM_APP_DISPATCH {
        run_dispatch_payload(lparam);
        return LRESULT(0);
    }
    unsafe { DefWindowProcW(hwnd, msg, wparam, lparam) }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::atomic::AtomicUsize;

    use std::time::Duration;

    #[test]
    fn spawned_dispatcher_executes_posted_jobs() {
        let dispatcher = Dispatcher::spawn_named("test-dispatch").expect("调度器应能启动");
        let counter = Arc::new(AtomicUsize::new(0));
        let counter2 = Arc::clone(&counter);
        dispatcher
            .post(move || {
                counter2.store(7, Ordering::Release);
            })
            .expect("投递应成功");
        for _ in 0..500 {
            if counter.load(Ordering::Acquire) == 7 {
                break;
            }
            std::thread::sleep(Duration::from_millis(2));
        }
        assert_eq!(counter.load(Ordering::Acquire), 7);
        dispatcher.stop();
    }

    #[test]
    fn post_after_stop_is_rejected() {
        let dispatcher = Dispatcher::spawn_named("test-dispatch-stop").expect("调度器应能启动");
        dispatcher.stop();
        std::thread::sleep(Duration::from_millis(20));
        let outcome = dispatcher.post(|| {});
        assert_eq!(outcome, Err(DispatchError::Stopped));
    }

    #[test]
    fn callback_panic_is_observed_not_fatal() {
        let _hook_guard = crate::thread::test_hook_lock().lock().unwrap();
        let seen = Arc::new(std::sync::Mutex::new(None));
        crate::thread::set_uncaught_hook({
            let seen = Arc::clone(&seen);
            Box::new(move |what| *seen.lock().unwrap() = Some(what.to_owned()))
        });
        let dispatcher = Dispatcher::spawn_named("test-dispatch-panic").expect("调度器应能启动");
        dispatcher.post(|| panic!("boom-123")).expect("投递应成功");
        dispatcher.post(|| {}).expect("后续投递应成功");
        for _ in 0..500 {
            if seen.lock().unwrap().is_some() {
                break;
            }
            std::thread::sleep(Duration::from_millis(2));
        }
        let observed = seen.lock().unwrap().clone();
        dispatcher.stop();
        assert!(
            observed.as_deref().unwrap_or_default().contains("boom-123"),
            "panic 必须被钩子观察到，实际: {observed:?}"
        );
    }
}
