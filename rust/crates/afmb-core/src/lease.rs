//! 可替换资源的租约：重建旧实例前，等最后一个使用者退出再释放。
//!
//! 对应 C# `Media/Smtc/ReplaceableResource.cs`：SMTC 管理器重建时，旧实例进入
//! "退役"状态，直到其上所有租约释放后才真正丢弃——避免使用中的实例被替换悬垂。

use std::sync::{Arc, Mutex};

struct Retired<T> {
    resource: Arc<T>,
}

struct State<T> {
    current: Arc<T>,
    retired: Vec<Retired<T>>,
}

/// 可替换资源。`replace` 之后旧实例仅在最后一个租约释放时销毁。
pub struct ReplaceableResource<T> {
    state: Mutex<State<T>>,
}

impl<T> ReplaceableResource<T> {
    pub fn new(current: T) -> Self {
        Self {
            state: Mutex::new(State {
                current: Arc::new(current),
                retired: Vec::new(),
            }),
        }
    }

    /// 取当前实例的租约（引用计数 +1）。
    pub fn lease(&self) -> ResourceLease<'_, T> {
        let state = self.state.lock().expect("ReplaceableResource 锁不得中毒");
        ResourceLease {
            resource: Arc::clone(&state.current),
            state: &self.state,
        }
    }

    /// 以新实例替换当前实例；旧实例进入退役列表，等全部租约释放后销毁。
    pub fn replace(&self, next: T) {
        let mut state = self.state.lock().expect("ReplaceableResource 锁不得中毒");
        let previous = Arc::clone(&state.current);
        state.current = Arc::new(next);
        // 只有仍被引用的旧实例才需要退役跟踪；无租约的立即消失。
        if Arc::strong_count(&previous) > 1 {
            state.retired.push(Retired { resource: previous });
        }
    }

    /// 当前实例（不取租约，仅供监控/诊断读取）。
    pub fn current(&self) -> Arc<T> {
        Arc::clone(
            &self
                .state
                .lock()
                .expect("ReplaceableResource 锁不得中毒")
                .current,
        )
    }

    /// 退役中（尚未释放）的旧实例数量。
    pub fn retiring_count(&self) -> usize {
        self.state
            .lock()
            .expect("ReplaceableResource 锁不得中毒")
            .retired
            .len()
    }
}

/// 资源租约：持有期间资源保证存活，释放时结算退役计数。
pub struct ResourceLease<'a, T> {
    resource: Arc<T>,
    state: &'a Mutex<State<T>>,
}

impl<T> std::ops::Deref for ResourceLease<'_, T> {
    type Target = T;

    fn deref(&self) -> &Self::Target {
        &self.resource
    }
}

impl<T> Drop for ResourceLease<'_, T> {
    fn drop(&mut self) {
        let mut state = self.state.lock().expect("ReplaceableResource 锁不得中毒");
        for index in (0..state.retired.len()).rev() {
            if Arc::ptr_eq(&state.retired[index].resource, &self.resource) {
                // 计数包含：退役列表项(1) + self.resource(1) + 其余存活租约。
                // 减去 self 之后只剩列表项本身，说明最后一个租约已退出。
                let remaining_without_self = Arc::strong_count(&state.retired[index].resource) - 1;
                if remaining_without_self == 1 {
                    state.retired.swap_remove(index);
                }
                return;
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn replace_retires_only_while_leases_live() {
        let resource: ReplaceableResource<Vec<u32>> = ReplaceableResource::new(vec![1]);
        let lease = resource.lease();
        assert_eq!(**lease, vec![1]);

        resource.replace(vec![2]);
        assert_eq!(resource.retiring_count(), 1, "仍有租约，旧实例必须退役等待");
        assert_eq!(**resource.current(), vec![2]);

        drop(lease);
        assert_eq!(resource.retiring_count(), 0, "最后一个租约退出后旧实例销毁");
    }

    #[test]
    fn replace_without_leases_frees_immediately() {
        let resource: ReplaceableResource<u8> = ReplaceableResource::new(1);
        resource.replace(2);
        assert_eq!(resource.retiring_count(), 0);
    }
}
