// 设备与性能区域共用的响应式等宽网格；按可用宽度换行，不缩放子控件。
using System.Windows;
using System.Windows.Controls.Primitives;

namespace AFMediaBar.Components;

/// <summary>在最大列数内保留每列的可读宽度，高度由实际子项决定。</summary>
public sealed class ResponsiveUniformGrid : UniformGrid
{
    public static readonly DependencyProperty MinimumColumnWidthProperty = DependencyProperty.Register(
        nameof(MinimumColumnWidth), typeof(double), typeof(ResponsiveUniformGrid),
        new FrameworkPropertyMetadata(112d, FrameworkPropertyMetadataOptions.AffectsMeasure), value => value is double width && double.IsFinite(width) && width > 0);
    public static readonly DependencyProperty MaximumColumnsProperty = DependencyProperty.Register(
        nameof(MaximumColumns), typeof(int), typeof(ResponsiveUniformGrid),
        new FrameworkPropertyMetadata(3, FrameworkPropertyMetadataOptions.AffectsMeasure), value => value is int columns && columns > 0);
    public double MinimumColumnWidth { get => (double)GetValue(MinimumColumnWidthProperty); set => SetValue(MinimumColumnWidthProperty, value); }
    public int MaximumColumns { get => (int)GetValue(MaximumColumnsProperty); set => SetValue(MaximumColumnsProperty, value); }

    protected override Size MeasureOverride(Size constraint)
    {
        var columns = double.IsFinite(constraint.Width)
            ? (int)Math.Clamp(Math.Floor(constraint.Width / MinimumColumnWidth), 1, MaximumColumns) : MaximumColumns;
        if (Columns != columns) SetCurrentValue(ColumnsProperty, columns);
        return base.MeasureOverride(constraint);
    }
}
