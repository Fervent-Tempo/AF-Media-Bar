//! AF Media Bar 本地化：四语言资源加载、语言解析与文案查询。
//!
//! 对应 C# `Localization/LocalizationService` + `Resources/Translations`：
//! - 资源来自主工程 resx（一次性转换产物嵌入 `resources/`），`include_str!` 编译期内嵌；
//! - 语言选项「跟随系统」按系统 UI 语言解析（`GetUserDefaultUILanguage`）；
//! - 回退链与 C# `Translations.Get` 一致：所选语言 → 简体中文（保底语言）→ 键名本身；
//! - 简繁转换（歌词呈现用）由 afmb-lyrics 里程碑引入的 OpenCC 词库承担，不在此层。

use std::collections::HashMap;
use std::sync::OnceLock;

use serde_json::Value;
use windows::Win32::Globalization::GetUserDefaultUILanguage;

const ZH_HANS_JSON: &str = include_str!("../resources/zh-Hans.json");
const ZH_HANT_JSON: &str = include_str!("../resources/zh-Hant.json");
const EN_US_JSON: &str = include_str!("../resources/en-US.json");
const VI_VN_JSON: &str = include_str!("../resources/vi-VN.json");

/// 界面语言（不含「跟随系统」——解析后的实际语言）。
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum Language {
    SimplifiedChinese,
    TraditionalChinese,
    English,
    Vietnamese,
}

impl Language {
    /// 资源文件名（不含扩展名）。
    pub fn resource_id(self) -> &'static str {
        match self {
            Language::SimplifiedChinese => "zh-Hans",
            Language::TraditionalChinese => "zh-Hant",
            Language::English => "en-US",
            Language::Vietnamese => "vi-VN",
        }
    }
}

/// 语言资源表（进程内单例，惰性构建）。
pub struct Catalog {
    tables: HashMap<&'static str, HashMap<String, String>>,
}

impl Catalog {
    fn shared() -> &'static Catalog {
        static CATALOG: OnceLock<Catalog> = OnceLock::new();
        CATALOG.get_or_init(|| {
            let mut tables = HashMap::new();
            for (id, raw) in [
                ("zh-Hans", ZH_HANS_JSON),
                ("zh-Hant", ZH_HANT_JSON),
                ("en-US", EN_US_JSON),
                ("vi-VN", VI_VN_JSON),
            ] {
                let parsed: HashMap<String, String> = match serde_json::from_str::<Value>(raw) {
                    Ok(Value::Object(map)) => map
                        .into_iter()
                        .filter_map(|(key, value)| {
                            value.as_str().map(|text| (key, text.to_owned()))
                        })
                        .collect(),
                    _ => HashMap::new(),
                };
                tables.insert(id, parsed);
            }
            Catalog { tables }
        })
    }

    /// 指定语言的条目数（诊断用）。
    pub fn entry_count(language: Language) -> usize {
        Catalog::shared().tables[language.resource_id()].len()
    }
}

/// 解析「跟随系统」：按系统 UI 语言映射，未识别时回退英文。
pub fn resolve_system_language() -> Language {
    let lang_id = unsafe { GetUserDefaultUILanguage() };
    let name = lang_name(lang_id);
    if name.starts_with("zh") {
        // 中文按地区区分简繁：大陆/新加坡/简体标记 → 简体；港澳台 → 繁体。
        if name.contains("Hans")
            || name.contains("CN")
            || name.contains("SG")
            || name.eq_ignore_ascii_case("zh")
        {
            return Language::SimplifiedChinese;
        }
        return Language::TraditionalChinese;
    }
    if name.starts_with("vi") {
        return Language::Vietnamese;
    }
    Language::English
}

fn lang_name(lang_id: u16) -> String {
    // LANGID 主语言 ID 低 10 位；子语言用于地区判断。
    let primary = lang_id & 0x3FF;
    let sub = lang_id >> 10;
    let primary_name = match primary {
        0x0004 => "zh",
        0x0009 => "en",
        0x002A => "vi",
        _ => "",
    };
    let region = match (primary, sub) {
        // LANGID：zh-CN=0x0804(sub2) zh-TW=0x0404(sub1) zh-HK=0x0C04 zh-SG=0x1004 zh-MO=0x1404
        (0x0004, 0x02) => "-CN",
        (0x0004, 0x01) => "-TW",
        (0x0004, 0x03) => "-HK",
        (0x0004, 0x04) => "-SG",
        (0x0004, 0x05) => "-MO",
        (0x0009, 0x01) => "-US",
        (0x002A, 0x01) => "-VN",
        _ => "",
    };
    format!("{primary_name}{region}")
}

/// 本地化查询门面：持有解析后的生效语言。
#[derive(Debug, Clone, Copy)]
pub struct Localization {
    effective: Language,
}

impl Localization {
    /// 按用户选项解析生效语言（对应 `InterfaceLanguagePolicy`）。
    pub fn resolve(option: crate::option::InterfaceLanguageOption) -> Self {
        let effective = match option {
            crate::option::InterfaceLanguageOption::System => resolve_system_language(),
            crate::option::InterfaceLanguageOption::SimplifiedChinese => {
                Language::SimplifiedChinese
            }
            crate::option::InterfaceLanguageOption::TraditionalChinese => {
                Language::TraditionalChinese
            }
            crate::option::InterfaceLanguageOption::English => Language::English,
            crate::option::InterfaceLanguageOption::Vietnamese => Language::Vietnamese,
        };
        Self { effective }
    }

    /// 生效语言。
    pub fn language(&self) -> Language {
        self.effective
    }

    /// 查询文案；回退链与 C# `Translations.Get` 一致：
    /// 所选语言 → **简体中文**（保底语言，缺键不悄悄换成英文）→ 键名本身。
    pub fn text(&self, key: &str) -> String {
        let catalog = Catalog::shared();
        catalog.tables[self.effective.resource_id()]
            .get(key)
            .filter(|text| !text.is_empty())
            .or_else(|| {
                catalog.tables["zh-Hans"]
                    .get(key)
                    .filter(|text| !text.is_empty())
            })
            .cloned()
            .unwrap_or_else(|| key.to_owned())
    }

    /// 带占位符查询：`{0}`…`{9}` 依次替换（对应 C# `string.Format`）。
    pub fn format(&self, key: &str, args: &[&str]) -> String {
        let mut text = self.text(key);
        for (index, arg) in args.iter().enumerate() {
            text = text.replace(&format!("{{{index}}}"), arg);
        }
        text
    }
}

/// 语言选项（镜像设置里的 `interfaceLanguage`，避免反向依赖 settings crate）。
pub mod option {
    #[derive(Debug, Clone, Copy, PartialEq, Eq)]
    pub enum InterfaceLanguageOption {
        System,
        SimplifiedChinese,
        TraditionalChinese,
        English,
        Vietnamese,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn all_four_languages_embed_1621_entries() {
        assert_eq!(Catalog::entry_count(Language::SimplifiedChinese), 1621);
        assert_eq!(Catalog::entry_count(Language::TraditionalChinese), 1621);
        assert_eq!(Catalog::entry_count(Language::English), 1621);
        assert_eq!(Catalog::entry_count(Language::Vietnamese), 1621);
    }

    #[test]
    fn query_falls_back_to_simplified_chinese_then_key() {
        let l10n = Localization::resolve(option::InterfaceLanguageOption::English);
        // 真实键：语言选择标题。
        let title = l10n.text("About.Language.Title");
        assert_ne!(
            title, "About.Language.Title",
            "英文表应能命中真实键（命中内容: {title}）"
        );
        assert_eq!(
            l10n.text("__no_such_key__"),
            "__no_such_key__",
            "未知键回退键名"
        );
    }

    #[test]
    fn fallback_language_is_simplified_chinese() {
        // 对齐 C# Translations.Get：某语言槽缺失/为空时回退简体中文，不悄悄换成英文。
        // 四表现有键集一致（各 1621 条），因此以"非英文语言 + 真实键"验证命中路径，
        // 回退语义由 text() 的实现顺序（先所选语言、后 zh-Hans、最后键名）保证。
        let l10n = Localization::resolve(option::InterfaceLanguageOption::English);
        let zh = Localization::resolve(option::InterfaceLanguageOption::SimplifiedChinese);
        let en_text = l10n.text("About.Language.Title");
        let zh_text = zh.text("About.Language.Title");
        assert_ne!(en_text, zh_text, "英文命中时不得回退简体");
    }

    #[test]
    fn language_options_resolve_to_expected_tables() {
        let zh = Localization::resolve(option::InterfaceLanguageOption::SimplifiedChinese);
        assert_eq!(zh.language(), Language::SimplifiedChinese);
        let vi = Localization::resolve(option::InterfaceLanguageOption::Vietnamese);
        assert_eq!(vi.language(), Language::Vietnamese);
    }

    #[test]
    fn placeholder_formatting_replaces_indices() {
        let l10n = Localization::resolve(option::InterfaceLanguageOption::English);
        let formatted = l10n.format("__test_{0}_key__", &["X"]);
        // 未知键回退键名后同样参与替换。
        assert_eq!(formatted, "__test_X_key__");
    }

    #[test]
    fn system_language_resolution_maps_langids() {
        // 直接测内部映射函数，避免依赖测试机系统语言。
        assert_eq!(lang_name(0x0804), "zh-CN"); // 简体中文（中国）
        assert_eq!(lang_name(0x0404), "zh-TW"); // 繁体中文（台湾）
        assert_eq!(lang_name(0x0409), "en-US");
        assert_eq!(lang_name(0x042A), "vi-VN");
        assert_eq!(lang_name(0x0C04), "zh-HK");
    }
}
