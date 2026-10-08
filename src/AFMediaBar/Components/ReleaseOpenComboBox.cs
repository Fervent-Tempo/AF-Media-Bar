// 设备选择入口在鼠标抬起后打开列表，避免弹层接收同一次点击；鼠标捕获由控件持有并在抬起或丢失时清理。
using System.Windows.Controls;
using System.Windows.Input;

namespace AFMediaBar.Components;

/// <summary>单次点击只打开下拉列表，保留 ComboBox 的键盘、滚轮和列表选择行为。</summary>
public class ReleaseOpenComboBox : ComboBox
{
    private bool _isOpeningClickPending;

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (IsDropDownOpen)
        {
            base.OnPreviewMouseLeftButtonDown(e);
            return;
        }

        // WPF-UI 在按下时打开 Popup；屏幕边缘调整位置或入场动画会让选项接收到这次抬起。
        e.Handled = true;
        Focus();
        _isOpeningClickPending = CaptureMouse();
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_isOpeningClickPending)
        {
            base.OnPreviewMouseLeftButtonUp(e);
            return;
        }

        _isOpeningClickPending = false;
        e.Handled = true;
        var position = e.GetPosition(this);
        ReleaseMouseCapture();
        if (IsEnabled && IsVisible &&
            position.X >= 0 && position.X < ActualWidth &&
            position.Y >= 0 && position.Y < ActualHeight)
        {
            SetCurrentValue(IsDropDownOpenProperty, true);
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        _isOpeningClickPending = false;
        base.OnLostMouseCapture(e);
    }
}
