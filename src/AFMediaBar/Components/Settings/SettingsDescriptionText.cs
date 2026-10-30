using System.Windows;
using System.Windows.Automation;
using System.Windows.Documents;
using System.Windows.Input;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace AFMediaBar.Components;

/// <summary>
/// 行说明文本：正文末尾可以紧跟一个圆形提示图标，悬停显示 <see cref="Hint"/>。
/// A row description whose text can be followed by a round hint icon that reveals <see cref="Hint"/> on hover.
///
/// 图标用 <see cref="InlineUIContainer"/> 嵌在文字流里，因此它落在最后一个字符后面，
/// 描述换行时也跟着文字走，而不是被推到行末或另起一行。
/// The icon lives inside the text flow through an <see cref="InlineUIContainer"/>, so it sits right after the last
/// character and follows the text when the description wraps, instead of being pushed to the row's edge.
/// </summary>
public class SettingsDescriptionText : TextBlock
{
    /// <summary>说明正文。/ The description text.</summary>
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description),
        typeof(string),
        typeof(SettingsDescriptionText),
        new PropertyMetadata(string.Empty, OnContentChanged));

    /// <summary>圆形图标的悬停提示；为空时不显示图标。/ Hover text behind the round icon; an empty value hides it.</summary>
    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(
        nameof(Hint),
        typeof(string),
        typeof(SettingsDescriptionText),
        new PropertyMetadata(string.Empty, OnContentChanged));

    /// <summary>说明正文。/ The description text.</summary>
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>圆形图标的悬停提示。/ Hover text behind the round icon.</summary>
    public string Hint
    {
        get => (string)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    private static void OnContentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((SettingsDescriptionText)sender).Rebuild();

    private void Rebuild()
    {
        Inlines.Clear();
        if (string.IsNullOrEmpty(Description)) return;

        Inlines.Add(new Run(Description));
        if (string.IsNullOrWhiteSpace(Hint)) return;

        var icon = new SymbolIcon
        {
            Symbol = SymbolRegular.Info24,
            FontSize = 18,
            Margin = new Thickness(4, 0, 0, 0),
            Cursor = Cursors.Help,
            ToolTip = Hint
        };
        icon.SetResourceReference(IconElement.ForegroundProperty, "TextFillColorSecondaryBrush");
        AutomationProperties.SetHelpText(icon, Hint);
        Inlines.Add(new InlineUIContainer(icon) { BaselineAlignment = BaselineAlignment.Center });
    }
}
