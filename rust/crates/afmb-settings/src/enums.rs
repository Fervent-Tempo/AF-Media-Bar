//! 根设置枚举：名称与值参与既有序列化契约（`docs/settings-contracts.json`）。
//!
//! 序列化语义对齐 C# `LenientEnumConverter<T>`：
//! - 写出 PascalCase 成员名；
//! - 读取接受大小写不敏感的成员名或已定义的数值；
//! - 未识别输入回落到该枚举的文档化默认（默认值与 C# `DefaultValue()` 映射一致）。
//!
//! 成员值只能追加、不能改值或重排（发布批次约束）。

use serde::de::Deserializer;
use serde::ser::Serializer;
use serde::{Deserialize, Serialize};
use std::fmt;

/// 为根枚举生成类型、序列化与宽松反序列化。
macro_rules! lenient_enum {
    (
        $(#[$meta:meta])*
        pub enum $name:ident {
            $($variant:ident = $value:literal),+ $(,)?
        }
        default = $default:ident
    ) => {
        $(#[$meta])*
        #[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
        #[repr(u8)]
        pub enum $name {
            $($variant = $value),+
        }

        impl $name {
            /// 全部成员（顺序即声明顺序，仅供遍历）。
            pub const ALL: &'static [Self] = &[$(Self::$variant),+];

            /// PascalCase 成员名（与 C# `ToString()` 一致）。
            pub fn as_str(self) -> &'static str {
                match self {
                    $(Self::$variant => stringify!($variant)),+
                }
            }

            /// 按成员名大小写不敏感解析。
            pub fn parse_name(text: &str) -> Option<Self> {
                $(
                    if text.eq_ignore_ascii_case(stringify!($variant)) {
                        return Some(Self::$variant);
                    }
                )+
                None
            }

            /// 按已定义数值解析。
            pub fn from_value(value: u8) -> Option<Self> {
                match value {
                    $($value => Some(Self::$variant),)+
                    _ => None,
                }
            }
        }

        impl Default for $name {
            fn default() -> Self {
                Self::$default
            }
        }

        impl fmt::Display for $name {
            fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
                f.write_str(self.as_str())
            }
        }

        impl Serialize for $name {
            fn serialize<S: Serializer>(&self, serializer: S) -> Result<S::Ok, S::Error> {
                serializer.serialize_str(self.as_str())
            }
        }

        impl<'de> Deserialize<'de> for $name {
            fn deserialize<D: Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
                let raw = serde_json::Value::deserialize(deserializer)?;
                Ok(match raw {
                    serde_json::Value::String(text) => Self::parse_name(&text),
                    serde_json::Value::Number(number) => number
                        .as_u64()
                        .and_then(|n| u8::try_from(n).ok())
                        .and_then(Self::from_value),
                    _ => None,
                }
                .unwrap_or_default())
            }
        }
    };
}

lenient_enum! {
    /// 任务栏位置。
    pub enum TaskbarBarPosition {
        Start = 0,
        Center = 1,
        End = 2,
    }
    default = Start
}

lenient_enum! {
    /// 布局方向模式。
    pub enum LayoutOrientationMode {
        Auto = 0,
        Horizontal = 1,
        Vertical = 2,
    }
    default = Auto
}

lenient_enum! {
    /// 灵动岛背景模式。
    pub enum DynamicIslandBackgroundMode {
        SystemTheme = 0,
        Transparent = 1,
    }
    default = SystemTheme
}

lenient_enum! {
    /// 托盘滚轮行为。
    pub enum TrayWheelBehavior {
        AdjustVolume = 0,
        SwitchOutputDevice = 1,
        Disabled = 2,
        SwitchInputDevice = 3,
    }
    default = SwitchOutputDevice
}

lenient_enum! {
    /// 双行歌词第二行模式。
    pub enum LyricsSecondaryLineMode {
        NextLine = 0,
        Translation = 1,
        Romanization = 2,
    }
    default = Translation
}

lenient_enum! {
    /// 歌词文本简繁转换方向（只影响呈现）。
    pub enum LyricsChineseConversionMode {
        None = 0,
        SimplifiedToTraditional = 1,
        TraditionalToSimplified = 2,
    }
    default = None
}

lenient_enum! {
    /// 窗口模式（灵动岛等未实现标识继续回退到任务栏）。
    pub enum WindowMode {
        Taskbar = 0,
        DynamicIsland = 1,
        FloatingBar = 2,
    }
    default = Taskbar
}

lenient_enum! {
    /// 灵动岛暂停时隐藏到的桌面边缘。
    pub enum DynamicIslandEdge {
        Top = 0,
        Right = 1,
        Bottom = 2,
        Left = 3,
    }
    default = Top
}

lenient_enum! {
    /// 歌词文字对齐方式。
    pub enum LyricsTextAlignment {
        Left = 0,
        Center = 1,
        Right = 2,
    }
    default = Center
}

lenient_enum! {
    /// 界面语言选项（实际生效语言由 l10n 服务解析）。
    pub enum InterfaceLanguage {
        System = 0,
        SimplifiedChinese = 1,
        TraditionalChinese = 2,
        English = 3,
        Vietnamese = 4,
    }
    default = System
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn serializes_as_pascal_case_name() {
        assert_eq!(
            serde_json::to_value(TrayWheelBehavior::SwitchOutputDevice).unwrap(),
            "SwitchOutputDevice"
        );
        assert_eq!(
            serde_json::to_value(InterfaceLanguage::SimplifiedChinese).unwrap(),
            "SimplifiedChinese"
        );
    }

    #[test]
    fn reads_name_case_insensitively_and_by_number() {
        let value: TrayWheelBehavior = serde_json::from_str("\"switchoutputdevice\"").unwrap();
        assert_eq!(value, TrayWheelBehavior::SwitchOutputDevice);
        let value: TaskbarBarPosition = serde_json::from_str("2").unwrap();
        assert_eq!(value, TaskbarBarPosition::End);
    }

    #[test]
    fn unknown_input_falls_back_to_documented_default() {
        let value: TrayWheelBehavior = serde_json::from_str("\"SomethingElse\"").unwrap();
        assert_eq!(
            value,
            TrayWheelBehavior::SwitchOutputDevice,
            "默认值须与 C# DefaultValue() 一致"
        );
        let value: LyricsSecondaryLineMode = serde_json::from_str("99").unwrap();
        assert_eq!(value, LyricsSecondaryLineMode::Translation);
        let value: WindowMode = serde_json::from_value(serde_json::Value::Null).unwrap();
        assert_eq!(value, WindowMode::Taskbar);
    }

    #[test]
    fn member_values_match_published_contract() {
        assert_eq!(TaskbarBarPosition::Center as u8, 1);
        assert_eq!(
            LyricsChineseConversionMode::TraditionalToSimplified as u8,
            2
        );
        assert_eq!(InterfaceLanguage::Vietnamese as u8, 4);
    }
}
