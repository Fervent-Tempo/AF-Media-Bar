//! AF Media Bar（Rust 版）组合根——对应 C# `App.xaml.cs`。
//!
//! 启动顺序（M0 范围，随里程碑扩展，顺序承自 C# `StartApplicationAsync`）：
//! 1. 解析命令行（测试覆盖项：`--afmb-settings-dir=`、`--afmb-smoke-exit`）
//! 2. 日志初始化 + 全局未捕获异常钩子（后台异常必须被观察）
//! 3. 单实例互斥体（重复实例提示后退出，退出码 2）
//! 4. 设置加载（含隔离/.bak 恢复），随后本地化解析
//! 5. 退出协调器 + UI 调度器消息循环
//! 6. 有序收尾：设置落盘 → 日志会话结束 → 退出
//!
//! 已发布 ID 一律不变：互斥体名、设置路径、schema v2。

use std::path::PathBuf;
use std::sync::Arc;
use std::time::Duration;

use afmb_core::dispatcher::Dispatcher;
use afmb_core::exit::ExitCoordinator;
use afmb_core::log::AppLog;
use afmb_core::thread::{set_uncaught_hook, spawn_observed};
use afmb_l10n::{Localization, option::InterfaceLanguageOption};
use afmb_platform::single_instance::{SINGLE_INSTANCE_MUTEX_NAME, SingleInstanceGuard};
use afmb_settings::prelude::{InterfaceLanguage, SettingsStore};
use windows::Win32::UI::WindowsAndMessaging::{
    MB_ICONINFORMATION, MB_OK, MB_SETFOREGROUND, MB_TOPMOST, MessageBoxW,
};
use windows::core::PCWSTR;

/// 命令行选项（仅测试/诊断用；发布形态无参数）。
#[derive(Debug, Default, Clone)]
struct LaunchArgs {
    /// 覆盖设置目录（隔离真机用户数据）。
    settings_dir: Option<PathBuf>,
    /// 冒烟模式：启动完成后立即请求退出；不弹任何交互框。
    smoke_exit: bool,
    /// 覆盖单实例互斥体名（仅自动化测试，发布形态一律用已发布名）。
    single_instance_name: Option<String>,
}

impl LaunchArgs {
    fn parse() -> Self {
        let mut args = Self::default();
        for arg in std::env::args().skip(1) {
            if let Some(dir) = arg.strip_prefix("--afmb-settings-dir=") {
                // 空值回退默认目录：空路径会解析为 CWD，导致运行时产物落进源码树。
                let trimmed = dir.trim();
                if !trimmed.is_empty() {
                    args.settings_dir = Some(PathBuf::from(trimmed));
                }
            } else if arg == "--afmb-smoke-exit" {
                args.smoke_exit = true;
            } else if let Some(name) = arg.strip_prefix("--afmb-single-instance-name=") {
                let name = name.trim();
                if !name.is_empty() {
                    args.single_instance_name = Some(name.to_owned());
                }
            }
        }
        args
    }
}

fn main() {
    let code = run();
    std::process::exit(code);
}

fn run() -> i32 {
    let args = LaunchArgs::parse();

    // —— 单实例（C# 顺序：先于日志与设置，被拒实例不写任何日志/设置）——
    let mutex_name = args
        .single_instance_name
        .as_deref()
        .unwrap_or(SINGLE_INSTANCE_MUTEX_NAME);
    let guard = match SingleInstanceGuard::acquire(mutex_name) {
        Ok(guard) => guard,
        Err(_) => {
            if !args.smoke_exit {
                // 提示语言按系统语言解析（与 C# 一致：单实例检查先于设置加载）。
                let l10n = Localization::resolve(InterfaceLanguageOption::System);
                show_already_running_box(&l10n.text("Startup.SingleInstance.AlreadyRunning"));
            }
            return 2;
        }
    };

    // —— 日志与异常观察 ——
    let base_dir = args.settings_dir.clone().unwrap_or_else(default_base_dir);
    let log = AppLog::init(base_dir.join("logs"));
    set_uncaught_hook({
        let log = Arc::clone(&log);
        Box::new(move |what| log.error("Uncaught", what))
    });
    log.session_start(env!("CARGO_PKG_VERSION"));
    log.info("Startup", format!("单实例互斥体已持有: {mutex_name}"));

    // —— 设置加载 ——
    let store = match SettingsStore::open(args.settings_dir.clone(), None, Some(Arc::clone(&log))) {
        Ok(store) => store,
        Err(error) => {
            log.error("Startup", format!("设置加载失败: {error}"));
            log.flush(Duration::from_secs(2));
            return 3;
        }
    };

    // —— 本地化（先于任何 UI 表面，对齐 C# 顺序）——
    let l10n = Localization::resolve(map_language(store.current().interface_language));
    log.info(
        "Startup",
        format!(
            "界面语言: {}（条目 {}）",
            l10n.language().resource_id(),
            afmb_l10n::Catalog::entry_count(l10n.language())
        ),
    );

    // —— 退出协调与 UI 调度器 ——
    let exit_coordinator = Arc::new(ExitCoordinator::new());
    let ui = match Dispatcher::for_current_thread() {
        Ok(dispatcher) => dispatcher,
        Err(error) => {
            log.error("Startup", format!("UI 调度器启动失败: {error:?}"));
            return 4;
        }
    };

    // 后台观察退出请求并终止消息循环。
    {
        let exit_coordinator = Arc::clone(&exit_coordinator);
        let ui = ui.clone();
        let log = Arc::clone(&log);
        let _ = spawn_observed("afmb-exit-watcher", move || {
            loop {
                if let Some(_code) =
                    exit_coordinator.wait_for_exit_request(Duration::from_secs(60 * 60 * 24 * 365))
                {
                    log.info("Exit", "收到退出请求，结束消息循环");
                    ui.stop();
                    return;
                }
            }
        });
    }

    if args.smoke_exit {
        log.info("Startup", "冒烟模式：启动完成后立即请求退出");
        exit_coordinator.request_exit(0);
    }

    log.info("Startup", "进入消息循环");
    ui.run();

    // —— 有序收尾（顺序承自 C# OnExit）——
    store.shutdown();
    log.session_end("正常退出");
    log.flush(Duration::from_secs(2));
    drop(guard);
    exit_coordinator.exit_requested().unwrap_or(0)
}

fn map_language(value: InterfaceLanguage) -> InterfaceLanguageOption {
    match value {
        InterfaceLanguage::System => InterfaceLanguageOption::System,
        InterfaceLanguage::SimplifiedChinese => InterfaceLanguageOption::SimplifiedChinese,
        InterfaceLanguage::TraditionalChinese => InterfaceLanguageOption::TraditionalChinese,
        InterfaceLanguage::English => InterfaceLanguageOption::English,
        InterfaceLanguage::Vietnamese => InterfaceLanguageOption::Vietnamese,
    }
}

fn default_base_dir() -> PathBuf {
    std::env::var_os("LOCALAPPDATA")
        .map(PathBuf::from)
        .unwrap_or_else(|| PathBuf::from("."))
        .join("AFMediaBar")
}

fn show_already_running_box(message: &str) {
    let text: Vec<u16> = message.encode_utf16().chain(std::iter::once(0)).collect();
    let title: Vec<u16> = "AF Media Bar"
        .encode_utf16()
        .chain(std::iter::once(0))
        .collect();
    unsafe {
        MessageBoxW(
            None,
            PCWSTR(text.as_ptr()),
            PCWSTR(title.as_ptr()),
            MB_OK | MB_ICONINFORMATION | MB_SETFOREGROUND | MB_TOPMOST,
        );
    }
}
