// 独立绘制团子与双眼，只接收呈现输入，不查询原生鼠标。
// 控件拥有眨眼时钟；宿主挂起或卸载时停止动画并恢复睁眼。
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;

namespace AFMediaBar.Components;

/// <summary>随视线移动双眼的矢量小宠物。</summary>
public sealed class PetControl : FrameworkElement
{
    private static readonly Geometry _body = CreateBody();
    private Brush _bodyBrush = Brushes.Maroon;
    private Brush _eyeBrush = Brushes.White;
    private PetColors? _colors;
    private readonly DispatcherTimer _blinkReset;
    private readonly TranslateTransform _gaze = new();
    private readonly ScaleTransform _eyelids = new(1, 1, 0, 32);
    private Point? _screenPosition;
    private Vector? _targetGaze;

    public PetControl()
    {
        ApplySettings(new());
        _blinkReset = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _blinkReset.Tick += OnBlinkReset;
        Unloaded += (_, _) => Suspend();
        LayoutUpdated += (_, _) => UpdateGaze();
    }

    internal void ApplySettings(PetComponentSettings settings)
    {
        var colors = PetVisualDefinition.Colors(settings);
        if (_colors == colors) return;
        _colors = colors;
        ColorHex.TryParse(colors.Body, out var body);
        ColorHex.TryParse(colors.Eyes, out var eyes);
        var bodyBrush = new SolidColorBrush(body);
        var eyeBrush = new SolidColorBrush(eyes);
        bodyBrush.Freeze();
        eyeBrush.Freeze();
        _bodyBrush = bodyBrush;
        _eyeBrush = eyeBrush;
        InvalidateVisual();
    }

    internal void Present(PetInputSnapshot input)
    {
        _screenPosition = input.ScreenPosition;
        UpdateGaze();
        if (input.Blink) Blink();
    }

    private void UpdateGaze()
    {
        if (_screenPosition is not { } position || !IsVisible || ActualWidth <= 0 || ActualHeight <= 0 ||
            PresentationSource.FromVisual(this) is null) return;
        var center = PointToScreen(new Point(ActualWidth / 2, ActualHeight / 2));
        var dpi = VisualTreeHelper.GetDpi(this);
        var direction = new Vector((position.X - center.X) / (400 * dpi.DpiScaleX),
            (position.Y - center.Y) / (400 * dpi.DpiScaleY));
        if (direction.Length > 1) direction.Normalize();
        var target = new Vector(direction.X * 4, direction.Y * 3);
        if (_targetGaze is { } previous && (target - previous).Length < 0.01) return;
        _targetGaze = target;
        var motion = MotionPolicy.ResolveCurrent();
        MoveEye(TranslateTransform.XProperty, target.X, motion.Mode == MotionMode.Full);
        MoveEye(TranslateTransform.YProperty, target.Y, motion.Mode == MotionMode.Full);
    }

    private void MoveEye(DependencyProperty property, double target, bool animate)
    {
        var current = (double)_gaze.GetValue(property);
        _gaze.BeginAnimation(property, null);
        _gaze.SetValue(property, target);
        if (animate)
            _gaze.BeginAnimation(property, new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(90))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            });
    }

    private void Blink()
    {
        var current = _eyelids.ScaleY;
        _eyelids.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _eyelids.ScaleY = 1;
        _blinkReset.Stop();
        if (!MotionPolicy.ResolveCurrent().UseTransitions)
        {
            _eyelids.ScaleY = 0.08;
        }
        else
        {
            var blink = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            blink.KeyFrames.Add(new LinearDoubleKeyFrame(current, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            blink.KeyFrames.Add(new EasingDoubleKeyFrame(0.08, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(60))));
            blink.KeyFrames.Add(new LinearDoubleKeyFrame(0.08, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(95))));
            blink.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200)))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
            _eyelids.BeginAnimation(ScaleTransform.ScaleYProperty, blink);
        }
        _blinkReset.Start();
    }

    private void OnBlinkReset(object? sender, EventArgs args)
    {
        _blinkReset.Stop();
        _eyelids.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _eyelids.ScaleY = 1;
    }

    internal void Suspend()
    {
        _screenPosition = null;
        _targetGaze = null;
        _blinkReset.Stop();
        _gaze.BeginAnimation(TranslateTransform.XProperty, null);
        _gaze.BeginAnimation(TranslateTransform.YProperty, null);
        _eyelids.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _gaze.X = _gaze.Y = 0;
        _eyelids.ScaleY = 1;
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        drawing.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
        drawing.PushTransform(new ScaleTransform(size / 64, size / 64));
        var highContrast = SystemParameters.HighContrast;
        drawing.DrawGeometry(highContrast ? SystemColors.WindowBrush : _bodyBrush,
            highContrast ? new Pen(SystemColors.WindowTextBrush, 1.5) : null, _body);
        var eyes = highContrast ? SystemColors.WindowTextBrush : _eyeBrush;
        drawing.PushTransform(_gaze);
        drawing.PushTransform(_eyelids);
        foreach (var centerX in new[] { 24d, 40d })
            drawing.DrawRoundedRectangle(eyes, null,
                new Rect(centerX - 3, 25.5, 6, 13), 3, 3);
        drawing.Pop();
        drawing.Pop();
        drawing.Pop();
        drawing.Pop();
    }

    private static Geometry CreateBody()
    {
        // 本项目独立绘制的贝塞尔轮廓，不包含参考库的几何数据。
        var geometry = Geometry.Parse(PetVisualDefinition.BodyPath);
        geometry.Freeze();
        return geometry;
    }

}
