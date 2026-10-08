using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace AFMediaBar.Components;

/// <summary>
/// 设置页的一行：左侧图标，中间标题与说明，右侧恰好一个控件。
///
/// 这条“一行一个控件”的规则是设置页可读性的来源：控件永远落在同一条竖线上，
/// 眼睛只需要沿一列往下扫，不需要在每一张卡片里重新找控件在哪。
/// 需要多个控件才能表达的设置不应该塞进一行，而应拆成多行，或用 <c>ui:CardExpander</c> 折叠。
/// One settings row: icon on the left, title and description in the middle, exactly one control on the right.
///
/// That "one control per row" rule is what makes a settings page scannable: controls always land on the same
/// vertical line, so the eye travels down a single column instead of hunting for the control inside every
/// card. A setting that needs several controls does not belong crammed into one row; it belongs in several
/// rows, or behind a <c>ui:CardExpander</c>.
/// </summary>
public class SettingsRow : HeaderedContentControl
{
    private static readonly DependencyPropertyKey IsCompactPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsCompact), typeof(bool), typeof(SettingsRow), new PropertyMetadata(false));
    /// <summary>Moves controls below the title when the sidebar leaves too little width for two columns.</summary>
    public static readonly DependencyProperty IsCompactProperty = IsCompactPropertyKey.DependencyProperty;
    /// <summary>Whether this row uses the narrow-content layout.</summary>
    public bool IsCompact => (bool)GetValue(IsCompactProperty);

    /// <inheritdoc />
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        SetValue(IsCompactPropertyKey, sizeInfo.NewSize.Width < 520d);
    }
    /// <summary>行标题左侧的图标。/ Icon to the left of the row title.</summary>
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon),
        typeof(IconElement),
        typeof(SettingsRow),
        new PropertyMetadata(null));

    /// <summary>行表面的圆角。嵌套行把它设为 0，因为它只保留一条发丝线。/ Corner radius of the row surface. A nested row sets it to zero because it keeps only a hairline.</summary>
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius),
        typeof(CornerRadius),
        typeof(SettingsRow),
        new PropertyMetadata(new CornerRadius(8d)));

    /// <summary>行标题。/ Row title.</summary>
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(SettingsRow),
        new PropertyMetadata(string.Empty));

    /// <summary>行说明，一句话，标题正下方；为空时不占高度。/ Row description, one line under the title; an empty value takes no space.</summary>
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description),
        typeof(string),
        typeof(SettingsRow),
        new PropertyMetadata(string.Empty));

    /// <summary>标题旁的内联状态徽章文本；为空时不显示。/ Inline status badge text next to the title; an empty value hides the badge.</summary>
    public static readonly DependencyProperty BadgeProperty = DependencyProperty.Register(
        nameof(Badge),
        typeof(string),
        typeof(SettingsRow),
        new PropertyMetadata(string.Empty));

    /// <summary>内联徽章的语气。/ Tone of the inline badge.</summary>
    public static readonly DependencyProperty BadgeToneProperty = DependencyProperty.Register(
        nameof(BadgeTone),
        typeof(SettingsChipTone),
        typeof(SettingsRow),
        new PropertyMetadata(SettingsChipTone.Neutral));

    /// <summary>
    /// 该行是否嵌在 <c>ui:CardExpander</c> 内部。嵌套行用更小的图标、左侧缩进与发丝线分隔，
    /// 因此它读起来属于上方那一行，而不是又一张卡片。
    /// Whether this row is nested inside a <c>ui:CardExpander</c>. A nested row uses a smaller icon, a left
    /// indent, and a hairline separator, so it reads as belonging to the row above rather than as another card.
    /// </summary>
    public static readonly DependencyProperty IsNestedProperty = DependencyProperty.Register(
        nameof(IsNested),
        typeof(bool),
        typeof(SettingsRow),
        new PropertyMetadata(false));

    /// <summary>行标题左侧的图标。/ Icon to the left of the row title.</summary>
    public IconElement? Icon
    {
        get => (IconElement?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>行表面的圆角。/ Corner radius of the row surface.</summary>
    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>行标题。/ Row title.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>行说明。/ Row description.</summary>
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>内联状态徽章文本。/ Inline status badge text.</summary>
    public string Badge
    {
        get => (string)GetValue(BadgeProperty);
        set => SetValue(BadgeProperty, value);
    }

    /// <summary>内联徽章语气。/ Tone of the inline badge.</summary>
    public SettingsChipTone BadgeTone
    {
        get => (SettingsChipTone)GetValue(BadgeToneProperty);
        set => SetValue(BadgeToneProperty, value);
    }

    /// <summary>是否嵌在展开器内部。/ Whether the row is nested inside an expander.</summary>
    public bool IsNested
    {
        get => (bool)GetValue(IsNestedProperty);
        set => SetValue(IsNestedProperty, value);
    }
}
