// 设置页共用的强调色色板；仅表达颜色选择，不读取或保存配置。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AFMediaBar.Components;

/// <summary>为强调色及通知文字提供相同的预设色块。</summary>
public partial class SettingsColorPalette : UserControl
{
    public static readonly DependencyProperty SelectionCommandProperty = DependencyProperty.Register(nameof(SelectionCommand), typeof(ICommand),
        typeof(SettingsColorPalette), new PropertyMetadata(null));
    public ICommand? SelectionCommand { get => (ICommand?)GetValue(SelectionCommandProperty); set => SetValue(SelectionCommandProperty, value); }
    public IReadOnlyList<string> Colors { get; } = Array.AsReadOnly(new[]
    {
        "#0078D4", "#0099BC", "#107C10", "#876400", "#C42B1C", "#E3008C", "#881798", "#7C3AED", "#4A545E", "#767676"
    });

    public SettingsColorPalette() => InitializeComponent();
}
