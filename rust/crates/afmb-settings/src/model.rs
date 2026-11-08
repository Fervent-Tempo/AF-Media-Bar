//! 应用设置根模型（schema v2）。
//!
//! 对齐 C# `Classes/Settings/AppSettings.cs`：
//! - 根标量字段全量类型化，字段名 camelCase（`rename_all`）；
//! - 分组设置（appearance、taskbarExperience、lyricsSource 等 19 组）按里程碑逐组类型化，
//!   未类型化阶段经 [`AppSettings::extra`] **原样透传**——读写不丢字段，等价于
//!   C# 端"缺失成员回落属性初始化默认值"的行为；
//! - [`AppSettings::normalize`] 移植 C# `Normalize()` 的根字段规则；
//!   分组自身的 Normalize 由各分组类型化的里程碑接管（代码内注明）。

use crate::enums::{
    DynamicIslandBackgroundMode, DynamicIslandEdge, InterfaceLanguage, LayoutOrientationMode,
    LyricsChineseConversionMode, LyricsSecondaryLineMode, LyricsTextAlignment, TaskbarBarPosition,
    TrayWheelBehavior, WindowMode,
};
use serde::{Deserialize, Serialize};

/// 任务栏横向偏移下限（DIP）。
pub const MINIMUM_TASKBAR_CROSS_AXIS_OFFSET_DIP: f64 = -20.0;
/// 任务栏横向偏移上限（DIP）。
pub const MAXIMUM_TASKBAR_CROSS_AXIS_OFFSET_DIP: f64 = 20.0;

/// 旧设置文件里表示"所有任务栏"的标识（只用于兼容迁移，不再写入）。
pub const LEGACY_ALL_TASKBARS_DEVICE_ID: &str = "{AFMediaBar.AllTaskbars}";

/// 歌词未唱层不透明度：默认/下限/上限（百分比）。
pub const LYRICS_UNSUNG_OPACITY_DEFAULT: i32 = 45;
pub const LYRICS_UNSUNG_OPACITY_MIN: i32 = 20;
pub const LYRICS_UNSUNG_OPACITY_MAX: i32 = 80;
/// 歌词字距：默认/下限/上限（字号百分比）。
pub const LYRICS_CHARACTER_SPACING_DEFAULT: i32 = 0;
pub const LYRICS_CHARACTER_SPACING_MIN: i32 = 0;
pub const LYRICS_CHARACTER_SPACING_MAX: i32 = 20;
/// 双行歌词行距：默认/下限/上限（字号百分比）。
pub const LYRICS_LINE_GAP_DEFAULT: i32 = 0;
pub const LYRICS_LINE_GAP_MIN: i32 = 0;
pub const LYRICS_LINE_GAP_MAX: i32 = 24;
/// 歌词框固定长度：默认/下限/上限（DIP）。
pub const LYRICS_FIXED_WIDTH_DEFAULT: i32 = 240;
pub const LYRICS_FIXED_WIDTH_MIN: i32 = 80;
pub const LYRICS_FIXED_WIDTH_MAX: i32 = 600;

fn clamp_i32(value: i32, min: i32, max: i32) -> i32 {
    value.clamp(min, max)
}

/// 应用全部用户设置。克隆廉价；修改经 [`crate::SettingsStore::update`] 发布。
///
/// `Default` 手写实现：与 C# 字段初始化器逐项对齐（bool 默认值不是 false 的一律在此声明），
/// `#[serde(default)]` 反序列化缺失字段时使用的就是它——等价于 C# 缺失成员回落字段初始化器。
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
#[serde(rename_all = "camelCase", default)]
pub struct AppSettings {
    // —— 外观分组（M0 透传；外观里程碑类型化）——
    // appearance / experiments / floatingBar / lyricsSecondaryLine / taskbarExperience /
    // audioDevices / interaction / taskbarSurface / dynamicIslandSurface / lyricsSource /
    // trackChangeNotification / smtcSourceFilter / mediaSelection / quickLaunch /
    // spectrumComponent / bluetoothBatteryWidget / systemMemoryOptimization /
    // performanceComponent / update 均经 extra 透传。
    pub tray_wheel_behavior: TrayWheelBehavior,

    pub lyrics_enabled: bool,
    pub allow_browser_and_video_lyrics: bool,
    pub two_line_lyrics_enabled: bool,
    /// 双行歌词第二行的来源顺序（列表顺序即优先级）。
    pub lyrics_secondary_line_mode: LyricsSecondaryLineMode,

    pub taskbar_bar_enabled: bool,
    /// 任务栏媒体栏的显式显示器集合；空/缺省回退主屏。
    pub taskbar_target_monitor_device_ids: Option<Vec<String>>,
    /// 旧版单目标字段，仅读取兼容；新写入统一使用 ids。
    pub taskbar_target_monitor_device_id: Option<String>,
    pub position: TaskbarBarPosition,
    pub taskbar_bar_background_blur: bool,
    pub taskbar_bar_manual_padding: i32,
    pub window_mode: WindowMode,
    pub layout_orientation_mode: LayoutOrientationMode,
    pub layout_length_scale_percent: f64,
    pub layout_thickness_scale_percent: f64,
    pub dynamic_island_background_mode: DynamicIslandBackgroundMode,
    pub taskbar_bar_cross_axis_offset_dip: f64,
    pub taskbar_bar_avoid_icons: bool,
    pub taskbar_bar_position_locked: bool,
    pub dynamic_island_left: Option<f64>,
    pub dynamic_island_top: Option<f64>,
    pub dynamic_island_edge: DynamicIslandEdge,
    pub dynamic_island_edge_docked: bool,

    pub lyrics_text_alignment: LyricsTextAlignment,
    pub lyrics_syllable_highlight_enabled: bool,
    pub lyrics_unsung_opacity_percent: i32,
    pub lyrics_character_spacing_percent: i32,
    pub lyrics_line_gap_percent: i32,
    pub lyrics_fixed_width_enabled: bool,
    pub lyrics_fixed_width_dip: i32,
    pub lyrics_long_line_scroll_enabled: bool,
    pub lyrics_info_line_filter_enabled: bool,
    pub lyrics_instrumental_title_only_enabled: bool,
    pub lyrics_furigana_filter_enabled: bool,
    pub lyrics_bracket_symbols_hidden: bool,
    pub lyrics_chinese_conversion: LyricsChineseConversionMode,
    pub lyrics_artist_separators: String,

    pub launch_at_startup: bool,
    pub developer_mode_enabled: bool,
    pub interface_language: InterfaceLanguage,
    /// TranslucentTB 兼容说明只提示一次；重置全部时不重置此项。
    pub translucent_tb_compatibility_prompt_shown: bool,

    /// 未类型化字段的透传容器（含全部分组设置）。
    #[serde(flatten)]
    pub extra: serde_json::Map<String, serde_json::Value>,
}

impl AppSettings {
    pub fn new() -> Self {
        Self::default()
    }
}

impl Default for AppSettings {
    /// 与 C# `AppSettings` 字段初始化器逐项一致。
    fn default() -> Self {
        Self {
            tray_wheel_behavior: TrayWheelBehavior::default(),
            lyrics_enabled: true,
            allow_browser_and_video_lyrics: false,
            two_line_lyrics_enabled: true,
            lyrics_secondary_line_mode: LyricsSecondaryLineMode::default(),
            taskbar_bar_enabled: true,
            taskbar_target_monitor_device_ids: None,
            taskbar_target_monitor_device_id: None,
            position: TaskbarBarPosition::default(),
            taskbar_bar_background_blur: false,
            taskbar_bar_manual_padding: 0,
            window_mode: WindowMode::default(),
            layout_orientation_mode: LayoutOrientationMode::default(),
            layout_length_scale_percent: 100.0,
            layout_thickness_scale_percent: 100.0,
            dynamic_island_background_mode: DynamicIslandBackgroundMode::default(),
            taskbar_bar_cross_axis_offset_dip: 0.0,
            taskbar_bar_avoid_icons: true,
            taskbar_bar_position_locked: false,
            dynamic_island_left: None,
            dynamic_island_top: None,
            dynamic_island_edge: DynamicIslandEdge::default(),
            dynamic_island_edge_docked: true,
            lyrics_text_alignment: LyricsTextAlignment::default(),
            lyrics_syllable_highlight_enabled: true,
            lyrics_unsung_opacity_percent: LYRICS_UNSUNG_OPACITY_DEFAULT,
            lyrics_character_spacing_percent: LYRICS_CHARACTER_SPACING_DEFAULT,
            lyrics_line_gap_percent: LYRICS_LINE_GAP_DEFAULT,
            lyrics_fixed_width_enabled: false,
            lyrics_fixed_width_dip: LYRICS_FIXED_WIDTH_DEFAULT,
            lyrics_long_line_scroll_enabled: true,
            lyrics_info_line_filter_enabled: true,
            lyrics_instrumental_title_only_enabled: false,
            lyrics_furigana_filter_enabled: false,
            lyrics_bracket_symbols_hidden: true,
            lyrics_chinese_conversion: LyricsChineseConversionMode::default(),
            lyrics_artist_separators: "/".to_owned(),
            launch_at_startup: true,
            developer_mode_enabled: false,
            interface_language: InterfaceLanguage::default(),
            translucent_tb_compatibility_prompt_shown: false,
            extra: Default::default(),
        }
    }
}

/// 根字段默认值（与 C# 字段初始化器一致），供 Normalize 回落使用。
#[derive(Debug, Clone, Copy)]
pub struct AppSettingsFieldDefaults;

impl AppSettingsFieldDefaults {
    pub const LAYOUT_LENGTH_SCALE_PERCENT: f64 = 100.0;
    pub const LAYOUT_THICKNESS_SCALE_PERCENT: f64 = 100.0;
    pub const TASKBAR_CROSS_AXIS_OFFSET_DIP: f64 = 0.0;
}

impl AppSettings {
    /// 根字段归一化（移植 C# `AppSettings.Normalize()`）。
    ///
    /// 枚举有效性由类型系统保证（宽松反序列化已在读取时回落默认）；
    /// 分组 Normalize 在各分组类型化的里程碑接管。
    pub fn normalize(&mut self) {
        // 旧灵动岛等未实现标识继续回退；悬浮模式不受限制。
        if self.window_mode != WindowMode::FloatingBar {
            self.window_mode = WindowMode::Taskbar;
        }

        // 旧单目标字段先按 C# setter 语义清洗（去空白或置空），再决定是否提升。
        self.taskbar_target_monitor_device_id = match self.taskbar_target_monitor_device_id.take() {
            Some(id) if !id.trim().is_empty() => Some(id.trim().to_owned()),
            _ => None,
        };
        // 显示器集合先清洗（C# 顺序：先 NormalizeMonitorDeviceIds 再提升），
        // 因此全空白列表被视为空、允许旧字段提升。去空白、去重（大小写不敏感）、保序。
        if let Some(ids) = self.taskbar_target_monitor_device_ids.take() {
            let mut normalized: Vec<String> = Vec::new();
            for id in ids {
                let trimmed = id.trim();
                if trimmed.is_empty() {
                    continue;
                }
                if !normalized
                    .iter()
                    .any(|existing| existing.eq_ignore_ascii_case(trimmed))
                {
                    normalized.push(trimmed.to_owned());
                }
            }
            self.taskbar_target_monitor_device_ids = Some(normalized);
        }
        // 旧单目标字段 → 显式集合提升（跳过"所有任务栏"旧标识）。
        let ids_empty = self
            .taskbar_target_monitor_device_ids
            .as_ref()
            .is_none_or(|ids| ids.is_empty());
        if ids_empty {
            let legacy = self
                .taskbar_target_monitor_device_id
                .take_if(|id| !is_legacy_all_taskbars(id));
            if let Some(id) = legacy {
                self.taskbar_target_monitor_device_ids = Some(vec![id]);
                self.taskbar_target_monitor_device_id = None;
            }
        }

        // 数值有限性 → 默认，再夹取（顺序与 C# 一致：先回退再夹取）。
        if !self.layout_length_scale_percent.is_finite() {
            self.layout_length_scale_percent =
                AppSettingsFieldDefaults::LAYOUT_LENGTH_SCALE_PERCENT;
        }
        if !self.layout_thickness_scale_percent.is_finite() {
            self.layout_thickness_scale_percent =
                AppSettingsFieldDefaults::LAYOUT_THICKNESS_SCALE_PERCENT;
        }
        if !self.taskbar_bar_cross_axis_offset_dip.is_finite() {
            self.taskbar_bar_cross_axis_offset_dip =
                AppSettingsFieldDefaults::TASKBAR_CROSS_AXIS_OFFSET_DIP;
        }
        self.layout_length_scale_percent = self.layout_length_scale_percent.clamp(70.0, 125.0);
        self.layout_thickness_scale_percent =
            self.layout_thickness_scale_percent.clamp(70.0, 125.0);
        self.taskbar_bar_cross_axis_offset_dip = self.taskbar_bar_cross_axis_offset_dip.clamp(
            MINIMUM_TASKBAR_CROSS_AXIS_OFFSET_DIP,
            MAXIMUM_TASKBAR_CROSS_AXIS_OFFSET_DIP,
        );

        // 灵动岛坐标：非有限或负值 → 回落 None。
        if let Some(left) = self.dynamic_island_left
            && (!left.is_finite() || left < 0.0)
        {
            self.dynamic_island_left = None;
        }
        if let Some(top) = self.dynamic_island_top
            && (!top.is_finite() || top < 0.0)
        {
            self.dynamic_island_top = None;
        }

        // 歌词数值夹取。
        self.lyrics_unsung_opacity_percent = clamp_i32(
            self.lyrics_unsung_opacity_percent,
            LYRICS_UNSUNG_OPACITY_MIN,
            LYRICS_UNSUNG_OPACITY_MAX,
        );
        self.lyrics_character_spacing_percent = clamp_i32(
            self.lyrics_character_spacing_percent,
            LYRICS_CHARACTER_SPACING_MIN,
            LYRICS_CHARACTER_SPACING_MAX,
        );
        self.lyrics_line_gap_percent = clamp_i32(
            self.lyrics_line_gap_percent,
            LYRICS_LINE_GAP_MIN,
            LYRICS_LINE_GAP_MAX,
        );
        self.lyrics_fixed_width_dip = clamp_i32(
            self.lyrics_fixed_width_dip,
            LYRICS_FIXED_WIDTH_MIN,
            LYRICS_FIXED_WIDTH_MAX,
        );

        if self.lyrics_artist_separators.is_empty() {
            // C# setter 保证非空：空串合法（关闭分割），此处仅维持原状。
        }
    }
}

/// 判断旧设置值是否表示"所有任务栏"（大小写不敏感）。
pub fn is_legacy_all_taskbars(device_id: &str) -> bool {
    device_id.eq_ignore_ascii_case(LEGACY_ALL_TASKBARS_DEVICE_ID)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn default_matches_csharp_field_initializers() {
        let settings = AppSettings::new();
        assert!(settings.lyrics_enabled);
        assert!(!settings.allow_browser_and_video_lyrics);
        assert!(settings.two_line_lyrics_enabled);
        assert!(settings.taskbar_bar_enabled);
        assert_eq!(
            settings.tray_wheel_behavior,
            TrayWheelBehavior::SwitchOutputDevice
        );
        assert_eq!(settings.position, TaskbarBarPosition::Start);
        assert_eq!(settings.window_mode, WindowMode::Taskbar);
        assert_eq!(settings.interface_language, InterfaceLanguage::System);
        assert!(settings.launch_at_startup);
        assert!(!settings.developer_mode_enabled);
        assert_eq!(settings.layout_length_scale_percent, 100.0);
        assert_eq!(settings.lyrics_unsung_opacity_percent, 45);
        assert_eq!(settings.lyrics_fixed_width_dip, 240);
        assert_eq!(settings.lyrics_artist_separators, "/");
        assert!(settings.taskbar_bar_avoid_icons);
    }

    #[test]
    fn normalize_promotes_legacy_single_monitor_target() {
        let mut settings = AppSettings::new();
        settings.taskbar_target_monitor_device_id = Some("  DISPLAY1  ".to_owned());
        settings.normalize();
        assert_eq!(
            settings.taskbar_target_monitor_device_ids,
            Some(vec!["DISPLAY1".to_owned()])
        );
        assert!(settings.taskbar_target_monitor_device_id.is_none());
    }

    #[test]
    fn normalize_treats_whitespace_only_list_as_empty_for_promotion() {
        // C# 顺序：先 NormalizeMonitorDeviceIds（全空白 → 空列表）再做旧字段提升。
        let mut settings = AppSettings::new();
        settings.taskbar_target_monitor_device_ids = Some(vec!["   ".to_owned()]);
        settings.taskbar_target_monitor_device_id = Some("DISPLAY2".to_owned());
        settings.normalize();
        assert_eq!(
            settings.taskbar_target_monitor_device_ids,
            Some(vec!["DISPLAY2".to_owned()]),
            "全空白列表必须视为空并允许提升"
        );
    }

    #[test]
    fn normalize_dedups_monitor_ids_case_insensitively_preserving_order() {
        let mut settings = AppSettings::new();
        settings.taskbar_target_monitor_device_ids = Some(vec![
            "DISPLAY\\A".to_owned(),
            " display\\a ".to_owned(),
            "DISPLAY\\B".to_owned(),
            " ".to_owned(),
        ]);
        settings.normalize();
        assert_eq!(
            settings.taskbar_target_monitor_device_ids,
            Some(vec!["DISPLAY\\A".to_owned(), "DISPLAY\\B".to_owned()])
        );
    }

    #[test]
    fn normalize_keeps_legacy_all_taskbars_marker() {
        let mut settings = AppSettings::new();
        settings.taskbar_target_monitor_device_id = Some("{afmediabar.alltaskbars}".to_owned());
        settings.normalize();
        assert!(settings.taskbar_target_monitor_device_ids.is_none());
        // take_if 分支未命中时字段保留原值。
        assert_eq!(
            settings.taskbar_target_monitor_device_id.as_deref(),
            Some("{afmediabar.alltaskbars}")
        );
    }

    #[test]
    fn normalize_clamps_scales_and_offsets() {
        let mut settings = AppSettings::new();
        settings.layout_length_scale_percent = 300.0;
        settings.layout_thickness_scale_percent = 10.0;
        settings.taskbar_bar_cross_axis_offset_dip = 99.0;
        settings.lyrics_unsung_opacity_percent = 5;
        settings.lyrics_fixed_width_dip = 9999;
        settings.normalize();
        assert_eq!(settings.layout_length_scale_percent, 125.0);
        assert_eq!(settings.layout_thickness_scale_percent, 70.0);
        assert_eq!(settings.taskbar_bar_cross_axis_offset_dip, 20.0);
        assert_eq!(settings.lyrics_unsung_opacity_percent, 20);
        assert_eq!(settings.lyrics_fixed_width_dip, 600);
    }

    #[test]
    fn normalize_reverts_non_finite_and_negative_island_positions() {
        let mut settings = AppSettings::new();
        settings.dynamic_island_left = Some(-1.0);
        settings.dynamic_island_top = Some(f64::NAN);
        settings.layout_length_scale_percent = f64::INFINITY;
        settings.normalize();
        assert!(settings.dynamic_island_left.is_none());
        assert!(settings.dynamic_island_top.is_none());
        assert_eq!(settings.layout_length_scale_percent, 100.0);
    }

    #[test]
    fn window_mode_outside_floating_bar_falls_back_to_taskbar() {
        let mut settings = AppSettings::new();
        settings.window_mode = WindowMode::DynamicIsland;
        settings.normalize();
        assert_eq!(settings.window_mode, WindowMode::Taskbar);
        settings.window_mode = WindowMode::FloatingBar;
        settings.normalize();
        assert_eq!(settings.window_mode, WindowMode::FloatingBar);
    }

    #[test]
    fn serde_uses_camel_case_names() {
        let settings = AppSettings::new();
        let value = serde_json::to_value(&settings).unwrap();
        assert!(value.get("trayWheelBehavior").is_some());
        assert!(value.get("lyricsUnsungOpacityPercent").is_some());
        assert!(value.get("translucentTbCompatibilityPromptShown").is_some());
    }
}
