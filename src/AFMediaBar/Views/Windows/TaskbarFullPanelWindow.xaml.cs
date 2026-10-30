using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Services.Layout;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;
using AFMediaBar.ViewModels.Windows;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace AFMediaBar.Views.Windows;

/// <summary>任务栏外的完整媒体、音频与性能面板。 / Full media, audio, and performance panel outside the taskbar.</summary>
public partial class TaskbarFullPanelWindow : FluentWindow
{
    private readonly FullPanelPresentationViewModel _presentation;
    private readonly AnchoredWindowPresentation _windowPresentation;
    private readonly WindowOutsideClickDismissal _outsideClickDismissal;
    private AppSettings? _presentationSettings;
    private AppSettings PresentationSettings => _presentationSettings ?? SettingsManager.Current;
    public void ApplyPresentationSettings(AppSettings? settings)
    {
        _presentationSettings = settings;
        if (IsVisible) ApplyFullPanelSettings(animate: false);
    }

    private static readonly TimeSpan VolumeWheelApplyDelay =
        TimeSpan.FromMilliseconds(AudioApplyPolicy.ApplicationVolumeDelayMilliseconds);
    private readonly MediaSessionService _mediaSessionService;
    private readonly AudioInteractionService _audioInteractionService;
    private readonly SystemMetricsMonitorService _metricsMonitor;
    private IDisposable? _metricsSubscription;
    private int _metricsSubscriptionGeneration;
    private readonly IDisplayMonitorService _displayMonitorService;
    private readonly DispatcherTimer _timer;
    private MediaSnapshot _snapshot = MediaSnapshot.Disconnected;
    private ApplicationVolumeSnapshot? _currentVolume;
    private bool _isLoadingDevices;
    private int _audioRefreshGeneration;
    private bool _isSeeking;
    private MediaSnapshot? _seekSnapshot;
    private double? _pendingSeekPosition;
    private long _pendingSeekStarted;
    private int _seekRequestVersion;
    private bool _audioControlsVisible;
    private bool _performanceVisible;
    private bool _isClosing;
    private int _volumeApplyVersion;
    private int _sectionAnimationVersion;
    private Rect? _anchor;
    private Point? _entryCenter;
    private FullPanelPlacementSide? _placementSide;
    private string? _targetMonitorDeviceId;

    public TaskbarFullPanelWindow(
        MediaSessionService mediaSessionService,
        AudioInteractionService audioInteractionService,
        SystemMetricsMonitorService metricsMonitor,
        WindowAppearanceService appearanceService,
        IDisplayMonitorService displayMonitorService,
        FullPanelPresentationViewModel presentation,
        NativeMouseInputMonitor mouseInput)
    {
        InitializeComponent();
        _windowPresentation = new(this);
        _outsideClickDismissal = new(this, mouseInput, RequestClose);
        // MoveToPoint 会先消费轨道点击；仍需暂停进度同步并保存这次定位意图。
        ProgressSlider.AddHandler(PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(ProgressSlider_MouseLeftButtonDown), handledEventsToo: true);
        ProgressSlider.AddHandler(PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(ProgressSlider_MouseLeftButtonUp), handledEventsToo: true);
        _presentation = presentation;
        InitializeResizeGrips();
        PanelRoot.SizeChanged += OnPanelContentSizeChanged;
        DpiChanged += (_, _) => { if (_isResizing) CancelActiveResize(); QueuePanelPosition(); };
        ApplyMotionEffects();
        _mediaSessionService = mediaSessionService;
        _audioInteractionService = audioInteractionService;
        _audioInteractionService.DeviceSwitches.Updated += OnAudioDeviceSwitchUpdated;
        _audioInteractionService.DeviceSwitches.Invalidated += OnAudioDeviceInvalidated;
        _metricsMonitor = metricsMonitor;
        _displayMonitorService = displayMonitorService;
        appearanceService.Attach(this);
        _mediaSessionService.SnapshotChanged += OnSnapshotChanged;
        SettingsManager.TaskbarExperienceSettingsChanged += OnTaskbarExperienceSettingsChanged;
        Translations.LanguageChanged += OnLanguageChanged;
        Closed += OnClosed;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += Timer_Tick;
        ApplySnapshot(_mediaSessionService.CurrentSnapshot ?? MediaSnapshot.Disconnected);
    }

    public void ToggleNear(Rect anchor, string targetMonitorDeviceId, bool verticalAnchor = false, FullPanelPlacementSide? side = null, Point? entryCenter = null)
    {
        if (_isClosing)
            return;

        if (IsVisible)
        {
            RequestClose();
            return;
        }

        ShowNear(anchor, targetMonitorDeviceId, verticalAnchor, side, entryCenter);
    }

    /// <summary>外侧悬停入口只请求打开，重复请求不关闭已有面板。</summary>
    public void ShowNear(Rect anchor, string targetMonitorDeviceId, bool verticalAnchor = false, FullPanelPlacementSide? side = null, Point? entryCenter = null)
    {
        if (_isClosing || IsVisible) return;

        _isClosing = false;
        _anchor = anchor;
        _entryCenter = entryCenter;
        _resizePlacement = null;
        _targetMonitorDeviceId = targetMonitorDeviceId;
        _verticalAnchor = verticalAnchor;
        _placementSide = side;
        _ = PresentNearAsync(anchor, targetMonitorDeviceId);
    }

    private async Task PresentNearAsync(Rect anchor, string targetMonitorDeviceId)
    {
        try
        {
            ApplyMotionEffects();
            ApplyFullPanelSettings(animate: false);
            PanelRoot.Opacity = MotionPolicy.ResolveCurrent().UseTransitions ? 0 : 1;
            var monitor = _displayMonitorService.ResolveFixedMonitor(targetMonitorDeviceId);
            var physicalAnchor = new Point((anchor.Left + anchor.Width / 2) * (monitor?.DpiX ?? 96) / 96d,
                (anchor.Top + anchor.Height / 2) * (monitor?.DpiY ?? 96) / 96d);
            if (!await _windowPresentation.ShowAsync(physicalAnchor, () => PositionNear(anchor, targetMonitorDeviceId)) || _isClosing) return;
            _outsideClickDismissal.Start();
            UpdateMetricsSubscription();
            _timer.Start();
            BeginOpenAnimation();
            if (ShowActivated) Activate();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[FullPanel] Presentation failed: {exception}");
            if (!_isClosing) RequestClose();
        }
    }

    /// <summary>幂等关闭完整层，避免失活事件重入窗口关闭。 / Closes the full panel idempotently without deactivation reentrancy.</summary>
    internal void RequestClose()
    {
        if (_isClosing)
            return;

        var wasPreparing = _windowPresentation.IsPreparing;
        _windowPresentation.Cancel();
        _outsideClickDismissal.Stop();
        _isClosing = true;
        CancelProgressSeek();
        _audioInteractionService.CancelDeviceOwner(this);
        var motion = MotionPolicy.ResolveCurrent();
        if (!IsVisible || wasPreparing || !motion.UseTransitions)
        {
            Close();
            return;
        }

        PanelRoot.BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            To = 0,
            Duration = motion.ExitDuration,
            EasingFunction = new PowerEase { Power = 3, EasingMode = EasingMode.EaseInOut }
        });
        PanelScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation
        {
            To = 0.985,
            Duration = motion.ExitDuration,
            EasingFunction = new PowerEase { Power = 3, EasingMode = EasingMode.EaseInOut }
        });
        var closeAnimation = new DoubleAnimation
        {
            To = 0.985,
            Duration = motion.ExitDuration,
            EasingFunction = new PowerEase { Power = 3, EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.Stop
        };
        closeAnimation.Completed += (_, _) => Close();
        PanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, closeAnimation, HandoffBehavior.SnapshotAndReplace);
    }

    private void BeginOpenAnimation()
    {
        var motion = MotionPolicy.ResolveCurrent();
        PanelRoot.BeginAnimation(OpacityProperty, null);
        PanelScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        PanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        PanelRoot.Opacity = motion.UseTransitions ? 0 : 1;
        PanelScale.ScaleX = motion.UseTransitions ? 0.98 : 1;
        PanelScale.ScaleY = motion.UseTransitions ? 0.98 : 1;
        if (!motion.UseTransitions)
            return;

        var ease = new PowerEase { Power = 3, EasingMode = EasingMode.EaseOut };
        PanelRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, motion.PanelDuration) { EasingFunction = ease });
        PanelScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, motion.PanelDuration) { EasingFunction = ease });
        PanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, motion.PanelDuration) { EasingFunction = ease });
    }

    private void ApplyMotionEffects()
    {
        if (MotionPolicy.ResolveCurrent().UseDecorativeEffects)
            PanelChrome.SetResourceReference(Border.EffectProperty, "AfPanelShadowEffect");
        else
            PanelChrome.Effect = null;
    }

    private bool _verticalAnchor;

    private void PositionNear(Rect anchor, string targetMonitorDeviceId)
    {
        var monitor = _displayMonitorService.ResolveFixedMonitor(targetMonitorDeviceId);
        var scaleX = Math.Max(1d / 96d, (monitor?.DpiX ?? 96) / 96d);
        var scaleY = Math.Max(1d / 96d, (monitor?.DpiY ?? 96) / 96d);
        var work = monitor is null || monitor.WorkArea.IsEmpty
            ? SystemParameters.WorkArea
            : new Rect(
                monitor.WorkArea.Left / scaleX,
                monitor.WorkArea.Top / scaleY,
                monitor.WorkArea.Width / scaleX,
                monitor.WorkArea.Height / scaleY);
        var side = _placementSide ?? FullPanelPlacementPolicy.ResolveSide(anchor, work, _verticalAnchor);
        var limit = FullPanelPlacementPolicy.GetPanelLimit(anchor, work, side);
        _panelWorkArea = new Rect(side == FullPanelPlacementSide.Right ? work.Right - limit.Width : work.Left,
            side == FullPanelPlacementSide.Below ? work.Bottom - limit.Height : work.Top, limit.Width, limit.Height);
        MaxWidth = limit.Width;
        MaxHeight = limit.Height;
        _resolvedSide = side;
        ApplyScaleGeometry(limit);
        UpdateResizeGrips();
        UpdateLayout();
        var size = new Size(ActualWidth, ActualHeight);
        var bounds = _resizePlacement is { } placement
            ? FullPanelScalePolicy.PlaceResizeBounds(placement.Bounds, size, placement.Edge, placement.Side)
            : FullPanelPlacementPolicy.PlacePanel(anchor, size, work, side, _entryCenter);
        bounds.X = Math.Clamp(bounds.X, _panelWorkArea.Left, Math.Max(_panelWorkArea.Left, _panelWorkArea.Right - bounds.Width));
        bounds.Y = Math.Clamp(bounds.Y, _panelWorkArea.Top, Math.Max(_panelWorkArea.Top, _panelWorkArea.Bottom - bounds.Height));
        Left = bounds.Left;
        Top = bounds.Top;
        PanelRoot.RenderTransformOrigin = side switch
        {
            FullPanelPlacementSide.Above => new Point(0.5, 1),
            FullPanelPlacementSide.Below => new Point(0.5, 0),
            FullPanelPlacementSide.Left => new Point(1, 0.5),
            _ => new Point(0, 0.5)
        };
    }

    private void OnSnapshotChanged(object? sender, MediaSnapshot snapshot) => Dispatcher.BeginInvoke(() => ApplySnapshot(snapshot));

    private void ApplySnapshot(MediaSnapshot snapshot)
    {
        if (_isClosing) return;
        if (_seekSnapshot is not null && !IsSameSeekTrack(_seekSnapshot, snapshot))
            CancelProgressSeek();
        _snapshot = snapshot;
        TitleText.Text = string.IsNullOrWhiteSpace(snapshot.Title) ? Translations.Get("Panel.FullPanel.NoMedia") : snapshot.Title;
        ArtistText.Text = snapshot.Artist;
        ArtistText.Visibility = string.IsNullOrWhiteSpace(snapshot.Artist) ||
            string.Equals(snapshot.Artist.Trim(), snapshot.SourceName.Trim(), StringComparison.OrdinalIgnoreCase)
            ? Visibility.Collapsed
            : Visibility.Visible;
        SourceText.Text = snapshot.SourceName;
        ApplyLyricsSourceLine(snapshot);
        ArtworkImage.Source = snapshot.Artwork;
        ArtworkPlaceholder.Visibility = snapshot.Artwork is null ? Visibility.Visible : Visibility.Collapsed;
        // 与媒体栏、通知共用比例上限，给文字保留空间；Uniform 在极端比例或未知尺寸时仍显示完整封面。
        var artwork = snapshot.Artwork as BitmapSource;
        var box = ArtworkBoxPolicy.Resolve(ArtworkBorder.Height, artwork?.PixelWidth ?? 0, artwork?.PixelHeight ?? 0);
        ArtworkBorder.Width = box.Width;
        ArtworkColumn.Width = new GridLength(box.Width);
        SetButtonAvailability(PreviousButton, snapshot.CanSkipPrevious);
        SetButtonAvailability(NextButton, snapshot.CanSkipNext);
        SetButtonAvailability(PlayButton, snapshot.CanPlayPause);
        SetButtonAvailability(RepeatButton, snapshot.CanChangeRepeat);
        RepeatButton.ToolTip = snapshot.RepeatMode switch
        {
            MediaRepeatMode.One => Translations.Get("Panel.Repeat.One"),
            MediaRepeatMode.All => Translations.Get("Panel.Repeat.All"),
            MediaRepeatMode.Off => Translations.Get("Panel.Repeat.Off"),
            _ => Translations.Get("Panel.Repeat.Unavailable")
        };
        PlayIcon.Symbol = snapshot.IsPlaying ? SymbolRegular.Pause24 : SymbolRegular.Play24;
        ProgressSlider.IsEnabled = snapshot.CanSeek;
        ProgressSlider.Maximum = Math.Max(1, snapshot.Duration);
        DurationText.Text = FormatTime(snapshot.Duration);
        UpdateProgress();
    }

    private void OnTaskbarExperienceSettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() => ApplyFullPanelSettings(animate: true));

    /// <summary>
    /// 语言变化时重算由代码拼出来的那一行（歌词来源）；绑定文案由 WPF 自己重读，只有这条是拼出来的。
    /// Recomputes the one line built in code (the lyric source) after a language change; bound text re-reads itself, and this is
    /// the only composed one.
    /// </summary>
    private void OnLanguageChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (_isClosing) return;
        ApplyLyricsSourceLine(_snapshot);
        ApplyChartStyles(PresentationSettings.TaskbarExperience.FullPanel.PerformanceVisuals);
        QueuePanelPosition();
    });

    /// <summary>
    /// 显示当前曲目的歌词来源。
    /// Shows the lyric source of the current track.
    ///
    /// 没有歌词时整行收起：这一行的存在意义是回答"这句歌词是哪来的"，没有歌词时它没有可回答的问题。
    /// The line collapses without lyrics: its whole point is to answer "where did this lyric come from", and without lyrics there
    /// is no such question.
    /// </summary>
    private void ApplyLyricsSourceLine(MediaSnapshot snapshot)
    {
        if (snapshot.Lyrics is not { } lyrics || string.IsNullOrWhiteSpace(lyrics.Source))
        {
            LyricsSourceText.Text = string.Empty;
            LyricsSourceText.Visibility = Visibility.Collapsed;
            return;
        }

        LyricsSourceText.Text = Translations.Format(
            "Panel.FullPanel.LyricsSource",
            LyricsSourceCatalog.GetDisplayName(lyrics.Source));
        LyricsSourceText.Visibility = Visibility.Visible;
    }

    private void ApplyFullPanelSettings(bool animate)
    {
        if (_isClosing)
            return;

        var settings = PresentationSettings.TaskbarExperience.FullPanel.Normalize();
        if (!_isResizing) _requestedScale = settings.ScalePercent / 100d;
        ApplyChartStyles(settings.PerformanceVisuals);
        var animationVersion = ++_sectionAnimationVersion;
        var audioBecameVisible = !_audioControlsVisible && settings.AudioControlsVisible;
        var mediaInfoWasVisible = MediaInfoSection.Visibility == Visibility.Visible;
        var mediaControlsWereVisible = MediaControlsSection.Visibility == Visibility.Visible;
        var audioWasVisible = AudioControlsSection.Visibility == Visibility.Visible;
        var performanceWasVisible = PerformanceSection.Visibility == Visibility.Visible;
        _audioControlsVisible = settings.AudioControlsVisible;
        _performanceVisible = settings.PerformanceVisible;

        var motion = MotionPolicy.ResolveCurrent();
        if (animate && motion.UseTransitions)
        {
            var exitingSections = new[]
            {
                (Section: (UIElement)MediaInfoSection, ShouldBeVisible: settings.MediaInfoVisible),
                (Section: (UIElement)MediaControlsSection, ShouldBeVisible: settings.MediaControlsVisible),
                (Section: (UIElement)AudioControlsSection, ShouldBeVisible: settings.AudioControlsVisible),
                (Section: (UIElement)PerformanceSection, ShouldBeVisible: settings.PerformanceVisible)
            }
            .Where(state => !state.ShouldBeVisible && state.Section.Visibility == Visibility.Visible)
            .Select(state => state.Section)
            .ToArray();

            if (exitingSections.Length > 0)
            {
                var ease = new PowerEase { Power = 3, EasingMode = EasingMode.EaseInOut };
                void CompleteExitAnimations()
                {
                    if (_isClosing || animationVersion != _sectionAnimationVersion)
                        return;

                    foreach (var section in exitingSections)
                    {
                        section.BeginAnimation(OpacityProperty, null);
                        section.Visibility = Visibility.Collapsed;
                        section.Opacity = 1;
                    }

                    ApplyFullPanelSettings(animate: true);
                }

                for (var index = 0; index < exitingSections.Length; index++)
                {
                    var animation = new DoubleAnimation(0, motion.ExitDuration)
                    {
                        EasingFunction = ease,
                        FillBehavior = FillBehavior.Stop
                    };
                    if (index == exitingSections.Length - 1)
                        animation.Completed += (_, _) => CompleteExitAnimations();

                    exitingSections[index].BeginAnimation(
                        OpacityProperty,
                        animation,
                        HandoffBehavior.SnapshotAndReplace);
                }
                return;
            }
        }

        MediaInfoSection.Visibility = settings.MediaInfoVisible ? Visibility.Visible : Visibility.Collapsed;
        MediaControlsSection.Visibility = settings.MediaControlsVisible ? Visibility.Visible : Visibility.Collapsed;
        AudioControlsSection.Visibility = settings.AudioControlsVisible ? Visibility.Visible : Visibility.Collapsed;
        PerformanceSection.Visibility = settings.PerformanceVisible ? Visibility.Visible : Visibility.Collapsed;
        UpdateMetricsSubscription();

        if (animate)
        {
            SetSectionEntryState(MediaInfoSection, settings.MediaInfoVisible &&
                (!mediaInfoWasVisible || MediaInfoSection.Opacity < 0.999));
            SetSectionEntryState(MediaControlsSection, settings.MediaControlsVisible &&
                (!mediaControlsWereVisible || MediaControlsSection.Opacity < 0.999));
            SetSectionEntryState(AudioControlsSection, settings.AudioControlsVisible &&
                (!audioWasVisible || AudioControlsSection.Opacity < 0.999));
            SetSectionEntryState(PerformanceSection, settings.PerformanceVisible &&
                (!performanceWasVisible || PerformanceSection.Opacity < 0.999));
        }

        if (audioBecameVisible)
            _ = RefreshAudioSafelyAsync();

        QueuePanelPosition();
        if (animate) AnimateVisibleSections();
    }

    private void UpdateMetricsSubscription()
    {
        if (!_performanceVisible || !IsVisible || _windowPresentation.IsPreparing) DisposeMetricsSubscription();
        else if (_metricsSubscription is null)
        {
            var generation = _metricsSubscriptionGeneration;
            _metricsSubscription = _metricsMonitor.Subscribe(Enum.GetValues<MetricKind>(), TimeSpan.FromMilliseconds(500),
                metrics => ApplyMetricsSnapshot(generation, metrics));
        }
    }

    private void SetSectionEntryState(UIElement section, bool entering)
    {
        section.BeginAnimation(OpacityProperty, null);
        section.Opacity = entering && MotionPolicy.ResolveCurrent().UseTransitions ? 0 : 1;
    }

    private void AnimateVisibleSections()
    {
        var motion = MotionPolicy.ResolveCurrent();
        if (!motion.UseTransitions)
            return;

        var ease = new PowerEase { Power = 3, EasingMode = EasingMode.EaseOut };
        foreach (var section in new UIElement[]
                 {
                     MediaInfoSection,
                     MediaControlsSection,
                     AudioControlsSection,
                     PerformanceSection
                 })
        {
            if (section.Visibility != Visibility.Visible || section.Opacity >= 1)
                continue;

            section.BeginAnimation(OpacityProperty, new DoubleAnimation(1, motion.FastDuration)
            {
                EasingFunction = ease
            });
        }
    }


    private async Task RefreshAudioAsync()
    {
        var generation = ++_audioRefreshGeneration;
        var volumeVersion = _volumeApplyVersion;
        var devices = await _audioInteractionService.GetOutputDevicesAsync();
        var inputs = await _audioInteractionService.GetInputDevicesAsync();
        var volume = await Task.Run(_audioInteractionService.GetCurrentMediaVolume);
        if (_isClosing || generation != _audioRefreshGeneration) return;
        _isLoadingDevices = true;
        try
        {
            DeviceCombo.ItemsSource = devices;
            InputDeviceCombo.ItemsSource = inputs;
            var outputPreview = _audioInteractionService.DeviceSwitches.GetPreview(new(AudioDeviceDirection.Output));
            var inputPreview = _audioInteractionService.DeviceSwitches.GetPreview(new(AudioDeviceDirection.Input));
            DeviceCombo.SelectedItem = outputPreview is null ? devices.FirstOrDefault(device => device.IsDefault) :
                devices.FirstOrDefault(device => device.PolicyId == outputPreview.PolicyId);
            InputDeviceCombo.SelectedItem = inputPreview is null ? inputs.FirstOrDefault(device => device.IsDefault) :
                inputs.FirstOrDefault(device => device.PolicyId == inputPreview.PolicyId);
            AudioDeviceStatusText.Text = DeviceCombo.SelectedItem is null || InputDeviceCombo.SelectedItem is null ?
                Translations.Get("Audio.Route.HiddenOrDisconnected") : string.Empty;
            if (volumeVersion == _volumeApplyVersion)
            {
                _currentVolume = volume;
                VolumeSlider.IsEnabled = volume is not null;
                VolumeSlider.Value = volume?.VolumePercent ?? 0;
                VolumeText.Text = volume is null ? Translations.Get("Panel.Volume.Unavailable") : $"{volume.VolumePercent}%";
            }
        }
        finally { _isLoadingDevices = false; }
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (MediaControlsSection.Visibility == Visibility.Visible)
            UpdateProgress();
    }

    private void ApplyMetricsSnapshot(int generation, SystemMetricsSnapshot metrics)
    {
        if (generation != _metricsSubscriptionGeneration || _isClosing || !_performanceVisible)
            return;
        foreach (var presenter in PanelPresenters()) presenter.ApplySnapshot(metrics);
    }

    private void DisposeMetricsSubscription()
    {
        _metricsSubscriptionGeneration++;
        _metricsSubscription?.Dispose();
        _metricsSubscription = null;
        foreach (var presenter in PanelPresenters()) presenter.Reset();
    }

    private void UpdateProgress()
    {
        var position = TaskbarExperiencePolicy.GetPosition(_snapshot, DateTimeOffset.UtcNow);
        if (!_isSeeking && _pendingSeekPosition is { } target &&
            (Math.Abs(_snapshot.Position - target) <= 1 ||
             Stopwatch.GetElapsedTime(_pendingSeekStarted) >= TimeSpan.FromSeconds(2)))
        {
            _pendingSeekPosition = null;
            _seekSnapshot = null;
        }
        if (!_isSeeking)
            ProgressSlider.Value = _pendingSeekPosition ?? position;
        PositionText.Text = FormatTime(ProgressSlider.Value);
    }

    private static string FormatTime(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
            seconds = 0;
        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }

    private static void SetButtonAvailability(System.Windows.Controls.Button button, bool enabled)
    {
        button.IsEnabled = enabled;
        button.Opacity = enabled ? 1 : 0.32;
    }

    private async void PreviousButton_Click(object sender, RoutedEventArgs e) => await _mediaSessionService.SkipPreviousAsync();
    private async void PlayButton_Click(object sender, RoutedEventArgs e) => await _mediaSessionService.TogglePlayPauseAsync();
    private async void NextButton_Click(object sender, RoutedEventArgs e) => await _mediaSessionService.SkipNextAsync();
    private async void RepeatButton_Click(object sender, RoutedEventArgs e) => await _mediaSessionService.CycleRepeatModeAsync();

    private async void ArtworkBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            await _mediaSessionService.TogglePlayPauseAsync();
            e.Handled = true;
        }
    }

    private void ProgressSlider_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isClosing || !_snapshot.CanSeek || e.ChangedButton != MouseButton.Left) return;
        _seekRequestVersion++;
        _pendingSeekPosition = null;
        _seekSnapshot = _snapshot;
        _isSeeking = true;
        // 点击轨道时 Thumb 不会捕获鼠标，须保证移出轨道后松手也能结束交互。
        if (e.Handled && !ProgressSlider.IsMouseCaptureWithin && !ProgressSlider.CaptureMouse())
            CancelProgressSeek();
    }

    private async void ProgressSlider_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isSeeking || e.ChangedButton != MouseButton.Left) return;
        var target = ProgressSlider.Value;
        var snapshot = _seekSnapshot;
        _isSeeking = false;
        if (Mouse.Captured == ProgressSlider) ProgressSlider.ReleaseMouseCapture();
        if (snapshot is null || !IsSameSeekTrack(snapshot, _snapshot))
        {
            CancelProgressSeek();
            return;
        }

        var version = ++_seekRequestVersion;
        _pendingSeekPosition = target;
        _pendingSeekStarted = Stopwatch.GetTimestamp();
        try
        {
            await _mediaSessionService.SeekAsync(target);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[FullPanel] Seek failed: {exception}");
            if (version == _seekRequestVersion) CancelProgressSeek();
        }
        finally
        {
            if (!_isClosing && version == _seekRequestVersion) UpdateProgress();
        }
    }

    private void ProgressSlider_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isSeeking && !ProgressSlider.IsMouseCaptureWithin) CancelProgressSeek();
    }

    private void CancelProgressSeek()
    {
        _seekRequestVersion++;
        _isSeeking = false;
        _pendingSeekPosition = null;
        _seekSnapshot = null;
        if (ProgressSlider.IsMouseCaptureWithin) Mouse.Capture(null);
    }

    private static bool IsSameSeekTrack(MediaSnapshot expected, MediaSnapshot current) =>
        current.IsConnected && current.CanSeek &&
        string.Equals(expected.SourceId, current.SourceId, StringComparison.Ordinal) &&
        string.Equals(expected.Title, current.Title, StringComparison.Ordinal) &&
        string.Equals(expected.Artist, current.Artist, StringComparison.Ordinal);


    private async void DeviceCombo_DropDownOpened(object sender, EventArgs e)
    {
        RegisterDevicePopup(DeviceCombo);
        await RefreshAudioAsync();
    }
    private async void InputDeviceCombo_DropDownOpened(object sender, EventArgs e)
    {
        RegisterDevicePopup(InputDeviceCombo);
        await RefreshAudioAsync();
    }
    private void RegisterDevicePopup(ComboBox combo)
    {
        if (combo.Template?.FindName("PART_Popup", combo) is Popup popup) _outsideClickDismissal.RegisterPopup(popup);
    }
    private async void DeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    { if (!_isLoadingDevices && DeviceCombo.SelectedItem is AudioDeviceOption device) await SelectDeviceAsync(device); }
    private async void InputDeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    { if (!_isLoadingDevices && InputDeviceCombo.SelectedItem is AudioDeviceOption device) await SelectDeviceAsync(device); }
    private async Task SelectDeviceAsync(AudioDeviceOption device)
    {
        try
        {
            var result = await _audioInteractionService.DeviceSwitches.SelectAsync(new(device.Direction), device, this);
            if (_isClosing) return;
            await RefreshAudioSafelyAsync();
            if (!_isClosing) AudioDeviceStatusText.Text = AudioDevicePresentation.GetResultText(result, false);
        }
        catch (Exception exception) { Debug.WriteLine(exception); if (!_isClosing) AudioDeviceStatusText.Text = Translations.Get("Audio.Route.Failed"); }
    }
    private async void DeviceCombo_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    { e.Handled = true; await PreviewDeviceAsync(AudioDeviceDirection.Output, e.Delta); }
    private async void InputDeviceCombo_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    { e.Handled = true; await PreviewDeviceAsync(AudioDeviceDirection.Input, e.Delta); }
    private async Task PreviewDeviceAsync(AudioDeviceDirection direction, int delta)
    {
        try { await _audioInteractionService.DeviceSwitches.PreviewAsync(new(direction), TrayWheelPolicy.GetDeviceSteps(delta), this); }
        catch (Exception exception) { Debug.WriteLine(exception); }
    }
    private void OnAudioDeviceSwitchUpdated(AudioDeviceSwitchUpdate update) => Dispatcher.BeginInvoke(() =>
    {
        if (_isClosing || update.Target.Application is not null) return;
        if (update.Result is null)
        {
            _isLoadingDevices = true;
            var combo = update.Target.Direction == AudioDeviceDirection.Output ? DeviceCombo : InputDeviceCombo;
            combo.SelectedItem = combo.Items.OfType<AudioDeviceOption>().FirstOrDefault(device => device.PolicyId == update.Candidate?.PolicyId);
            _isLoadingDevices = false;
        }
        else AudioDeviceStatusText.Text = AudioDevicePresentation.GetResultText(update.Result, false);
    });
    private void OnAudioDeviceInvalidated(object? sender, EventArgs args) => Dispatcher.BeginInvoke(() =>
    { if (!_isClosing && IsVisible) _ = RefreshAudioSafelyAsync(); });
    private async Task RefreshAudioSafelyAsync()
    {
        try { await RefreshAudioAsync(); }
        catch (Exception exception) { Debug.WriteLine(exception); }
    }

    private async void VolumeSlider_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_currentVolume is null)
            return;
        var value = (int)Math.Round(VolumeSlider.Value);
        VolumeText.Text = $"{value}%";
        await QueueVolumeApplyAsync(value, TimeSpan.Zero);
    }

    private void VolumeSlider_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_currentVolume is null)
            return;

        var steps = TrayWheelPolicy.GetVolumeSteps(e.Delta);
        if (steps == 0)
            return;
        var value = Math.Clamp((int)Math.Round(VolumeSlider.Value) + steps * 2, 0, 100);
        VolumeSlider.Value = value;
        VolumeText.Text = $"{value}%";
        _ = QueueVolumeApplyAsync(value, VolumeWheelApplyDelay);
        e.Handled = true;
    }

    private async Task QueueVolumeApplyAsync(int value, TimeSpan delay)
    {
        if (_currentVolume is not { } volume)
            return;

        var version = ++_volumeApplyVersion;
        try
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay);
            if (_isClosing || version != _volumeApplyVersion)
                return;
            await Task.Run(() => _audioInteractionService.SetApplicationVolume(volume.ProcessName, value));
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[TaskbarFullPanelWindow] Application-volume change failed: {exception}");
        }
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!_isClosing && !_windowPresentation.IsPreparing && IsVisible && !IsActive && !DeviceCombo.IsDropDownOpen && !InputDeviceCombo.IsDropDownOpen)
                RequestClose();
        }, DispatcherPriority.ContextIdle);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (_isResizing) CancelActiveResize();
        else RequestClose();
        e.Handled = true;
    }

    /// <summary>标记窗口已进入关闭流程，覆盖所有外部关闭入口。 / Marks the window as closing for every external close path.</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _isClosing = true;
        CancelProgressSeek();
        _audioInteractionService.CancelDeviceOwner(this);
        base.OnClosing(e);
        if (e.Cancel)
            _isClosing = false;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _isClosing = true;
        _audioRefreshGeneration++;
        _windowPresentation.Dispose();
        _outsideClickDismissal.Dispose();
        _audioInteractionService.DeviceSwitches.Updated -= OnAudioDeviceSwitchUpdated;
        _audioInteractionService.DeviceSwitches.Invalidated -= OnAudioDeviceInvalidated;
        _audioInteractionService.CancelDeviceOwner(this);
        _volumeApplyVersion++;
        _timer.Stop();
        PanelRoot.SizeChanged -= OnPanelContentSizeChanged;
        CancelPanelResize();
        DisposeMetricsSubscription();
        _mediaSessionService.SnapshotChanged -= OnSnapshotChanged;
        SettingsManager.TaskbarExperienceSettingsChanged -= OnTaskbarExperienceSettingsChanged;
        Translations.LanguageChanged -= OnLanguageChanged;
        Closed -= OnClosed;
    }
}
