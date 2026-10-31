using System.Windows;
using System.Windows.Controls;

namespace AFMediaBar.Components;

/// <summary>
/// 多列布局，每一列各自向下堆叠：子项保持自己的高度，展开一个不会把同列之外的内容拉高或挪走。
/// A multi-column layout that stacks each child in its own column at its own height, so expanding one item
/// never stretches or shifts anything outside that column.
///
/// 与 <see cref="System.Windows.Controls.Primitives.UniformGrid"/> 的差别：后者把所有单元格统一成「全网格最高子项」，
/// 一张卡片展开会把同一网格里每个单元格都撑到同样高度，并给每一行预留同样的空白；子项按
/// 「下标 % 列数」固定分列后，只有所在列会跟着变高。
/// Unlike <see cref="System.Windows.Controls.Primitives.UniformGrid"/>, which sizes every cell to the tallest child in
/// the whole grid, children here are assigned to a column by `index % Columns` and keep their own height.
/// </summary>
public class ColumnFlowPanel : Panel
{
    /// <summary>列数，至少为 1；1 表示单列顺序排列。/ Column count, at least 1; 1 stacks everything in order.</summary>
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns),
        typeof(int),
        typeof(ColumnFlowPanel),
        new FrameworkPropertyMetadata(2, FrameworkPropertyMetadataOptions.AffectsMeasure),
        value => (int)value >= 1);

    /// <summary>列数。/ Number of columns.</summary>
    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var columns = Columns;
        var bounded = !double.IsInfinity(availableSize.Width);
        var slot = bounded ? availableSize.Width / columns : double.PositiveInfinity;
        var heights = new double[columns];
        var widest = 0d;

        var index = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(slot, double.PositiveInfinity));
            heights[index % columns] += child.DesiredSize.Height;
            widest = Math.Max(widest, child.DesiredSize.Width);
            index++;
        }

        var tallest = 0d;
        foreach (var height in heights) tallest = Math.Max(tallest, height);
        return new Size(bounded ? availableSize.Width : widest * columns, tallest);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = Columns;
        var slot = finalSize.Width / columns;
        var offsets = new double[columns];

        var index = 0;
        foreach (UIElement child in InternalChildren)
        {
            var column = index % columns;
            child.Arrange(new Rect(column * slot, offsets[column], slot, child.DesiredSize.Height));
            offsets[column] += child.DesiredSize.Height;
            index++;
        }

        return finalSize;
    }
}
