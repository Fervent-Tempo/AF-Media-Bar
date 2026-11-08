//! AF Media Bar Windows 平台绑定层。
//!
//! 对应 C# `Classes/Interop/` 与各平台服务中的 Win32 调用。
//! 按里程碑填充：M0 单实例；M1 任务栏查找/停靠、显示器、DWM、Raw Input；
//! M2 Core Audio、UIA；M4 DWM 材质；M5 电源、进程内存。

pub mod single_instance;

pub mod prelude {
    pub use crate::single_instance::{SINGLE_INSTANCE_MUTEX_NAME, SingleInstanceGuard};
}
