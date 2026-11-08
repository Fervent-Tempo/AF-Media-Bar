//! AF Media Bar 内存清理提权助手（Rust 版骨架）。
//!
//! 对应 C# `AFMediaBar.MemoryHelper`（M5 里程碑实现）：
//! - 仅接受固定操作：`--run <standard|deep> <guid>`、`--install/--revoke <sid> <guid>`、`--uninstall`；
//! - 结果经命名管道 `AFMB.Memory.<nonce>` 单向回传；装管前校验服务端 PID 为 AFMediaBar；
//! - 60 秒看门狗；需管理员权限。
//!
//! 已发布 ID（管道前缀、计划任务名）不变。

fn main() {
    // M5: 管道协议与计划任务注册。
    std::process::exit(87); // 未实现的参数直接以错误码退出
}
