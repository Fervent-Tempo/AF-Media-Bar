// 复用五种样式选项和静态预览，不创建采样租约或计时器。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Data;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Components;

/// <summary>性能样式编辑器；绑定值由页面 ViewModel 保存。</summary>
public sealed class MetricStylePicker : Grid
{
    public static readonly DependencyProperty VisualStyleProperty = DependencyProperty.Register(
        nameof(VisualStyle), typeof(MetricVisualStyle), typeof(MetricStylePicker),
        new FrameworkPropertyMetadata(MetricVisualStyle.Numeric, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnStyleChanged));
    private readonly ComboBox _picker = new() { MinWidth = 140, SelectedValuePath = "Tag" };
    private readonly MetricChart _preview = new() { Width = 52, Height = 20, Margin = new Thickness(8, 0, 0, 0) };
    public MetricVisualStyle VisualStyle { get => (MetricVisualStyle)GetValue(VisualStyleProperty); set => SetValue(VisualStyleProperty, value); }

    public MetricStylePicker()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foreach (var style in Enum.GetValues<MetricVisualStyle>())
        {
            var item = new ComboBoxItem { Tag = style };
            item.SetResourceReference(ContentControl.ContentProperty, "Loc.Performance.Visual." + style);
            _picker.Items.Add(item);
        }
        var accessibleName = new MultiBinding { StringFormat = "{0} · {1}" };
        accessibleName.Bindings.Add(new Binding("Header") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Wpf.Ui.Controls.CardExpander), 1) });
        accessibleName.Bindings.Add(new Binding("Title") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(SettingsRow), 1) });
        _picker.SetBinding(AutomationProperties.NameProperty, accessibleName);
        SetColumn(_preview, 1);
        Children.Add(_picker);
        Children.Add(_preview);
        _picker.SelectedValue = VisualStyle;
        _picker.SelectionChanged += (_, _) =>
        {
            if (_picker.SelectedValue is MetricVisualStyle style && style != VisualStyle)
                SetCurrentValue(VisualStyleProperty, style);
        };
        Loaded += (_, _) => RefreshPreview();
        RefreshPreview();
    }

    private static void OnStyleChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var control = (MetricStylePicker)sender;
        control._picker.SelectedValue = (MetricVisualStyle)e.NewValue;
        control.RefreshPreview();
    }

    private void RefreshPreview()
    {
        _preview.VisualStyle = VisualStyle;
        _preview.ShowPreview();
    }
}
