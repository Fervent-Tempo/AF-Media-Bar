using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AFMediaBar.Components.Wpf.BuiltIn.Spectrum;

public partial class SpectrumView : UserControl
{
    public SpectrumView() => InitializeComponent();
}

public sealed class SpectrumBars : FrameworkElement
{
    private readonly float[] _values = new float[9];
    private long _lastRenderTick;

    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<float>), typeof(SpectrumBars),
        new FrameworkPropertyMetadata(Array.Empty<float>(), OnValuesChanged));
    public static readonly DependencyProperty BandCountProperty = DependencyProperty.Register(
        nameof(BandCount), typeof(int), typeof(SpectrumBars), new FrameworkPropertyMetadata(9, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty RefreshRateHzProperty = DependencyProperty.Register(
        nameof(RefreshRateHz), typeof(int), typeof(SpectrumBars), new PropertyMetadata(20));
    public static readonly DependencyProperty SensitivityPercentProperty = DependencyProperty.Register(
        nameof(SensitivityPercent), typeof(int), typeof(SpectrumBars), new FrameworkPropertyMetadata(100, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty BarBrushProperty = DependencyProperty.Register(
        nameof(BarBrush), typeof(Brush), typeof(SpectrumBars), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<float> Values { get => (IReadOnlyList<float>)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public int BandCount { get => (int)GetValue(BandCountProperty); set => SetValue(BandCountProperty, value); }
    public int RefreshRateHz { get => (int)GetValue(RefreshRateHzProperty); set => SetValue(RefreshRateHzProperty, value); }
    public int SensitivityPercent { get => (int)GetValue(SensitivityPercentProperty); set => SetValue(SensitivityPercentProperty, value); }
    public Brush BarBrush { get => (Brush)GetValue(BarBrushProperty); set => SetValue(BarBrushProperty, value); }

    private static void OnValuesChanged(DependencyObject target, DependencyPropertyChangedEventArgs args) =>
        ((SpectrumBars)target).UpdateValues(args.NewValue as IReadOnlyList<float> ?? Array.Empty<float>());

    private void UpdateValues(IReadOnlyList<float> values)
    {
        var refreshRate = Math.Clamp(RefreshRateHz, 5, 30);
        var now = Environment.TickCount64;
        if (now - _lastRenderTick < 1_000 / refreshRate) return;
        _lastRenderTick = now;
        for (var index = 0; index < _values.Length; index++)
            _values[index] = index < values.Count ? Math.Clamp(values[index] * SensitivityPercent / 100f, 0, 1) : 0;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var count = Math.Clamp(BandCount, 1, _values.Length);
        var width = ActualWidth > 0 ? ActualWidth : 88;
        var height = ActualHeight > 0 ? ActualHeight : 24;
        const double gap = 3;
        var barWidth = Math.Max(2, (width - gap * (count - 1)) / count);
        for (var index = 0; index < count; index++)
        {
            var barHeight = Math.Clamp(3 + Math.Sqrt(_values[index]) * (height - 3), 3, height);
            drawingContext.DrawRoundedRectangle(BarBrush, null, new Rect(index * (barWidth + gap), (height - barHeight) / 2, barWidth, barHeight), 2, 2);
        }
    }

    protected override Size MeasureOverride(Size availableSize) => new(Math.Min(88, availableSize.Width), 24);
}
