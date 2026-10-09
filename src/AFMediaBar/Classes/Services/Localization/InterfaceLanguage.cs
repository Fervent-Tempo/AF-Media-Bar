namespace AFMediaBar.Classes.Services.Localization;

/// <summary>
/// 界面语言设置。它保存的是用户的**选择**，而不是当前生效的语言：<see cref="System"/> 表示跟随系统，
/// 具体语言由 <see cref="InterfaceLanguagePolicy"/> 按系统 UI 区域性解析。
///
/// 成员值参与设置文件序列化，因此只能追加，不得重排或删除。
/// The interface-language setting. It stores the user's choice rather than the language in effect:
/// <see cref="System"/> means "follow the system", and <see cref="InterfaceLanguagePolicy"/> resolves it against the
/// system UI culture.
///
/// The member values are serialized into the settings file, so they may only be appended, never reordered or removed.
/// </summary>
public enum InterfaceLanguage
{
    /// <summary>跟随系统 UI 语言。/ Follow the system UI language.</summary>
    System = 0,

    /// <summary>简体中文。/ Simplified Chinese.</summary>
    SimplifiedChinese = 1,

    /// <summary>繁体中文。/ Traditional Chinese.</summary>
    TraditionalChinese = 2,

    /// <summary>英文。/ English.</summary>
    English = 3,

    /// <summary>越南语。/ Vietnamese.</summary>
    Vietnamese = 4,
}

/// <summary>
/// 解析之后实际生效的界面语言。它没有"跟随系统"这一项：设置可以被跟随系统解析，而一个已经生效的语言必然是四者之一。
/// The interface language actually in effect. It has no "follow the system" member: a setting can follow the system, but a
/// language in effect is always one of the four concrete ones.
/// </summary>
public enum LocalizationLanguage
{
    /// <summary>简体中文。/ Simplified Chinese.</summary>
    SimplifiedChinese = 0,

    /// <summary>繁体中文。/ Traditional Chinese.</summary>
    TraditionalChinese = 1,

    /// <summary>英文。/ English.</summary>
    English = 2,

    /// <summary>越南语。/ Vietnamese.</summary>
    Vietnamese = 3,
}
