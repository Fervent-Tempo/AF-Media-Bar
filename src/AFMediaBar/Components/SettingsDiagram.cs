using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AFMediaBar.Components;

/// <summary>
/// 说明图的种类。每一种对应 <c>SettingsDiagrams.xaml</c> 里一个同名模板，
/// 页面只声明“这里要解释什么”，不引用具体模板键。
/// The kind of an explainer diagram. Each kind maps to a same-named template in
/// <c>SettingsDiagrams.xaml</c>, so a page declares what it is explaining rather than a template key.
/// </summary>
public enum SettingsDiagramKind
{
    /// <summary>静置层包含什么。/ What the rest layer contains.</summary>
    LayerRest,

    /// <summary>悬停层包含什么。/ What the hover layer contains.</summary>
    LayerHover,

    /// <summary>完整层包含什么。/ What the full layer contains.</summary>
    LayerFull,

    /// <summary>任务栏位置、图标避让与边缘偏移。/ Taskbar placement, icon avoidance, and edge offset.</summary>
    TaskbarPlacement,

    /// <summary>媒体栏长度与固定长度安全区间。/ Media bar length and the fixed-length safe range.</summary>
    TaskbarLength,

    /// <summary>静置层的点击区域。/ Click zones on the rest layer.</summary>
    InputGestures,

    /// <summary>滚轮方向在不同表面上的含义。/ What a wheel direction means on each surface.</summary>
    WheelGestures,

    /// <summary>托盘溢出区与图标位置。/ The tray overflow area and where the icon must sit.</summary>
    TrayOverflow,

    /// <summary>双行歌词的两行分别是什么。/ What the two lyric lines are.</summary>
    LyricsTwoLine,

    /// <summary>歌词的三种对齐。/ The three lyric alignments.</summary>
    LyricsAlignment,

    /// <summary>通知锚点位于目标显示器工作区。/ Notification anchors inside the target monitor's work area.</summary>
    NotificationPlacement,

    /// <summary>频谱柱数量与参数。/ Spectrum bar count and parameters.</summary>
    Spectrum,
}

/// <summary>
/// 说明图宿主：按 <see cref="Kind"/> 选择模板，并负责循环演示的生命周期。
///
/// 循环演示是装饰性的，所以它只在说明图可见、且所在窗口处于前台且未最小化时运行；
/// 窗口失去前台后立刻停止，避免在后台持续占用 GPU。
/// A diagram host: it selects a template from <see cref="Kind"/> and owns the lifetime of the demo loops.
///
/// The loops are decorative, so they run only while the diagram is visible and its window is foreground and
/// not minimised, and stop the moment the window loses the foreground rather than keep costing GPU time in
/// the background.
/// </summary>
public class SettingsDiagram : Control
{
    /// <summary>脉冲循环的标记；带此标记的元素会做透明度脉冲。/ Marker for a pulsing loop; a marked element pulses its opacity.</summary>
    public const string PulseMarker = "demo-pulse";

    /// <summary>纵向轻推循环的标记；带此标记的元素会做纵向往返。/ Marker for a vertical nudge loop; a marked element travels up and down.</summary>
    public const string NudgeMarker = "demo-nudge-y";

    /// <summary>说明图种类。/ Diagram kind.</summary>
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind),
        typeof(SettingsDiagramKind),
        typeof(SettingsDiagram),
        new PropertyMetadata(SettingsDiagramKind.LayerRest));

    /// <summary>循环演示当前是否允许运行。/ Whether the demo loops are currently allowed to run.</summary>
    private static readonly DependencyPropertyKey IsLivePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsLive),
        typeof(bool),
        typeof(SettingsDiagram),
        new PropertyMetadata(false));

    /// <summary>循环演示当前是否允许运行；由可见性与窗口前台状态共同决定。/ Whether the demo loops may run; decided by visibility together with the window's foreground state.</summary>
    public static readonly DependencyProperty IsLiveProperty = IsLivePropertyKey.DependencyProperty;

    private readonly List<FrameworkElement> _pulsing = [];
    private readonly List<FrameworkElement> _nudging = [];
    private Window? _window;

    /// <summary>
    /// 创建说明图宿主并订阅可见性变化；可见性是循环能否播放的两个条件之一。
    /// Creates the diagram host and subscribes to visibility changes, which is one of the two conditions for
    /// the loops to run.
    /// </summary>
    public SettingsDiagram()
    {
        IsVisibleChanged += (_, _) => RefreshLiveState();
    }

    /// <summary>说明图种类。/ Diagram kind.</summary>
    public SettingsDiagramKind Kind
    {
        get => (SettingsDiagramKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>循环演示当前是否允许运行。/ Whether the demo loops are currently allowed to run.</summary>
    public bool IsLive => (bool)GetValue(IsLiveProperty);

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        CollectMarkedElements(this);
        RefreshLiveState();
    }

    /// <inheritdoc />
    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        DetachWindow();
        AttachWindow(Window.GetWindow(this));
    }

    private void AttachWindow(Window? window)
    {
        if (window is null)
        {
            return;
        }

        _window = window;
        window.Activated += OnWindowStateChanged;
        window.Deactivated += OnWindowStateChanged;
        window.StateChanged += OnWindowStateChanged;
        window.IsVisibleChanged += OnWindowVisibilityChanged;
        RefreshLiveState();
    }

    private void DetachWindow()
    {
        if (_window is null)
        {
            return;
        }

        _window.Activated -= OnWindowStateChanged;
        _window.Deactivated -= OnWindowStateChanged;
        _window.StateChanged -= OnWindowStateChanged;
        _window.IsVisibleChanged -= OnWindowVisibilityChanged;
        _window = null;
    }

    private void OnWindowStateChanged(object? sender, EventArgs e) => RefreshLiveState();

    private void OnWindowVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => RefreshLiveState();

    /// <summary>
    /// 重新计算是否允许播放循环，并在状态翻转时启停动画。停止时把元素恢复到静止终值，
    /// 因为一个停在中途的循环会看起来像渲染错误。
    /// Recomputes whether the loops may run and starts or stops them as the state flips. Stopping restores
    /// the resting value, because a loop frozen mid-cycle reads as a rendering fault.
    /// </summary>
    private void RefreshLiveState()
    {
        var live = IsVisible
                   && _window is { IsVisible: true, WindowState: not WindowState.Minimized, IsActive: true };

        SetValue(IsLivePropertyKey, live);

        foreach (var element in _pulsing)
        {
            if (live)
            {
                StartPulse(element);
            }
            else
            {
                Stop(element, UIElement.OpacityProperty);
            }
        }

        foreach (var element in _nudging)
        {
            if (live)
            {
                StartNudge(element);
            }
            else
            {
                Stop(element, TranslateTransform.YProperty);
            }
        }
    }

    /// <summary>收集模板里带循环标记的元素。标记写成 Tag 而不是命名部件，这样模板可以保持为普通模板而不必暴露部件契约。/ Collects the marked elements; using Tag rather than named parts keeps the templates plain.</summary>
    private void CollectMarkedElements(DependencyObject root)
    {
        _pulsing.Clear();
        _nudging.Clear();
        Walk(root);

        void Walk(DependencyObject node)
        {
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var index = 0; index < count; index++)
            {
                var child = VisualTreeHelper.GetChild(node, index);
                if (child is FrameworkElement element)
                {
                    if (element.Tag is string marker)
                    {
                        if (marker == PulseMarker)
                        {
                            _pulsing.Add(element);
                        }
                        else if (marker == NudgeMarker)
                        {
                            _nudging.Add(element);
                        }
                    }
                }

                Walk(child);
            }
        }
    }

    private static void StartPulse(FrameworkElement element)
    {
        if (element.HasAnimatedProperties)
        {
            return;
        }

        // 起点与终点都是 1，只在中间下探，因此循环停下来时元素仍停在可见的终值上。
        // Both ends are 1 and the dip is in the middle, so a stopped loop leaves the element on a visible value.
        var animation = new DoubleAnimationUsingKeyFrames { Duration = new Duration(TimeSpan.FromMilliseconds(1800)), RepeatBehavior = RepeatBehavior.Forever };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(1d, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0.35d, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(700))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(1d, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1500))));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(1d, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1800))));

        element.BeginAnimation(UIElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static void StartNudge(FrameworkElement element)
    {
        var transform = element.RenderTransform as TranslateTransform;
        if (transform is null)
        {
            transform = new TranslateTransform();
            element.RenderTransform = transform;
        }

        if (transform.HasAnimatedProperties)
        {
            return;
        }

        var animation = new DoubleAnimation(0d, -4d, new Duration(TimeSpan.FromMilliseconds(900)))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        transform.BeginAnimation(TranslateTransform.YProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>
    /// 停止循环并落到静止终值。必须先清除动画再写本地值，反序会被动画快照回退到起始值。
    /// Stops a loop and lands the resting value. The animation must be cleared before the local value is
    /// written, or the snapshot taken at start would roll the element back.
    /// </summary>
    private static void Stop(FrameworkElement element, DependencyProperty property)
    {
        if (property == UIElement.OpacityProperty)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1d;
            return;
        }

        if (element.RenderTransform is TranslateTransform transform)
        {
            transform.BeginAnimation(TranslateTransform.YProperty, null);
            transform.Y = 0d;
        }
    }
}
