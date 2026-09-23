using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Layout;

namespace AFMediaBar.Views.Windows;

/// <summary>
/// 同一表面的实时弹簧形变；只在尚未稳定时监听渲染帧。
/// Real-time spring morphing of one surface; rendering is subscribed only while a value is unsettled.
/// </summary>
public partial class DynamicIslandWindow
{
    private IslandSpringFrame _mediaSpring;
    private IslandSpringFrame _expansionSpring;
    private IslandSpringFrame _pressSpring;
    private bool _rendering;
    private long _lastFrameTimestamp;
    private TimeSpan _lastRenderingTime = TimeSpan.MinValue;
    private double _drawnMediaProgress = double.NaN;
    private double _drawnExpansionProgress = double.NaN;

    private double MediaTarget => _latestSnapshot.IsConnected ? 1 : 0;
    private double ExpansionTarget => !_latestSnapshot.IsConnected ? 0 : _wantsExpanded ? 1 :
        IsPreviewingHold && MotionPolicy.ResolveCurrent().UseContinuousMotion
            ? DynamicIslandHoldPolicy.GetPreviewExpansion(HoldElapsedSeconds, _holdExpansionStart)
            : 0;
    private double PressTarget => _pointerDown && !_pointerCancelled && !_holdTriggered
        ? (_expandedAtPress ? 1 : 1 - DynamicIslandHoldPolicy.GetProgress(HoldElapsedSeconds))
        : 0;

    private void RetargetIsland()
    {
        if (_isClosing)
            return;
        if (!IsVisible || _fullscreenSuppressed || !MotionPolicy.ResolveCurrent().UseContinuousMotion)
        {
            SnapPresentation();
            DrawIsland();
            StopRendering();
            return;
        }
        if (IsMotionSettled())
        {
            DrawIsland();
            return;
        }
        if (_rendering)
            return;
        _rendering = true;
        _lastFrameTimestamp = Stopwatch.GetTimestamp();
        _lastRenderingTime = TimeSpan.MinValue;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_isClosing || !IsVisible || _fullscreenSuppressed)
        {
            StopRendering();
            return;
        }
        if (e is RenderingEventArgs renderingEvent)
        {
            if (renderingEvent.RenderingTime == _lastRenderingTime)
                return;
            _lastRenderingTime = renderingEvent.RenderingTime;
        }
        var now = Stopwatch.GetTimestamp();
        var seconds = Stopwatch.GetElapsedTime(_lastFrameTimestamp, now).TotalSeconds;
        _lastFrameTimestamp = now;
        if (IsPreviewingHold)
            TryCompleteHold();
        if (!MotionPolicy.ResolveCurrent().UseContinuousMotion)
        {
            SnapPresentation();
            DrawIsland();
            StopRendering();
            return;
        }
        _mediaSpring = AdvanceSpring(_mediaSpring, MediaTarget, seconds);
        _expansionSpring = AdvanceSpring(_expansionSpring, ExpansionTarget, seconds);
        _pressSpring = AdvanceSpring(_pressSpring, PressTarget, seconds * 1.8);
        var blend = 1 - Math.Exp(-seconds / 0.055);
        for (var i = 0; i < _activityLevels.Length; i++)
        {
            _activityLevels[i] += (_activityTargets[i] - _activityLevels[i]) * blend;
            if (Math.Abs(_activityLevels[i] - _activityTargets[i]) < 0.005)
                _activityLevels[i] = _activityTargets[i];
        }
        DrawIsland();
        if (IsMotionSettled())
            StopRendering();
    }

    private static IslandSpringFrame AdvanceSpring(IslandSpringFrame frame, double target, double seconds)
    {
        var next = DynamicIslandMotion.Advance(frame, target, seconds);
        return DynamicIslandMotion.IsSettled(next, target) ? new IslandSpringFrame(target, 0) : next;
    }

    private bool IsMotionSettled()
    {
        // A changing hold target must keep receiving frames even if the spring catches it momentarily.
        if (IsPreviewingHold && MotionPolicy.ResolveCurrent().UseContinuousMotion)
            return false;
        if (!DynamicIslandMotion.IsSettled(_mediaSpring, MediaTarget) ||
            !DynamicIslandMotion.IsSettled(_expansionSpring, ExpansionTarget) ||
            !DynamicIslandMotion.IsSettled(_pressSpring, PressTarget))
            return false;
        for (var i = 0; i < _activityLevels.Length; i++)
            if (Math.Abs(_activityLevels[i] - _activityTargets[i]) >= 0.005)
                return false;
        return true;
    }

    private void SnapPresentation()
    {
        _mediaSpring = new IslandSpringFrame(MediaTarget, 0);
        _expansionSpring = new IslandSpringFrame(ExpansionTarget, 0);
        _pressSpring = new IslandSpringFrame(PressTarget, 0);
        Array.Copy(_activityTargets, _activityLevels, _activityLevels.Length);
    }

    private void StopRendering()
    {
        if (!_rendering)
            return;
        CompositionTarget.Rendering -= OnRendering;
        _rendering = false;
        _lastFrameTimestamp = 0;
    }

    private void DrawIsland()
    {
        DrawIslandGeometry();
        var pressScale = 1 - 0.025 * Math.Clamp(_pressSpring.Value, 0, 1);
        if (PressTransform.ScaleX != pressScale)
            PressTransform.ScaleX = PressTransform.ScaleY = pressScale;
        var detailInteractive = _wantsExpanded && DetailLayer.Opacity > 0.5;
        if (DetailLayer.IsHitTestVisible != detailInteractive)
            DetailLayer.IsHitTestVisible = detailInteractive;
        var progressInteractive = detailInteractive && ProgressRow.Opacity > 0.5;
        var controlsInteractive = detailInteractive && ControlsRow.Opacity > 0.5;
        if (ProgressRow.IsHitTestVisible != progressInteractive)
            ProgressRow.IsHitTestVisible = progressInteractive;
        if (ControlsRow.IsHitTestVisible != controlsInteractive)
            ControlsRow.IsHitTestVisible = controlsInteractive;
        DrawActivityBars();
        if (!_latestSnapshot.IsConnected && ArtworkFrame.Opacity < 0.001)
        {
            if (ArtworkImage.Source is not null)
                ArtworkImage.Source = null;
            if (TitleText.Text.Length != 0)
                TitleText.Text = string.Empty;
            if (ArtistText.Text.Length != 0)
                ArtistText.Text = string.Empty;
        }
    }

    private void DrawIslandGeometry()
    {
        // Audio and press frames do not invalidate artwork or text layout, or rebuild the island clip.
        if (_drawnMediaProgress == _mediaSpring.Value && _drawnExpansionProgress == _expansionSpring.Value)
            return;
        var geometry = DynamicIslandGeometry.Calculate(_mediaSpring.Value, _expansionSpring.Value);
        var left = (HostWidth - geometry.Width) / 2;
        // Keep the body fixed and centered. Both FillContains and pointer positions use these body coordinates.
        // Alpha-zero pixels outside this clip pass through the layered HWND without a native window region.
        IslandClip.Rect = new Rect(left, 0, geometry.Width, geometry.Height);
        IslandClip.RadiusX = IslandClip.RadiusY = geometry.Radius;
        PressTransform.CenterY = geometry.Height / 2;

        var artworkScale = geometry.Artwork.Width / ArtworkFrame.Width;
        ArtworkScale.ScaleX = ArtworkScale.ScaleY = artworkScale;
        ArtworkTranslation.X = left + geometry.Artwork.X;
        ArtworkTranslation.Y = geometry.Artwork.Y;
        var expansion = Math.Clamp(_expansionSpring.Value, 0, 1);
        ArtworkClip.RadiusX = ArtworkClip.RadiusY = (5 + 4 * expansion) / artworkScale;
        // Preserve the placeholder's original five-pixel inset while the single artwork visual scales.
        var placeholderScale = (geometry.Artwork.Width - 10) / ((ArtworkFrame.Width - 10) * artworkScale);
        var placeholderInset = 5 / artworkScale - 5;
        ArtworkPlaceholderTransform.Matrix = new Matrix(placeholderScale, 0, 0, placeholderScale,
            placeholderInset, placeholderInset);
        ArtworkFrame.Opacity = geometry.MediaOpacity;
        ActivityTranslation.X = left + geometry.ActivityOrigin.X;
        ActivityTranslation.Y = geometry.ActivityOrigin.Y;
        ActivityIndicator.Opacity = geometry.MediaOpacity;

        var detailOffset = (geometry.Width - DetailLayer.Width) / 2;
        var textLeft = geometry.Artwork.Right + 13;
        TrackTextTranslation.X = textLeft - detailOffset - 87;
        var textWidth = Math.Clamp(geometry.ActivityOrigin.X - 12 - textLeft, 0, TitleText.Width);
        TrackTextClip.Rect = new Rect(0, 0, textWidth, 74);
        DetailTranslation.Y = 8 * (1 - expansion);
        DetailLayer.Opacity = geometry.DetailOpacity;
        // Delay the lower rows until the physical island can contain them; no half-clipped times or buttons.
        var contentProgress = Math.Clamp((geometry.Height - 37) / 123, 0, 1);
        var progressReveal = Reveal(contentProgress, 0.75, 0.95);
        var controlsReveal = Reveal(contentProgress, 0.86, 1);
        ProgressRow.Opacity = progressReveal;
        ControlsRow.Opacity = controlsReveal;
        ProgressRevealTranslation.Y = 4 * (1 - progressReveal);
        ControlsRevealTranslation.Y = 6 * (1 - controlsReveal);
        _drawnMediaProgress = _mediaSpring.Value;
        _drawnExpansionProgress = _expansionSpring.Value;
    }

    private static double Reveal(double progress, double start, double end)
    {
        var value = Math.Clamp((progress - start) / (end - start), 0, 1);
        return value * value * (3 - 2 * value);
    }

    private void DrawActivityBars()
    {
        for (var i = 0; i < _activityBars.Length; i++)
        {
            var height = 3 + 19 * Math.Clamp(_activityLevels[i], 0, 1);
            var clip = (RectangleGeometry)_activityBars[i].Clip;
            if (clip.Rect.Height != height)
                clip.Rect = new Rect(0, (24 - height) / 2, 3, height);
        }
    }
}
