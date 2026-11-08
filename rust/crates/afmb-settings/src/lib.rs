//! AF Media Bar 设置层：schema v2 模型、持久化、隔离与「我的默认设置」。
//!
//! 对应 C# `Classes/Settings/` + `Classes/Services/Settings/`。
//! 兼容契约见 `docs/settings-contracts.md` / `docs/settings-contracts.json`；
//! 枚举成员值只能追加、不能改值或重排；schema 只在发布批次变更。
//!
//! - [`model::AppSettings`]：根标量字段全量类型化；分组设置经 `extra` 透传，
//!   随里程碑逐组类型化（读写不丢字段）。
//! - [`store::SettingsStore`]：加载 / `.bak` 恢复 / 隔离 / 去抖原子保存 / user-defaults。
//! - [`enums`]：根枚举（宽松读取、PascalCase 写出、文档化默认回落）。

pub mod enums;
pub mod model;
pub mod store;

pub mod prelude {
    pub use crate::enums::{
        DynamicIslandBackgroundMode, DynamicIslandEdge, InterfaceLanguage, LayoutOrientationMode,
        LyricsChineseConversionMode, LyricsSecondaryLineMode, LyricsTextAlignment,
        TaskbarBarPosition, TrayWheelBehavior, WindowMode,
    };
    pub use crate::model::AppSettings;
    pub use crate::store::{CURRENT_SCHEMA_VERSION, SettingsError, SettingsStore};
}
