using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AFMediaBar.Classes.Services;

namespace AFMediaBar.Components;

/// <summary>
/// 设置页滑杆的选中与调节输入。
///
/// 滑杆的选中就是键盘焦点：点击或拖动滑杆即获得焦点（行的强调色内圈随之亮起），因此鼠标入口与 Tab 完全一致。
/// 选中时左右方向键按一下走一格、按住则按时间加长每次的距离，滚轮按一下走一格；未选中时滚轮不拦截，页面照常滚动。
/// Selection and adjustment input for settings-page sliders.
///
/// A slider's selection is its keyboard focus: clicking or dragging it takes focus, which lights the row's accent ring, so the
/// mouse entry and Tab behave identically. While selected, an arrow key moves one step and accelerates the longer it is held,
/// and a wheel notch moves one step; while unselected the wheel is left alone so the page keeps scrolling.
/// </summary>
public static class SliderInputBehavior
{
    /// <summary>是否启用该行为；设置页滑杆样式默认开启，任务栏表面的滑杆显式关闭。 / Whether the behavior is attached; settings-row styles enable it and taskbar-surface sliders opt out explicitly.</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(SliderInputBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty HoldProperty = DependencyProperty.RegisterAttached(
        "Hold",
        typeof(HoldState),
        typeof(SliderInputBehavior),
        new PropertyMetadata(null));

    /// <summary>复用同一个委托实例，使 AddHandler 与 RemoveHandler 一定配对。 / One shared delegate instance so AddHandler and RemoveHandler always pair up.</summary>
    private static readonly MouseButtonEventHandler MouseDownHandler = OnPreviewMouseLeftButtonDown;

    /// <summary>读取行为是否启用。/ Reads whether the behavior is attached.</summary>
    /// <param name="element">目标滑杆。/ Target slider.</param>
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    /// <summary>启用或关闭行为。/ Attaches or detaches the behavior.</summary>
    /// <param name="element">目标滑杆。/ Target slider.</param>
    /// <param name="value">是否启用。/ Whether to attach.</param>
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Slider slider)
            return;

        if (e.NewValue is true)
        {
            // 鼠标按下必须用 AddHandler + handledEventsToo 注册：Slider 自己处理"点击轨道直接跳到该点"
            // （IsMoveToPointEnabled 为真时），并在同一个预览事件里把它标记为已处理。用 += 注册的处理器收不到已处理的事件，
            // 于是点滑块圆点能选中、点空白轨道却连焦点都没拿到——这正是"只有点圆点才能选中"的根因。
            // The mouse-down handler must be attached with AddHandler and handledEventsToo: when IsMoveToPointEnabled is true the
            // Slider itself handles "click the track to jump there" and marks that same preview event handled. A handler attached
            // with += never receives a handled event, so clicking the thumb selected the slider while clicking the empty track did
            // not even take focus - the exact reason only the thumb worked.
            slider.AddHandler(Mouse.PreviewMouseDownEvent, MouseDownHandler, handledEventsToo: true);
            slider.PreviewKeyDown += OnPreviewKeyDown;
            slider.PreviewKeyUp += OnPreviewKeyUp;
            slider.PreviewMouseWheel += OnPreviewMouseWheel;
            slider.LostKeyboardFocus += OnLostKeyboardFocus;
            slider.Unloaded += OnUnloaded;
            return;
        }

        slider.RemoveHandler(Mouse.PreviewMouseDownEvent, MouseDownHandler);
        slider.PreviewKeyDown -= OnPreviewKeyDown;
        slider.PreviewKeyUp -= OnPreviewKeyUp;
        slider.PreviewMouseWheel -= OnPreviewMouseWheel;
        slider.LostKeyboardFocus -= OnLostKeyboardFocus;
        slider.Unloaded -= OnUnloaded;
        StopHold(slider);
    }

    /// <summary>
    /// 点击滑杆即选中：这里只补焦点，事件不标记为已处理，因此滑块拖动与点击轨道定位仍然照旧。
    /// A click selects the slider: this only supplies focus and leaves the event unhandled, so thumb dragging and
    /// click-to-position keep working unchanged.
    /// </summary>
    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider slider && !slider.IsKeyboardFocusWithin)
            slider.Focus();
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not Slider slider)
            return;

        var direction = e.Key switch
        {
            Key.Left => SliderAdjustmentDirection.Decrease,
            Key.Right => SliderAdjustmentDirection.Increase,
            _ => (SliderAdjustmentDirection?)null
        };
        if (direction is null)
            return;

        // 方向键由本行为独占：WPF 自带的小步长会让一次按键移动两格，而长按加速也无从实现。
        // The arrow keys belong to this behavior alone: WPF's own small change would move two steps per press and leave no room
        // for hold acceleration.
        e.Handled = true;
        var state = GetHold(slider);
        if (state is { IsHolding: true } && state.Direction == direction.Value)
            return;

        StopHold(slider);
        state = new HoldState { Direction = direction.Value };
        slider.SetValue(HoldProperty, state);
        // 第一次按键立刻走一格，短按因此不需要等计时器。
        // The first press moves one step immediately, so a short press never waits for the timer.
        ApplyStep(slider, ResolveBaseStep(slider), direction.Value);
        state.StartedAt = Stopwatch.GetTimestamp();
        state.Timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = SliderAdjustmentPolicy.RepeatInterval };
        state.Timer.Tick += (_, _) => OnHoldTick(slider);
        state.Timer.Start();
    }

    private static void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (sender is Slider slider && e.Key is Key.Left or Key.Right)
            StopHold(slider);
    }

    private static void OnHoldTick(Slider slider)
    {
        var state = GetHold(slider);
        if (state is not { IsHolding: true })
        {
            StopHold(slider);
            return;
        }

        var held = Stopwatch.GetElapsedTime(state.StartedAt);
        var step = SliderAdjustmentPolicy.ResolveRepeatStep(
            ResolveBaseStep(slider),
            held,
            slider.Minimum,
            slider.Maximum);
        if (!ApplyStep(slider, step, state.Direction))
        {
            // 已经顶到边界：继续每 110 ms 走一次只会空转，因此直接结束本次长按。
            // The value has reached its bound: repeating every 110 ms would only spin, so the hold ends here.
            StopHold(slider);
        }
    }

    /// <summary>
    /// 滚轮只在滑杆已选中时调节，并标记为已处理以阻止页面同时滚动；未选中时完全放行，滚轮仍然是滚页面。
    /// The wheel adjusts only while the slider is selected and marks the event handled so the page does not scroll at the same
    /// time; while unselected the event is left alone and the wheel still scrolls the page.
    /// </summary>
    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not Slider slider || !slider.IsKeyboardFocusWithin || e.Delta == 0)
            return;

        var direction = e.Delta > 0 ? SliderAdjustmentDirection.Increase : SliderAdjustmentDirection.Decrease;
        ApplyStep(slider, ResolveBaseStep(slider), direction);
        e.Handled = true;
    }

    private static void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is Slider slider)
            StopHold(slider);
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is Slider slider)
            StopHold(slider);
    }

    /// <summary>按当前步长调节一次，并返回取值是否真的发生了变化。 / Adjusts once by the given step and reports whether the value actually moved.</summary>
    private static bool ApplyStep(Slider slider, double step, SliderAdjustmentDirection direction)
    {
        if (!double.IsFinite(step) || step <= 0)
            return false;

        var next = SliderAdjustmentPolicy.Adjust(slider.Value, step, direction, slider.Minimum, slider.Maximum);
        if (Math.Abs(next - slider.Value) < double.Epsilon)
            return false;

        slider.Value = next;
        return true;
    }

    private static double ResolveBaseStep(Slider slider) =>
        SliderAdjustmentPolicy.ResolveBaseStep(
            slider.Minimum,
            slider.Maximum,
            slider.IsSnapToTickEnabled ? slider.TickFrequency : 0,
            slider.SmallChange);

    private static HoldState? GetHold(DependencyObject element) => (HoldState?)element.GetValue(HoldProperty);

    private static void StopHold(Slider slider)
    {
        if (GetHold(slider) is not { } state)
            return;

        state.Timer?.Stop();
        state.Timer = null;
        state.IsHolding = false;
        slider.ClearValue(HoldProperty);
    }

    /// <summary>一次长按的进行时状态：方向、起始时刻与重复计时器。 / State of one in-progress hold: direction, start timestamp, and repeat timer.</summary>
    private sealed class HoldState
    {
        public SliderAdjustmentDirection Direction { get; init; }

        public long StartedAt { get; set; }

        public DispatcherTimer? Timer { get; set; }

        public bool IsHolding { get; set; } = true;
    }
}
