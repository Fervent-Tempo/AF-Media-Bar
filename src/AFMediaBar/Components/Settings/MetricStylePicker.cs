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
    public static readonly DependencyProperty MetricProperty = DependencyProperty.Register(
        nameof(Metric), typeof(AFMediaBar.Classes.Models.MetricKind), typeof(MetricStylePicker),
        new PropertyMetadata(AFMediaBar.Classes.Models.MetricKind.SystemCpu, (sender, _) => ((MetricStylePicker)sender).RefreshPreview()));
    public AFMediaBar.Classes.Models.MetricKind Metric { get => (AFMediaBar.Classes.Models.MetricKind)GetValue(MetricProperty); set => SetValue(MetricProperty, value); }
    private readonly ComboBox _picker = new() { Width = 140, SelectedValuePath = "Tag" };
    private readonly MetricPresenter _preview = new() { Metric = AFMediaBar.Classes.Models.MetricKind.SystemCpu, ShowHover = false, Width = 96, Height = 28, Margin = new Thickness(0, 0, 8, 0) };
    public MetricVisualStyle VisualStyle { get => (MetricVisualStyle)GetValue(VisualStyleProperty); set => SetValue(VisualStyleProperty, value); }

    public MetricStylePicker()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
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
        SetColumn(_picker, 1);
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
        _preview.Metric = Metric;
        _preview.VisualStyle = VisualStyle;
        _preview.Visibility = Visibility.Visible;
        _preview.ShowPreview();
    }
}
