using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AFMediaBar.Adapters;
using AFMediaBar.Interop;
using AFMediaBar.Models;
using AFMediaBar.Services;
using Loc = AFMediaBar.Services.Localization;

namespace AFMediaBar;

/// <summary>
/// 协调媒体快照渲染、展开状态和跑马灯动画，不获取或持有 WinRT 媒体会话。
/// Coordinates media snapshot rendering, expansion state, and marquee animation without owning WinRT sessions.
/// </summary>
public partial class MainWindow
{
    private const double CollapsedInfoWidth = 210;
    private const double ExpandedInfoWidth = 96;
    private const double AudioVisualizerWidth = 88;
    private const double AudioVisualizerCenterBias = 10;

    private readonly DispatcherTimer _collapseTimer;
    private readonly DispatcherTimer _marqueeTimer;
    private IReadOnlyList<MediaSessionOption> _mediaSessions = [];
    private MediaSnapshot? _lastSnapshot;
    // 断开提示只缓存错误标题的词典 key；detail 是异常信息，不做本地化重放。
    // Only the localized title key is cached; exception details are not replayed on language changes.
    private string? _disconnectedTitleKey;
    private bool _hasConnectedMedia;
    private bool _selectedMediaIsPlaying;

    private void OnSnapshotChanged(object? sender, MediaSnapshot snapshot)
    {
        _disconnectedTitleKey = null;
        Dispatcher.InvokeAsync(() => ApplySnapshot(snapshot));
    }

    private void OnSessionsChanged(IReadOnlyList<MediaSessionOption> sessions)
    {
        Dispatcher.InvokeAsync(() => ApplySessions(sessions));
    }

    private void ApplySnapshot(MediaSnapshot snapshot)
    {
        _lastSnapshot = snapshot;
        ComponentSurface_OnSnapshotChanged(snapshot);
        _selectedMediaIsPlaying = snapshot.IsConnected && snapshot.IsPlaying;
        _hasConnectedMedia = _mediaSessions.Any(session => session.IsPlaying) ||
            (_selectedMediaIsPlaying && _mediaSessions.Count == 0);
        var volumeSourceChanged = !string.Equals(
            _lastVolumeSourceId,
            snapshot.SourceId,
            StringComparison.OrdinalIgnoreCase);
        _lastVolumeSourceId = snapshot.SourceId;
        if (volumeSourceChanged)
        {
            Interlocked.Increment(ref _volumeRefreshVersion);
            _volumeApplyTimer.Stop();
            _pendingVolumePercent = null;
            _pendingVolumeWheelSteps = 0;
            _currentApplicationVolume = null;
        }

        var displayTitle = !snapshot.IsConnected &&
            string.IsNullOrWhiteSpace(snapshot.Title)
            ? Loc.Get("Main.Placeholder.Title")
            : snapshot.Title;
        var displayArtist = !snapshot.IsConnected &&
            string.IsNullOrWhiteSpace(snapshot.Artist)
            ? Loc.Get("Main.Placeholder.Subtitle")
            : snapshot.Artist;
        TitleText.Text = displayTitle;
        ArtistText.Text = displayArtist;
        ArtworkImage.Source = snapshot.Artwork.AsImageSource();
        VerticalTitleText.Text = FormatVerticalText(displayTitle);
        VerticalArtistText.Text = FormatVerticalText(displayArtist);
        VerticalTitleText.ToolTip = displayTitle;
        VerticalArtistText.ToolTip = displayArtist;
        VerticalArtworkImage.Source = snapshot.Artwork.AsImageSource();
        ArtworkPlaceholder.Visibility = snapshot.Artwork is null
            ? Visibility.Visible
            : Visibility.Collapsed;
        VerticalArtworkPlaceholder.Visibility = ArtworkPlaceholder.Visibility;

        PreviousButton.IsEnabled = snapshot.IsConnected && snapshot.CanSkipPrevious;
        PlayPauseButton.IsEnabled = snapshot.IsConnected && snapshot.CanPlayPause;
        NextButton.IsEnabled = snapshot.IsConnected && snapshot.CanSkipNext;
        VerticalPreviousButton.IsEnabled = PreviousButton.IsEnabled;
        VerticalPlayPauseButton.IsEnabled = PlayPauseButton.IsEnabled;
        VerticalNextButton.IsEnabled = NextButton.IsEnabled;
        PlayPauseGlyph.Text = snapshot.IsPlaying ? "\uE769" : "\uE768";
        VerticalPlayPauseGlyph.Text = PlayPauseGlyph.Text;
        ApplyLocalizedSnapshotText(snapshot);

        if (_metricSettings.VolumeControlEnabled &&
            (volumeSourceChanged ||
                VolumeControlPopup.IsOpen ||
                VolumeStatusPopup.IsOpen))
        {
            _ = RefreshCurrentMediaVolumeAsync(
                snapshot.SourceId,
                snapshot.SourceName);
        }

        ScheduleMarqueeUpdate();
        if (_windowSettings.HideWhenNoMedia)
        {
            PositionOverTaskbar(force: true);
        }
    }

    /// <summary>
    /// 刷新快照中的本地化文本；语言切换后由 App 调用重放当前状态。
    /// Title/Artist 等媒体数据保持快照原值，不做本地化。
    /// </summary>
    private void ApplyLocalizedSnapshotText(MediaSnapshot snapshot)
    {
        PlayPauseButton.ToolTip = snapshot.IsPlaying
            ? Loc.Get("Main.Control.Pause")
            : Loc.Get("Main.Control.Play");
        VerticalPlayPauseButton.ToolTip = PlayPauseButton.ToolTip;
        ConnectionMenuText.Text = snapshot.IsConnected
            ? Loc.Get("Main.SourceStatusFormat", snapshot.SourceName, snapshot.Title)
            : Loc.Get("Main.Placeholder.Title");
        ShowSourceMenuItem.Header = snapshot.IsConnected
            ? Loc.Get("Main.Menu.ShowSourceFormat", snapshot.SourceName)
            : Loc.Get("Main.Menu.ShowSource");
        ShowSourceMenuItem.IsEnabled = !string.IsNullOrWhiteSpace(snapshot.SourceId);
        _trayIconService?.UpdateTooltip(
            snapshot.IsConnected
                ? Loc.Get("Main.TrayTooltipFormat", snapshot.SourceName, snapshot.Title, snapshot.Artist)
                : Loc.Get("Main.TitleIdle"));
    }

    internal void RefreshLocalizedText()
    {
        if (_lastSnapshot is { } snapshot)
        {
            var replayed = _disconnectedTitleKey is null
                ? snapshot
                : snapshot with { Title = Loc.Get(_disconnectedTitleKey) };
            // Language changes only require text refresh. Replaying the full snapshot
            // also re-enters the hide/position path and can hide a floating HWND while
            // the media state is unchanged.
            ApplyLocalizedSnapshotText(replayed);
            if (!replayed.IsConnected)
            {
                var title = string.IsNullOrWhiteSpace(replayed.Title)
                    ? Loc.Get("Main.Placeholder.Title")
                    : replayed.Title;
                var artist = string.IsNullOrWhiteSpace(replayed.Artist)
                    ? Loc.Get("Main.Placeholder.Subtitle")
                    : replayed.Artist;
                TitleText.Text = title;
                ArtistText.Text = artist;
                VerticalTitleText.Text = FormatVerticalText(title);
                VerticalArtistText.Text = FormatVerticalText(artist);
                VerticalTitleText.ToolTip = title;
                VerticalArtistText.ToolTip = artist;
            }
        }

        ApplySessions(_mediaSessions, updateVisibility: false);
    }

    private void ApplySessions(
        IReadOnlyList<MediaSessionOption> sessions,
        bool updateVisibility = true)
    {
        _mediaSessions = sessions;
        var hasPlayingSession = sessions.Any(session => session.IsPlaying) ||
            (_selectedMediaIsPlaying && sessions.Count == 0);
        var hasChanged = _hasConnectedMedia != hasPlayingSession;
        _hasConnectedMedia = hasPlayingSession;
        if (hasChanged && updateVisibility)
        {
            if (_windowSettings.HideWhenNoMedia)
            {
                PositionOverTaskbar(force: true);
            }
        }
        MediaSourcesMenuItem.Items.Clear();
        if (sessions.Count == 0)
        {
            MediaSourcesMenuItem.Items.Add(new MenuItem
            {
                Header = Loc.Get("Main.Menu.NoSessions"),
                IsEnabled = false
            });
            MediaSourcesMenuItem.IsEnabled = false;
            return;
        }

        MediaSourcesMenuItem.IsEnabled = true;
        foreach (var session in sessions)
        {
            var item = new MenuItem
            {
                Header = session.IsPlaying
                    ? Loc.Get("Main.Menu.SessionPlayingFormat", session.DisplayName)
                    : session.DisplayName,
                IsCheckable = true,
                IsChecked = session.IsSelected,
                Tag = session.Key
            };
            item.Click += MediaSource_OnClick;
            MediaSourcesMenuItem.Items.Add(item);
        }
    }

    private async void MediaSource_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string key })
        {
            await RunMediaCommandAsync(() => _mediaSessionService.SelectSessionAsync(key));
        }
    }

    private void ShowDisconnectedState(string titleKey, string detail)
    {
        _disconnectedTitleKey = titleKey;
        ApplySnapshot(MediaSnapshot.Disconnected with
        {
            Title = Loc.Get(titleKey),
            Artist = detail
        });
    }

    private void PlayerRoot_OnMouseEnter(object sender, MouseEventArgs e)
    {
        _collapseTimer.Stop();
        ComponentSurface_OnLayoutPointerNearChanged(pointerNear: true);
        SetExpanded(expanded: true, animate: true);
    }

    private void PlayerRoot_OnMouseLeave(object sender, MouseEventArgs e)
    {
        ScheduleCollapse();
    }

    private void ScheduleCollapse()
    {
        _collapseTimer.Stop();
        _collapseTimer.Start();
    }

    private void OnCollapseTimerTick(object? sender, EventArgs e)
    {
        _collapseTimer.Stop();
        if (!_isMenuOpen &&
            !OutputDevicePopup.IsOpen &&
            !VolumeControlPopup.IsOpen &&
            !_isDragging &&
            !IsCursorInsideWindow())
        {
            ComponentSurface_OnLayoutPointerNearChanged(pointerNear: false);
            SetExpanded(expanded: false, animate: true);
        }
    }

    private void SetExpanded(bool expanded, bool animate)
    {
        if (!_windowSettings.AutoCollapse && !expanded)
        {
            expanded = true;
        }

        _isExpanded = expanded;
        _viewModel.ApplyPresentation(IsVisible, expanded);
        // 新布局的悬停容器始终跟随实际指针；旧全局自动收起只影响透明兼容树。
        // New hover containers always follow the real pointer; legacy global collapse affects only the transparent compatibility tree.
        animate &= !_metricSettings.LowGpuMode;
        if (_isVerticalLayout)
        {
            ApplyVerticalExpandedState(expanded, animate);
            return;
        }

        var showMediaInfo = _windowSettings.ShowMediaInfo;
        var showVisualizer = _metricSettings.AudioMonitorEnabled && !expanded;
        var showControls = expanded || (!showMediaInfo && !showVisualizer);
        ControlsHost.IsHitTestVisible = showControls;
        AudioVisualizerHost.Visibility = showVisualizer
            ? Visibility.Visible
            : Visibility.Collapsed;
        InfoHost.Visibility = showMediaInfo && !showVisualizer
            ? Visibility.Visible
            : Visibility.Collapsed;
        var infoWidth = expanded
            ? ExpandedInfoWidth
            : CollapsedInfoWidth;
        InfoHost.BeginAnimation(FrameworkElement.WidthProperty, null);
        InfoHost.MaxWidth = Math.Min(infoWidth, CentralHost.Width);
        InfoHost.Width = InfoHost.MaxWidth;
        TitleText.Width = double.NaN;
        TitleText.MaxWidth = double.PositiveInfinity;
        TitleText.TextTrimming = TextTrimming.None;
        UpdateAudioVisualizerPlacement();
        var controlsOpacity = showControls ? 1d : 0d;
        var controlsOffset = showControls ? 0d : 8d;
        var titleOffset = expanded ? -8d : 0d;
        var artistOffset = expanded ? 0d : 3d;
        var artistOpacity = expanded ? 1d : 0d;
        if (!animate)
        {
            ControlsHost.BeginAnimation(UIElement.OpacityProperty, null);
            ControlsTransform.BeginAnimation(TranslateTransform.XProperty, null);
            TitleTransform.BeginAnimation(TranslateTransform.YProperty, null);
            ArtistTransform.BeginAnimation(TranslateTransform.YProperty, null);
            ArtistText.BeginAnimation(UIElement.OpacityProperty, null);
            ControlsHost.Opacity = controlsOpacity;
            ControlsTransform.X = controlsOffset;
            TitleTransform.Y = titleOffset;
            ArtistTransform.Y = artistOffset;
            ArtistText.Opacity = artistOpacity;
            ScheduleMarqueeUpdate();
            return;
        }

        var duration = new Duration(TimeSpan.FromMilliseconds(220));
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        ControlsHost.BeginAnimation(
            UIElement.OpacityProperty,
            CreateAnimation(controlsOpacity, duration, easing));
        ControlsTransform.BeginAnimation(
            TranslateTransform.XProperty,
            CreateAnimation(controlsOffset, duration, easing));
        TitleTransform.BeginAnimation(
            TranslateTransform.YProperty,
            CreateAnimation(titleOffset, duration, easing));
        ArtistTransform.BeginAnimation(
            TranslateTransform.YProperty,
            CreateAnimation(artistOffset, duration, easing));
        ArtistText.BeginAnimation(
            UIElement.OpacityProperty,
            CreateAnimation(artistOpacity, duration, easing));
        ScheduleMarqueeUpdate();
    }

    /// <summary>
    /// 使用窗口矩形而不是 WPF 的 IsMouseOver 判断离开状态；布局变宽/变窄时旧视觉树可能短暂丢失命中，不能因此闪回离开槽位。
    /// Uses the native window rectangle instead of WPF IsMouseOver; resizing can briefly lose the old visual hit and must not flash back to the leave slot.
    /// </summary>
    private bool IsCursorInsideWindow()
    {
        if (_windowHandle == nint.Zero ||
            !NativeMethods.GetCursorPos(out var cursor) ||
            !NativeMethods.GetWindowRect(_windowHandle, out var bounds))
        {
            return PlayerRoot.IsMouseOver;
        }

        return cursor.X >= bounds.Left &&
            cursor.X < bounds.Right &&
            cursor.Y >= bounds.Top &&
            cursor.Y < bounds.Bottom;
    }

    private void ApplyVerticalExpandedState(bool expanded, bool animate)
    {
        AudioVisualizerHost.Visibility = Visibility.Collapsed;
        var showMediaInfo = _windowSettings.ShowMediaInfo;
        var showControls = expanded || !showMediaInfo;
        VerticalInfoHost.Visibility = showMediaInfo
            ? Visibility.Visible
            : Visibility.Collapsed;
        VerticalInfoHost.IsHitTestVisible = showMediaInfo && !expanded;
        VerticalControlsHost.IsHitTestVisible = showControls;
        var infoOpacity = showMediaInfo && !expanded ? 1d : 0d;
        var controlsOpacity = showControls ? 1d : 0d;
        if (!animate)
        {
            VerticalInfoHost.BeginAnimation(UIElement.OpacityProperty, null);
            VerticalControlsHost.BeginAnimation(UIElement.OpacityProperty, null);
            VerticalInfoHost.Opacity = infoOpacity;
            VerticalControlsHost.Opacity = controlsOpacity;
            ScheduleMarqueeUpdate();
            return;
        }

        var duration = new Duration(TimeSpan.FromMilliseconds(220));
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        VerticalInfoHost.BeginAnimation(
            UIElement.OpacityProperty,
            CreateAnimation(infoOpacity, duration, easing));
        VerticalControlsHost.BeginAnimation(
            UIElement.OpacityProperty,
            CreateAnimation(controlsOpacity, duration, easing));
        ScheduleMarqueeUpdate();
    }

    private void UpdateAudioVisualizerPlacement()
    {
        var centralWidth = CentralHost.Width;
        var centeredLeft =
            (centralWidth - AudioVisualizerWidth) / 2 +
            AudioVisualizerCenterBias;
        var rightmostLeft = centralWidth - AudioVisualizerWidth;
        AudioVisualizerTransform.X = Math.Clamp(
            centeredLeft,
            0,
            rightmostLeft);
    }

    private static DoubleAnimation CreateAnimation(
        double target,
        Duration duration,
        IEasingFunction easing)
    {
        return new DoubleAnimation
        {
            To = target,
            Duration = duration,
            EasingFunction = easing,
            FillBehavior = FillBehavior.HoldEnd
        };
    }

    private void ScheduleMarqueeUpdate()
    {
        if (_metricSettings.LowGpuMode)
        {
            _marqueeTimer.Stop();
            StopMarquees();
            return;
        }

        _marqueeTimer.Stop();
        _marqueeTimer.Start();
    }

    private void OnMarqueeTimerTick(object? sender, EventArgs e)
    {
        _marqueeTimer.Stop();
        UpdateMarquees();
    }

    private void UpdateMarquees()
    {
        if (_metricSettings.LowGpuMode || !IsWindowContentVisible())
        {
            StopMarquees();
            return;
        }

        if (!_windowSettings.ShowMediaInfo)
        {
            StopMarquees();
            return;
        }

        if (_isVerticalLayout)
        {
            StopHorizontalMarquees();
            if (_isExpanded)
            {
                StopVerticalMarquees();
                return;
            }

            UpdateVerticalMarquee(
                VerticalTitleMarquee,
                VerticalTitleViewport,
                VerticalTitleTransform);
            UpdateVerticalMarquee(
                VerticalArtistMarquee,
                VerticalArtistViewport,
                VerticalArtistTransform);
            return;
        }

        StopVerticalMarquees();
        UpdateMarquee(TitleText, TitleViewport, TitleTransform);
        UpdateMarquee(ArtistText, ArtistViewport, ArtistTransform);
    }

    private bool IsWindowContentVisible()
    {
        if (_windowHandle == nint.Zero ||
            Visibility != Visibility.Visible ||
            !_hasPresented ||
            Opacity <= 0.01 ||
            !NativeMethods.GetWindowRect(_windowHandle, out var windowRect))
        {
            return false;
        }

        var monitor = NativeMethods.MonitorFromWindow(_windowHandle, 2);
        var monitorInfo = NativeMethods.MonitorInfo.Create();
        return monitor != nint.Zero &&
            NativeMethods.GetMonitorInfo(monitor, ref monitorInfo) &&
            windowRect.Right > monitorInfo.Monitor.Left &&
            windowRect.Left < monitorInfo.Monitor.Right &&
            windowRect.Bottom > monitorInfo.Monitor.Top &&
            windowRect.Top < monitorInfo.Monitor.Bottom;
    }

    private void StopMarquees()
    {
        StopHorizontalMarquees();
        StopVerticalMarquees();
    }

    private void StopHorizontalMarquees()
    {
        StopHorizontalMarquee(TitleTransform);
        StopHorizontalMarquee(ArtistTransform);
    }

    private void StopVerticalMarquees()
    {
        StopVerticalMarquee(VerticalTitleTransform);
        StopVerticalMarquee(VerticalArtistTransform);
    }

    private static void StopHorizontalMarquee(TranslateTransform transform)
    {
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.X = 0;
    }

    private static void StopVerticalMarquee(TranslateTransform transform)
    {
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        transform.Y = 0;
    }

    private static void UpdateMarquee(
        System.Windows.Controls.TextBlock text,
        FrameworkElement viewport,
        TranslateTransform transform)
    {
        transform.BeginAnimation(TranslateTransform.XProperty, null);
        transform.X = 0;
        text.Width = double.NaN;
        text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var textWidth = Math.Ceiling(text.DesiredSize.Width + 1);
        text.Width = Math.Max(viewport.ActualWidth, textWidth);
        var overflow = textWidth - viewport.ActualWidth;
        if (overflow <= 2 || viewport.ActualWidth <= 0)
        {
            return;
        }

        var travelSeconds = Math.Max(3, overflow / 22d);
        var animation = new DoubleAnimation
        {
            From = 0,
            To = -(overflow + 8),
            BeginTime = TimeSpan.FromSeconds(1),
            Duration = TimeSpan.FromSeconds(travelSeconds),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        transform.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    private static void UpdateVerticalMarquee(
        FrameworkElement content,
        FrameworkElement viewport,
        TranslateTransform transform)
    {
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        transform.Y = 0;
        content.Height = double.NaN;
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var contentHeight = Math.Ceiling(content.DesiredSize.Height + 1);
        content.Height = Math.Max(viewport.ActualHeight, contentHeight);
        var overflow = contentHeight - viewport.ActualHeight;
        if (overflow <= 2 || viewport.ActualHeight <= 0)
        {
            return;
        }

        var travelSeconds = Math.Max(3, overflow / 22d);
        var animation = new DoubleAnimation
        {
            From = 0,
            To = -(overflow + 8),
            BeginTime = TimeSpan.FromSeconds(1),
            Duration = TimeSpan.FromSeconds(travelSeconds),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        transform.BeginAnimation(TranslateTransform.YProperty, animation);
    }

    private static string FormatVerticalText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length * 2);
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            if (element is "\r" or "\n")
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }
            builder.Append(element);
        }

        return builder.ToString();
    }

}
