//! 单实例守卫：命名互斥体，进程存活期内持有。
//!
//! 对应 C# `SingleInstanceGuard`（`Local\AFMediaBar.SingleInstance`，内核句柄持有、
//! 不绑定线程）。已发布互斥体名不变。

use std::ffi::OsStr;
use std::os::windows::ffi::OsStrExt;

use windows::Win32::Foundation::{CloseHandle, ERROR_ALREADY_EXISTS, GetLastError, HANDLE};
use windows::Win32::System::Threading::CreateMutexW;
use windows::core::PCWSTR;

/// 已发布的单实例互斥体名（不得变更）。
pub const SINGLE_INSTANCE_MUTEX_NAME: &str = r"Local\AFMediaBar.SingleInstance";

/// 已运行错误。
#[derive(Debug)]
pub struct AlreadyRunning;

fn to_wide(text: &str) -> Vec<u16> {
    OsStr::new(text)
        .encode_wide()
        .chain(std::iter::once(0))
        .collect()
}

/// 尝试获取命名互斥体；他人已持有时返回 [`AlreadyRunning`]。
pub struct SingleInstanceGuard {
    handle: HANDLE,
    name: String,
}

impl SingleInstanceGuard {
    /// 获取互斥体（初始拥有）。进程退出/句柄释放即解除。
    pub fn acquire(name: &str) -> Result<Self, AlreadyRunning> {
        let wide = to_wide(name);
        let handle = unsafe { CreateMutexW(None, true, PCWSTR(wide.as_ptr())) }
            .map_err(|_| AlreadyRunning)?;
        if unsafe { GetLastError() } == ERROR_ALREADY_EXISTS {
            unsafe {
                let _ = CloseHandle(handle);
            }
            return Err(AlreadyRunning);
        }
        Ok(Self {
            handle,
            name: name.to_owned(),
        })
    }

    /// 守卫持有的互斥体名。
    pub fn name(&self) -> &str {
        &self.name
    }
}

impl Drop for SingleInstanceGuard {
    fn drop(&mut self) {
        unsafe {
            let _ = CloseHandle(self.handle);
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn second_acquire_is_rejected_while_first_holds() {
        let name = format!(r"Local\AFMediaBar.Test.{}", std::process::id());
        let first = SingleInstanceGuard::acquire(&name).expect("首次获取应成功");
        assert_eq!(first.name(), name);
        let second = SingleInstanceGuard::acquire(&name);
        assert!(second.is_err(), "持有人存续期间第二次获取必须失败");
        drop(first);
        let third = SingleInstanceGuard::acquire(&name).expect("释放后应可重新获取");
        drop(third);
    }

    #[test]
    fn published_mutex_name_unchanged() {
        assert_eq!(
            SINGLE_INSTANCE_MUTEX_NAME,
            r"Local\AFMediaBar.SingleInstance"
        );
    }
}
