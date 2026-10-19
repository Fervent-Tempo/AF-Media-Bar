// 列表排序手柄，仅表达输入意图；所在列表的排序行为负责捕获与预览。
using System.Windows;
using System.Windows.Controls.Primitives;

namespace AFMediaBar.Components;

/// <summary>支持拖动与键盘排序的手柄，ItemKey 用于列表重建后恢复焦点。</summary>
public sealed class SettingsReorderHandle : Thumb
{
    public static readonly DependencyProperty ItemKeyProperty = DependencyProperty.Register(
        nameof(ItemKey), typeof(object), typeof(SettingsReorderHandle), new PropertyMetadata(null));

    public object? ItemKey { get => GetValue(ItemKeyProperty); set => SetValue(ItemKeyProperty, value); }
}
