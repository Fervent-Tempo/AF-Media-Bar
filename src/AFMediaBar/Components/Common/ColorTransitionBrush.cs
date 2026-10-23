// 每个呈现所有者独占可变画刷和动画时钟；隐藏、挂起或释放时由所有者调用 Finish。
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AFMediaBar.Classes.Services;

namespace AFMediaBar.Components;

/// <summary>可中断并接续当前颜色的短过渡，不修改共享或系统画刷。</summary>
internal sealed class ColorTransitionBrush
{
    private readonly SolidColorBrush _brush;
    private readonly ColorValue _value = new();
    private Color? _target;
    private int _generation;

    internal ColorTransitionBrush(SolidColorBrush? brush = null)
    {
        _brush = brush ?? new SolidColorBrush();
        _value.Color = _brush.Color;
        // 资源与样式可能自动冻结闲置画刷；绑定使本实例在没有动画时也保持可变。
        BindingOperations.SetBinding(_brush, SolidColorBrush.ColorProperty,
            new Binding(nameof(ColorValue.Color)) { Source = _value, Mode = BindingMode.OneWay });
    }

    internal static TimeSpan ResolveDuration(bool animate) => animate && !SystemParameters.HighContrast
        ? MotionPolicy.ResolveCurrent().ForegroundColorDuration : TimeSpan.Zero;

    internal bool Owns(SolidColorBrush brush) => ReferenceEquals(_brush, brush);

    internal Brush Apply(Brush target, TimeSpan duration)
    {
        if (!SystemParameters.ClientAreaAnimation) Finish();
        if (SystemParameters.HighContrast || target is not SolidColorBrush solid)
        {
            Finish();
            _target = null;
            return target;
        }
        return Apply(solid.Color, duration);
    }

    internal SolidColorBrush Apply(Color target, TimeSpan duration)
    {
        if (_target == target)
        {
            return _brush;
        }
        var from = _brush.Color;
        var initialized = _target is not null;
        Finish();
        _target = target;
        if (!initialized || duration <= TimeSpan.Zero || from == target)
        {
            _value.Color = target;
            return _brush;
        }
        // 新时钟下一次渲染才开始；基值暂留当前画面，避免这一帧先跳到终点。
        _value.Color = from;

        var generation = _generation;
        var animation = new ColorAnimationUsingKeyFrames { Duration = duration, FillBehavior = FillBehavior.HoldEnd };
        animation.KeyFrames.Add(new DiscreteColorKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new SplineColorKeyFrame(target, KeyTime.FromTimeSpan(duration), new KeySpline(0.77, 0, 0.175, 1)));
        animation.Completed += (_, _) =>
        {
            if (generation == _generation) Finish();
        };
        _brush.BeginAnimation(SolidColorBrush.ColorProperty, animation, HandoffBehavior.SnapshotAndReplace);
        return _brush;
    }

    internal void Finish()
    {
        _generation++;
        if (_target is { } target) _value.Color = target;
        _brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
    }

    private sealed class ColorValue : DependencyObject
    {
        internal static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
            nameof(Color), typeof(Color), typeof(ColorValue), new PropertyMetadata(Colors.Transparent));

        public Color Color
        {
            get => (Color)GetValue(ColorProperty);
            set => SetValue(ColorProperty, value);
        }
    }
}
