// 设置页的自适应版心与预览布局；只管理呈现，不持有设置或外部资源。
using System.Windows;
using System.Windows.Controls;

namespace AFMediaBar.Components;

/// <summary>为固定页头、设置滚动区和局部预览提供统一布局。</summary>
[TemplatePart(Name = "PART_ScrollViewer", Type = typeof(ScrollViewer))]
public sealed class SettingsPageLayout : HeaderedContentControl
{
    public static readonly DependencyProperty ContentMaximumWidthProperty = DependencyProperty.Register(
        nameof(ContentMaximumWidth), typeof(double), typeof(SettingsPageLayout), new PropertyMetadata(1120d));
    public double ContentMaximumWidth { get => (double)GetValue(ContentMaximumWidthProperty); set => SetValue(ContentMaximumWidthProperty, value); }
    private static readonly DependencyPropertyKey ScrollViewerPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ScrollViewer), typeof(ScrollViewer), typeof(SettingsPageLayout), new PropertyMetadata(null));
    public static readonly DependencyProperty ScrollViewerProperty = ScrollViewerPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey IsWidePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsWide), typeof(bool), typeof(SettingsPageLayout), new PropertyMetadata(false));
    public static readonly DependencyProperty IsWideProperty = IsWidePropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey PreviewColumnPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(PreviewColumn), typeof(GridLength), typeof(SettingsPageLayout), new PropertyMetadata(new GridLength(360)));
    public static readonly DependencyProperty PreviewColumnProperty = PreviewColumnPropertyKey.DependencyProperty;
    public static readonly DependencyProperty PreviewProperty = DependencyProperty.Register(
        nameof(Preview), typeof(object), typeof(SettingsPageLayout), new PropertyMetadata(null, (sender, _) => ((SettingsPageLayout)sender).UpdateLayoutMode()));
    public static readonly DependencyProperty PreviewExpandedProperty = DependencyProperty.Register(
        nameof(PreviewExpanded), typeof(bool), typeof(SettingsPageLayout), new PropertyMetadata(false, OnExpandedChanged));
    public static readonly DependencyProperty PreviewViewportHeightProperty = DependencyProperty.Register(
        nameof(PreviewViewportHeight), typeof(double), typeof(SettingsPageLayout), new PropertyMetadata(360d));
    public double PreviewViewportHeight => (double)GetValue(PreviewViewportHeightProperty);
    private FrameworkElement? _body;
    private bool _wideExpanded = true;
    private bool _compactExpanded;
    private bool _changingLayout;

    static SettingsPageLayout() => DefaultStyleKeyProperty.OverrideMetadata(typeof(SettingsPageLayout), new FrameworkPropertyMetadata(typeof(SettingsPageLayout)));

    public ScrollViewer? ScrollViewer => (ScrollViewer?)GetValue(ScrollViewerProperty);
    public bool IsWide => (bool)GetValue(IsWideProperty);
    public GridLength PreviewColumn => (GridLength)GetValue(PreviewColumnProperty);
    public object? Preview { get => GetValue(PreviewProperty); set => SetValue(PreviewProperty, value); }
    public bool PreviewExpanded { get => (bool)GetValue(PreviewExpandedProperty); set => SetValue(PreviewExpandedProperty, value); }

    public override void OnApplyTemplate()
    {
        if (_body is not null) _body.SizeChanged -= OnBodySizeChanged;
        base.OnApplyTemplate();
        _body = GetTemplateChild("PART_Body") as FrameworkElement;
        if (_body is not null) _body.SizeChanged += OnBodySizeChanged;
        SetValue(ScrollViewerPropertyKey, GetTemplateChild("PART_ScrollViewer") as ScrollViewer);
        UpdateLayoutMode();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateLayoutMode();
    }

    private void OnBodySizeChanged(object sender, SizeChangedEventArgs args) => UpdateLayoutMode();

    private void UpdateLayoutMode()
    {
        var width = Math.Min(1680d, Math.Max(0d, ActualWidth - 64d));
        var wide = Preview is not null && width >= 1000d;
        SetValue(PreviewColumnPropertyKey, new GridLength(wide ? Math.Clamp(width * .32d, 360d, 520d) : 0d));
        SetValue(PreviewViewportHeightProperty, Math.Clamp(wide ? (_body?.ActualHeight ?? ActualHeight) - 210 : (_body?.ActualHeight ?? ActualHeight) * .45, 120d, 480d));
        if (wide == IsWide) return;
        _changingLayout = true;
        SetValue(IsWidePropertyKey, wide);
        SetCurrentValue(PreviewExpandedProperty, wide ? _wideExpanded : _compactExpanded);
        _changingLayout = false;
    }

    private static void OnExpandedChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var layout = (SettingsPageLayout)sender;
        if (layout._changingLayout) return;
        if (layout.IsWide) layout._wideExpanded = (bool)args.NewValue;
        else layout._compactExpanded = (bool)args.NewValue;
    }
}
