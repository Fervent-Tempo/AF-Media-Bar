// Coordinates taskbar rest-component transitions across media connection changes.
// The control owns and releases temporary bitmap ghosts and its completion timer.
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Components;

/// <summary>媒体栏静置组件的身份匹配过渡。/ Identity-matched transitions for taskbar rest components.</summary>
public partial class TaskBarMediaControl
{
    private readonly List<Image> _restTransitionGhosts = [];
    private DispatcherTimer? _restTransitionTimer;
    private readonly List<RestMove> _restTransitionMoves = [];
    private long _restTransitionStartedAt;
    private TimeSpan _restTransitionDuration;
    private IEasingFunction? _restTransitionEase;
    private ScaleTransform? _restTextClipScale;
    private double _restTextClipWidth;
    private double _restTextLeft;
    private double _restTextStartVisibleWidth;
    private FrameworkElement? _restIncomingArtwork;
    private double _restIncomingArtworkOriginalOpacity;
    private bool _restTransitionDefersHide;
    private bool _restTransitionActive;
    private bool _restTransitionEntering;
    private bool _restTransitionPreparing;
    private bool _restTransitionTargetPublished;
    private bool _restTransitionKeepsOutgoingText;
    private double _outgoingTextLeft;
    private double _outgoingTextWidth;
    private double _outgoingTextHeight;
    private double _outgoingTextVisibleWidth;

    /// <summary>宿主在切换期间协同提交目标宽度。/ Lets the host coordinate the target width during a connection transition.</summary>
    public bool IsRestConnectionTransitionActive => _restTransitionActive;

    /// <summary>进入媒体状态时先扩展宿主；退出时待文字收拢后再缩短。/ Expand the host before entry, and shrink after exit.</summary>
    public bool IsRestConnectionTransitionEntering => _restTransitionEntering;

    /// <summary>宿主在拖动或任务栏运动期间关闭这类视觉过渡。/ The host disables this visual transition during dragging or taskbar motion.</summary>
    public bool AllowRestConnectionTransition { get; set; } = true;

    /// <summary>退场完成后通知宿主重算窗口显隐。/ Notifies the host to re-evaluate window visibility after an exit.</summary>
    public event EventHandler? RestTransitionFinished;

    private sealed record RestVisual(
        Point Position,
        double Width,
        double Height,
        BitmapSource? Bitmap,
        double VisibleWidth);

    private sealed record RestMove(
        FrameworkElement Element,
        TranslateTransform Transform,
        double FromX,
        double FromY,
        double TargetX,
        bool Appearing,
        double OriginalOpacity,
        Transform OriginalTransform);

    private Dictionary<TaskbarRestComponent, RestVisual>? CaptureRestVisuals(bool targetConnected)
    {
        if (!AllowRestConnectionTransition || !IsLoaded || _currentMode != WindowMode.Taskbar ||
            _isVertical || !CurrentMotion.UseTransitions)
            return null;

        var experience = SettingsManager.Current.TaskbarExperience.Normalize();
        var targetVisibility = new TaskbarRestLayoutPolicy.Visibility(
            targetConnected,
            experience.IdleComponents,
            experience.SpectrumVisible,
            experience.PerformanceVisible,
            experience.OutputDeviceVisible,
            experience.VolumeVisible,
            experience.ArtworkVisible);
        var visuals = new Dictionary<TaskbarRestComponent, RestVisual>();
        foreach (var component in TaskbarRestLayoutPolicy.DefaultOrder)
        {
            var element = GetRestElement(component);
            if (element.Visibility != Visibility.Visible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
                continue;

            if (!TryGetRestPosition(element, MainCanvas, out var point))
                continue;
            var dpi = VisualTreeHelper.GetDpi(element);
            var pixelWidth = (int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX);
            var pixelHeight = (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY);
            BitmapSource? bitmap = null;
            var needsBitmap = component == TaskbarRestComponent.Artwork ||
                (component != TaskbarRestComponent.MediaText &&
                 !TaskbarRestLayoutPolicy.IsVisible(component, targetVisibility));
            if (needsBitmap && pixelWidth > 0 && pixelHeight > 0 && pixelWidth <= 4096 && pixelHeight <= 1024)
            {
                try
                {
                    var render = new RenderTargetBitmap(pixelWidth, pixelHeight,
                        96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
                    render.Render(element);
                    render.Freeze();
                    bitmap = render;
                }
                catch (Exception ex)
                {
                    // A temporarily detached WebView or visual can still join the live transition without a ghost.
                    Debug.WriteLine(ex);
                }
            }

            var visibleWidth = element.Clip is RectangleGeometry { Transform: ScaleTransform scale }
                ? element.ActualWidth * scale.ScaleX
                : element.ActualWidth;
            visuals[component] = new RestVisual(point, element.ActualWidth,
                element.ActualHeight, bitmap, visibleWidth);
        }

        return visuals;
    }

    private FrameworkElement GetRestElement(TaskbarRestComponent component) => component switch
    {
        TaskbarRestComponent.Artwork => SongImageBorder,
        TaskbarRestComponent.MediaText => SongInfoStackPanel,
        TaskbarRestComponent.Spectrum => TaskbarSpectrumHoverSurface,
        TaskbarRestComponent.Performance => TaskbarPerformanceHoverSurface,
        TaskbarRestComponent.OutputDevice => TaskbarOutputDeviceHoverSurface,
        TaskbarRestComponent.Volume => TaskbarVolumeHoverSurface,
        _ => throw new ArgumentOutOfRangeException(nameof(component))
    };

    internal static bool TryGetRestPosition(FrameworkElement element, UIElement canvas, out Point position)
    {
        position = default;
        if (element.FindCommonVisualAncestor(canvas) is null)
            return false;

        try
        {
            // The four rest widgets are siblings of MainCanvas, not descendants of it.
            // TranslatePoint uses their shared ancestor and also covers artwork/text inside the canvas.
            position = element.TranslatePoint(new Point(), canvas);
            return double.IsFinite(position.X) && double.IsFinite(position.Y);
        }
        catch (InvalidOperationException)
        {
            // A control can leave the visual tree while its media or host window is changing.
            return false;
        }
    }

    private void PrepareRestConnectionTransition(Dictionary<TaskbarRestComponent, RestVisual>? before, bool entering)
    {
        if (before is null)
        {
            StopRestTransitions();
            return;
        }

        StopRestTransitions();
        // A title-change entrance can still own the same text surface. Settle it before the
        // connection clip takes over, so two independent opacity/translation clocks cannot fight.
        SongInfoStackPanel.BeginAnimation(OpacityProperty, null);
        SongInfoStackPanel.Opacity = 1;
        if (SongInfoStackPanel.RenderTransform is TranslateTransform textTransform)
        {
            textTransform.BeginAnimation(TranslateTransform.XProperty, null);
            textTransform.X = 0;
        }
        _restTransitionActive = true;
        _restTransitionEntering = entering;
        _restTransitionPreparing = true;
        _restTransitionDefersHide = !entering;
        InteractionSurface.IsHitTestVisible = false;
        if (!entering && before.TryGetValue(TaskbarRestComponent.MediaText, out var text))
        {
            _restTransitionKeepsOutgoingText = true;
            _outgoingTextLeft = text.Position.X;
            _outgoingTextWidth = text.Width;
            _outgoingTextHeight = text.Height;
            _outgoingTextVisibleWidth = text.VisibleWidth;
        }
    }

    private void PublishRestTransitionTargetSize()
    {
        _restTransitionPreparing = false;
        RaiseDesiredSizeChanged(isForcedRefresh: true);
        _restTransitionTargetPublished = true;
    }

    private void AnimateRestConnectionChange(Dictionary<TaskbarRestComponent, RestVisual>? before)
    {
        if (before is null || !_restTransitionActive)
            return;

        _restTransitionMoves.Clear();
        _restIncomingArtwork = null;
        _restTextClipScale = null;
        _restTransitionDuration = CurrentMotion.RestConnectionDuration;
        _restTransitionEase = MotionPolicy.CreateRestConnectionEase();
        InteractionSurface.UpdateLayout();

        foreach (var component in TaskbarRestLayoutPolicy.DefaultOrder)
        {
            if (component == TaskbarRestComponent.MediaText)
                continue;

            var element = GetRestElement(component);
            var wasVisible = before.TryGetValue(component, out var previous);
            var isVisible = element.Visibility == Visibility.Visible &&
                (element.ActualWidth > 0 || (double.IsFinite(element.Width) && element.Width > 0));

            if (wasVisible && (component == TaskbarRestComponent.Artwork || !isVisible))
                AddRestTransitionGhost(previous!);

            if (!isVisible)
                continue;

            if (component == TaskbarRestComponent.Artwork)
            {
                // Note and cover occupy one slot: keep the old pixels above the new artwork.
                _restIncomingArtworkOriginalOpacity = element.Opacity;
                element.Opacity = 0;
                _restIncomingArtwork = element;
                continue;
            }

            if (!TryGetRestPosition(element, MainCanvas, out var target))
                continue;

            var fromX = wasVisible ? previous!.Position.X - target.X : 0;
            var fromY = wasVisible ? previous!.Position.Y - target.Y : 0;
            var originalOpacity = element.Opacity;
            var originalTransform = element.RenderTransform;
            var transform = new TranslateTransform(fromX, fromY);
            element.RenderTransform = transform;
            element.Opacity = wasVisible ? originalOpacity : 0;
            _restTransitionMoves.Add(new RestMove(
                element, transform, fromX, fromY, target.X, !wasVisible, originalOpacity, originalTransform));
        }

        // A live clip keeps WebView lyrics readable and retracts in reverse on exit.
        if (SongInfoStackPanel.Visibility == Visibility.Visible)
        {
            var width = _restTransitionEntering ? SongInfoStackPanel.Width : _outgoingTextWidth;
            var height = _restTransitionEntering ? SongInfoStackPanel.ActualHeight : _outgoingTextHeight;
            if (double.IsFinite(width) && width > 0 && double.IsFinite(height) && height > 0)
            {
                _restTextClipWidth = width;
                _restTextLeft = _restTransitionEntering
                    ? Canvas.GetLeft(SongInfoStackPanel)
                    : _outgoingTextLeft;
                _restTextStartVisibleWidth = _restTransitionEntering
                    ? before.TryGetValue(TaskbarRestComponent.MediaText, out var text) ? text.VisibleWidth : 0
                    : _outgoingTextVisibleWidth;
                _restTextClipScale = new ScaleTransform(0, 1,
                    ResolvedTaskbarArrangement == TaskbarArrangement.Right ? width : 0, 0);
                SongInfoStackPanel.Clip = new RectangleGeometry(new Rect(0, 0, width, height))
                {
                    Transform = _restTextClipScale
                };
            }
        }

        _restTransitionStartedAt = Stopwatch.GetTimestamp();
        AdvanceRestTransition(0);
        _restTransitionTimer ??= new DispatcherTimer(DispatcherPriority.Render);
        _restTransitionTimer.Interval = TimeSpan.FromMilliseconds(16);
        _restTransitionTimer.Tick -= RestTransitionTimer_Tick;
        _restTransitionTimer.Tick += RestTransitionTimer_Tick;
        _restTransitionTimer.Start();
    }

    private void AddRestTransitionGhost(RestVisual previous)
    {
        if (previous.Bitmap is null)
            return;

        var ghost = new Image
        {
            Source = previous.Bitmap,
            Width = previous.Width,
            Height = previous.Height,
            IsHitTestVisible = false,
            Opacity = 1,
            Stretch = Stretch.Fill
        };
        Canvas.SetLeft(ghost, previous.Position.X);
        Canvas.SetTop(ghost, previous.Position.Y);
        Panel.SetZIndex(ghost, 100);
        MainCanvas.Children.Add(ghost);
        _restTransitionGhosts.Add(ghost);
    }

    private void RestTransitionTimer_Tick(object? sender, EventArgs e)
    {
        var elapsed = Stopwatch.GetElapsedTime(_restTransitionStartedAt).TotalMilliseconds;
        var duration = Math.Max(1, _restTransitionDuration.TotalMilliseconds);
        var fraction = Math.Clamp(elapsed / duration, 0, 1);
        AdvanceRestTransition(fraction);
        if (fraction >= 1)
        {
            _restTransitionTimer?.Stop();
            FinishRestTransition();
        }
    }

    private void AdvanceRestTransition(double fraction)
    {
        var progress = _restTransitionEase?.Ease(fraction) ?? fraction;
        foreach (var move in _restTransitionMoves)
        {
            move.Transform.X = RestConnectionTransitionPolicy.WidgetLeft(
                move.TargetX + move.FromX, move.TargetX, progress) - move.TargetX;
            move.Transform.Y = move.FromY * (1 - progress);
            if (move.Appearing)
                move.Element.Opacity = progress * move.OriginalOpacity;
        }

        if (_restIncomingArtwork is not null)
            _restIncomingArtwork.Opacity = progress * _restIncomingArtworkOriginalOpacity;
        foreach (var ghost in _restTransitionGhosts)
            ghost.Opacity = 1 - progress;

        if (_restTextClipScale is null)
            return;

        var targetWidth = _restTransitionEntering ? _restTextClipWidth : 0;
        // The rightmost edge of live text must stay behind every retained widget, including when
        // a video cover is wider than the idle note or the idle note has been disabled.
        var fromRight = ResolvedTaskbarArrangement == TaskbarArrangement.Right;
        var boundary = _restTransitionMoves.Count == 0
            ? double.PositiveInfinity
            : fromRight
                ? -_restTransitionMoves.Max(move => move.TargetX + move.Transform.X + move.Element.ActualWidth)
                : _restTransitionMoves.Min(move => move.TargetX + move.Transform.X);
        var textStart = fromRight ? -(_restTextLeft + _restTextClipWidth) : _restTextLeft;
        var visibleWidth = RestConnectionTransitionPolicy.VisibleTextWidth(
            _restTextStartVisibleWidth, targetWidth, progress, textStart,
            boundary, _restTextClipWidth);
        _restTextClipScale.ScaleX = Math.Clamp(visibleWidth / _restTextClipWidth, 0, 1);
    }

    private void FinishRestTransition()
    {
        var wasActive = _restTransitionActive;
        var clearOutgoingText = _restTransitionKeepsOutgoingText;
        _restTransitionKeepsOutgoingText = false;
        if (clearOutgoingText)
        {
            SongTitle.Text = string.Empty;
            SongArtist.Text = string.Empty;
            SongLyricsPanel.Opacity = 0;
            SongLyricsPanel.IsHitTestVisible = false;
            UpdateWebLyricsPresentation(allowTransition: false);
            SongInfoStackPanel.Visibility = Visibility.Collapsed;
        }

        _restTransitionActive = false;
        _restTransitionDefersHide = false;
        _restTransitionPreparing = false;
        _restTransitionTargetPublished = false;
        SongInfoStackPanel.Clip = null;
        InteractionSurface.IsHitTestVisible = true;
        RestoreRestTransitionVisuals();
        _restTransitionMoves.Clear();
        _restIncomingArtwork = null;
        _restTextClipScale = null;
        _restTransitionEase = null;
        ClearRestTransitionGhosts();
        if (wasActive)
        {
            RestTransitionFinished?.Invoke(this, EventArgs.Empty);
            RaiseDesiredSizeChanged(isForcedRefresh: true);
        }
    }

    private void ClearRestTransitionGhosts()
    {
        foreach (var ghost in _restTransitionGhosts)
        {
            MainCanvas.Children.Remove(ghost);
        }
        _restTransitionGhosts.Clear();
    }

    private void RestoreRestTransitionVisuals()
    {
        if (_restIncomingArtwork is not null)
            _restIncomingArtwork.Opacity = _restIncomingArtworkOriginalOpacity;
        foreach (var move in _restTransitionMoves)
        {
            move.Element.Opacity = move.OriginalOpacity;
            move.Element.RenderTransform = move.OriginalTransform;
        }
    }

    private void StopRestTransitions()
    {
        _restTransitionTimer?.Stop();
        _restTransitionActive = false;
        _restTransitionDefersHide = false;
        _restTransitionPreparing = false;
        _restTransitionTargetPublished = false;
        _restTransitionKeepsOutgoingText = false;
        SongInfoStackPanel.Clip = null;
        InteractionSurface.IsHitTestVisible = true;
        RestoreRestTransitionVisuals();
        _restTransitionMoves.Clear();
        _restIncomingArtwork = null;
        _restTextClipScale = null;
        _restTransitionEase = null;
        ClearRestTransitionGhosts();
    }
}
