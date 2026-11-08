//! 代际计数：后台结果应用前的"是否仍是最新"检查。
//!
//! 对应 C# 中 `_generation` / `_mediaRevision` / `_captureVersion` 等模式：
//! 后台任务启动时取走当前代号，投递结果时若代号已前进则丢弃，禁止旧结果覆盖新状态。

use std::sync::atomic::{AtomicU64, Ordering};

#[derive(Debug, Default)]
pub struct Generation {
    counter: AtomicU64,
}

impl Generation {
    pub fn new() -> Self {
        Self::default()
    }

    /// 取走下一个代号并返回它（同时返回递增后的当前值，即"新起点"）。
    pub fn next(&self) -> u64 {
        self.counter.fetch_add(1, Ordering::AcqRel) + 1
    }

    /// 当前代号。
    pub fn current(&self) -> u64 {
        self.counter.load(Ordering::Acquire)
    }

    /// 某次后台取用的代号是否仍是当前代号。
    pub fn is_current(&self, taken: u64) -> bool {
        self.current() == taken
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn monotonically_advances_and_validates() {
        let g = Generation::new();
        assert_eq!(g.current(), 0);

        let first = g.next();
        assert_eq!(first, 1);
        assert!(g.is_current(first));

        g.next();
        assert!(!g.is_current(first), "旧代号在前进后必须失效");
        assert_eq!(g.current(), 2);
    }
}
