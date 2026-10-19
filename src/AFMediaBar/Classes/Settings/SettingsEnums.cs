// 定义共用设置枚举；名称和值参与现有序列化契约。
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AFMediaBar.Classes.Settings;


/// <summary>任务栏位置。 / Taskbar placement.</summary>
public enum TaskbarBarPosition { Start = 0, Center = 1, End = 2 }
/// <summary>布局方向模式。 / Layout orientation mode.</summary>
public enum LayoutOrientationMode { Auto = 0, Horizontal = 1, Vertical = 2 }
/// <summary>灵动岛背景模式。 / Dynamic-island background mode.</summary>
public enum DynamicIslandBackgroundMode { SystemTheme = 0, Transparent = 1 }
/// <summary>托盘滚轮行为。 / Tray-wheel behavior.</summary>
public enum TrayWheelBehavior { AdjustVolume = 0, SwitchOutputDevice = 1, Disabled = 2, SwitchInputDevice = 3 }
/// <summary>双行歌词第二行模式。 / Secondary lyric-line mode.</summary>
public enum LyricsSecondaryLineMode
{
    /// <summary>显示下一句歌词。/ Show the next lyric line.</summary>
    NextLine = 0,

    /// <summary>显示当前句翻译。/ Show the translation of the active line.</summary>
    Translation = 1,

    /// <summary>显示当前句音译（如粤拼、罗马字）。成员值参与序列化，因此只能追加。
    /// Show the romanization of the active line (Cantonese jyutping, romanized Japanese, and so on). Member values take part in
    /// serialization, so this may only be appended.</summary>
    Romanization = 2
}

/// <summary>
/// 歌词文本的简繁转换方向：只在显示前改写呈现文本，不动取词结果本身。
/// Conversion direction of lyric text: it only rewrites the presented text, never the retrieved lyric document itself.
///
/// 成员值参与序列化，因此只能追加、不能改值或重排。默认 <c>None</c>，即升级前的观感完全不变。
/// Member values take part in serialization, so members may only be appended, never renumbered or reordered. The default is
/// <c>None</c>, which leaves the look exactly as it was before this setting existed.
/// </summary>
public enum LyricsChineseConversionMode
{
    /// <summary>不做转换，歌词按来源原样显示。/ No conversion; lyrics show exactly as their source wrote them.</summary>
    None = 0,

    /// <summary>简体转繁体。/ Convert Simplified Chinese to Traditional Chinese.</summary>
    SimplifiedToTraditional = 1,

    /// <summary>繁体转简体。/ Convert Traditional Chinese to Simplified Chinese.</summary>
    TraditionalToSimplified = 2
}
