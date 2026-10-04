using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace AFMediaBar.Components;

/// <summary>
/// 设置页分组的色调。同一个页面里的相邻分组用不同色调，让分组边界在滚动时也能被一眼区分，
/// 而不必依赖用户去读分组标题。
/// Tint of a settings group. Neighbouring groups on one page alternate tints so the boundary between
/// them is visible while scrolling, without the user having to read the group header.
/// </summary>
public enum SettingsGroupTint
{
    /// <summary>主色：承载当前位置或当前生效配置的分组。/ Accent: the group that owns the active location or configuration.</summary>
    Accent,

    /// <summary>中性：常规设置分组，也是默认值。/ Neutral: an ordinary settings group; this is the default.</summary>
    Neutral,

    /// <summary>弱化：未实现或按版本预留的分组，视觉上明确退到后面。/ Muted: groups that are not implemented or reserved for a later version.</summary>
    Muted,
}

/// <summary>
/// 设置页分组容器：一个带图标、标题和可选状态芯片的分组，内部承载该分组的卡片。
///
/// 它同时是分组导航的唯一事实来源：<see cref="SettingsGroupStrip"/> 按声明顺序读取页面滚动内容里的
/// 分组，因此分组标签不会与页面内容各自维护一份名称而出现错位。
/// A settings group container: an icon, a header, an optional status chip, and the cards that belong to
/// the group.
///
/// It is also the single source of truth for group navigation: <see cref="SettingsGroupStrip"/> reads the
/// groups out of the page's scroll content in declaration order, so the tab labels cannot drift away from
/// the page content.
/// </summary>
public class SettingsGroup : HeaderedContentControl
{
    /// <summary>分组标题左侧的图标。/ Icon shown to the left of the group header.</summary>
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon),
        typeof(IconElement),
        typeof(SettingsGroup),
        new PropertyMetadata(null));

    /// <summary>分组色调，决定图标底片与标题竖条的颜色强度。/ Group tint, which sets the strength of the icon chip and header rail.</summary>
    public static readonly DependencyProperty TintProperty = DependencyProperty.Register(
        nameof(Tint),
        typeof(SettingsGroupTint),
        typeof(SettingsGroup),
        new PropertyMetadata(SettingsGroupTint.Neutral));

    /// <summary>可选状态芯片文本；为空时不显示芯片。/ Optional status chip text; an empty value hides the chip.</summary>
    public static readonly DependencyProperty StatusTextProperty = DependencyProperty.Register(
        nameof(StatusText),
        typeof(string),
        typeof(SettingsGroup),
        new PropertyMetadata(string.Empty));

    /// <summary>状态芯片语气。/ Tone of the status chip.</summary>
    public static readonly DependencyProperty StatusToneProperty = DependencyProperty.Register(
        nameof(StatusTone),
        typeof(SettingsChipTone),
        typeof(SettingsGroup),
        new PropertyMetadata(SettingsChipTone.Neutral));

    /// <summary>
    /// 分组说明，一行白话，标题正下方。用于解释「常驻状态 / 快捷控制 / 完整面板」等界面名称何时生效。
    /// Group description: one plain line directly under the header explaining when a view such as
    /// "Always-on view / Quick controls / Full panel" is shown.
    /// </summary>
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description),
        typeof(string),
        typeof(SettingsGroup),
        new PropertyMetadata(string.Empty));

    /// <summary>分组标题左侧的图标。/ Icon shown to the left of the group header.</summary>
    public IconElement? Icon
    {
        get => (IconElement?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>分组色调。/ Group tint.</summary>
    public SettingsGroupTint Tint
    {
        get => (SettingsGroupTint)GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    /// <summary>可选状态芯片文本。/ Optional status chip text.</summary>
    public string StatusText
    {
        get => (string)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    /// <summary>分组说明。/ Group description.</summary>
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>状态芯片语气。/ Tone of the status chip.</summary>
    public SettingsChipTone StatusTone
    {
        get => (SettingsChipTone)GetValue(StatusToneProperty);
        set => SetValue(StatusToneProperty, value);
    }
}
