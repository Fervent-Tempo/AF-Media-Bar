using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace AFMediaBar.Views.Windows;

/// <summary>任务栏外的完整媒体、音频与性能面板。 / Full media, audio, and performance panel outside the taskbar.</summary>
public partial class TaskbarFullPanelWindow : FluentWindow
{
    private static readonly TimeSpan DeviceWheelApplyDelay =
        TimeSpan.FromMilliseconds(AudioApplyPolicy.OutputDevicePreviewDelayMilliseconds);
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
    private bool _isSeeking;
    private bool _audioControlsVisible;
    private bool _performanceVisible;
    private bool _isClosing;
    private int _deviceApplyVersion;
    private int _volumeApplyVersion;
    private int _sectionAnimationVersion;
    private Rect? _anchor;
    private string? _targetMonitorDeviceId;

    public TaskbarFullPanelWindow(
        MediaSessionService mediaSessionService,
        AudioInteractionService audioInteractionService,
        SystemMetricsMonitorService metricsMonitor,
        WindowAppearanceService appearanceService,
        IDisplayMonitorService displayMonitorService)
    {
        InitializeComponent();
        ApplyMotionEffects();
        _mediaSessionService = mediaSessionService;
        _audioInteractionService = audioInteractionService;
        _metricsMonitor = metricsMonitor;
        _displayMonitorService = displayMonitorService;
        appearanceService.Attach(this);
        _mediaSessionService.SnapshotChanged += OnSnapshotChanged;
        SettingsManager.TaskbarExperienceSettingsChanged += OnTaskbarExperienceSettingsChanged;
        Translations.LanguageChanged += OnLanguageChanged;
        Closed += OnClosed;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += Timer_Tick;
        _timer.Start();
        ApplySnapshot(_mediaSessionService.CurrentSnapshot ?? MediaSnapshot.Disconnected);
        ApplyFullPanelSettings(animate: false);
    }

    public void ToggleNear(Rect anchor, string targetMonitorDeviceId)
    {
        if (_isClosing)
            return;

        if (IsVisible)
        {
            RequestClose();
            return;
        }

        _isClosing = false;
        _anchor = anchor;
        _targetMonitorDeviceId = targetMonitorDeviceId;
        ApplyMotionEffects();
        Show();
        UpdateLayout();
        PositionNear(anchor, targetMonitorDeviceId);
        BeginOpenAnimation(anchor);
        Activate();
    }

    /// <summary>幂等关闭完整层，避免失活事件重入窗口关闭。 / Closes the full panel idempotently without deactivation reentrancy.</summary>
    internal void RequestClose()
    {
        if (_isClosing)
            return;

        _isClosing = true;
        var motion = MotionPolicy.ResolveCurrent();
        if (!IsVisible || !motion.UseTransitions)
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

    private void BeginOpenAnimation(Rect anchor)
    {
        var motion = MotionPolicy.ResolveCurrent();
        PanelRoot.BeginAnimation(OpacityProperty, null);
        PanelScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        PanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        PanelRoot.Opacity = motion.UseTransitions ? 0 : 1;
        PanelScale.ScaleX = motion.UseTransitions ? 0.98 : 1;
        PanelScale.ScaleY = motion.UseTransitions ? 0.98 : 1;
        PanelRoot.RenderTransformOrigin = Top < anchor.Top
            ? new Point(0.5, 1)
            : new Point(0.5, 0);
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
            PanelRoot.SetResourceReference(Border.EffectProperty, "AfPanelShadowEffect");
        else
            PanelRoot.Effect = null;
    }

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
        Left = Math.Clamp(anchor.Left + (anchor.Width - ActualWidth) / 2, work.Left, Math.Max(work.Left, work.Right - ActualWidth));
        var above = anchor.Top - ActualHeight - 8;
        var below = anchor.Bottom + 8;
        var desiredTop = above >= work.Top ? above : below;
        Top = Math.Clamp(desiredTop, work.Top, Math.Max(work.Top, work.Bottom - ActualHeight));
    }

    private void OnSnapshotChanged(object? sender, MediaSnapshot snapshot) => Dispatcher.BeginInvoke(() => ApplySnapshot(snapshot));

    private void ApplySnapshot(MediaSnapshot snapshot)
    {
        _snapshot = snapshot;
        TitleText.Text = string.IsNullOrWhiteSpace(snapshot.Title) ? Translations.Get("Panel.FullPanel.NoMedia") : snapshot.Title;
        ArtistText.Text = string.IsNullOrWhiteSpace(snapshot.Artist) ? snapshot.SourceName : snapshot.Artist;
        SourceText.Text = snapshot.SourceName;
        ApplyLyricsSourceLine(snapshot);
        ArtworkImage.Source = snapshot.Artwork;
        ArtworkPlaceholder.Visibility = snapshot.Artwork is null ? Visibility.Visible : Visibility.Collapsed;
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
    private void OnLanguageChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() => ApplyLyricsSourceLine(_snapshot));

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

        var settings = SettingsManager.Current.TaskbarExperience.FullPanel.Normalize();
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
        DisposeMetricsSubscription();
        if (settings.PerformanceVisible)
        {
            var generation = _metricsSubscriptionGeneration;
            _metricsSubscription = _metricsMonitor.Subscribe(
                Enum.GetValues<MetricKind>(),
                TimeSpan.FromMilliseconds(500),
                metrics => ApplyMetricsSnapshot(generation, metrics));
        }

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
            _ = RefreshAudioAsync();

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            UpdateLayout();
            if (IsVisible && _anchor is Rect anchor && _targetMonitorDeviceId is { } targetMonitorDeviceId)
                PositionNear(anchor, targetMonitorDeviceId);
            if (animate)
                AnimateVisibleSections();
        }));
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
        try
        {
            _isLoadingDevices = true;
            var devices = await _audioInteractionService.GetOutputDevicesAsync();
            DeviceCombo.ItemsSource = devices;
            DeviceCombo.SelectedItem = devices.FirstOrDefault(device => device.IsDefault);
            _currentVolume = await Task.Run(_audioInteractionService.GetCurrentMediaVolume);
            VolumeSlider.IsEnabled = _currentVolume is not null;
            VolumeSlider.Value = _currentVolume?.VolumePercent ?? 0;
            VolumeText.Text = _currentVolume is null ? Translations.Get("Panel.Volume.Unavailable") : $"{_currentVolume.VolumePercent}%";
        }
        finally
        {
            _isLoadingDevices = false;
        }
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
        RamMetric.Text = $"{metrics.SystemMemoryPercent}%";
        CpuMetric.Text = metrics.SystemCpuPercent is int cpu ? $"{cpu}%" : "—";
        GpuMetric.Text = metrics.SystemGpuPercent is int gpu ? $"{gpu}%" : "—";
        ProcessMetric.Text = $"{metrics.ProcessMemoryMegabytes} MB";
    }

    private void DisposeMetricsSubscription()
    {
        _metricsSubscriptionGeneration++;
        _metricsSubscription?.Dispose();
        _metricsSubscription = null;
    }

    private void UpdateProgress()
    {
        var position = TaskbarExperiencePolicy.GetPosition(_snapshot, DateTimeOffset.UtcNow);
        if (!_isSeeking)
            ProgressSlider.Value = position;
        PositionText.Text = FormatTime(position);
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

    private void ProgressSlider_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _isSeeking = true;
    private async void ProgressSlider_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isSeeking = false;
        if (_snapshot.CanSeek)
            await _mediaSessionService.SeekAsync(ProgressSlider.Value);
    }

    private void ProgressSlider_LostMouseCapture(object sender, MouseEventArgs e) => _isSeeking = false;

    private async void DeviceCombo_DropDownOpened(object sender, EventArgs e) => await RefreshAudioAsync();
    private async void DeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoadingDevices && DeviceCombo.SelectedItem is AudioDeviceOption device)
        {
            var version = ++_deviceApplyVersion;
            await ApplyOutputDeviceAsync(device, version, TimeSpan.Zero);
        }
    }

    private void DeviceCombo_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var devices = DeviceCombo.Items.OfType<AudioDeviceOption>().ToList();
        var signedSteps = TrayWheelPolicy.GetDeviceSteps(e.Delta);
        if (devices.Count == 0 || signedSteps == 0)
            return;

        var current = DeviceCombo.SelectedItem is AudioDeviceOption selected
            ? Math.Max(0, devices.IndexOf(selected))
            : 0;
        var target = devices[WheelInput.MoveCircular(current, signedSteps, devices.Count)];
        _isLoadingDevices = true;
        try
        {
            DeviceCombo.SelectedItem = target;
        }
        finally
        {
            _isLoadingDevices = false;
        }

        var version = ++_deviceApplyVersion;
        _ = ApplyOutputDeviceAsync(target, version, DeviceWheelApplyDelay);
        e.Handled = true;
    }

    private async Task ApplyOutputDeviceAsync(AudioDeviceOption device, int version, TimeSpan delay)
    {
        try
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay);
            if (_isClosing || version != _deviceApplyVersion)
                return;
            await _audioInteractionService.SetOutputDeviceAsync(device);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[TaskbarFullPanelWindow] Output-device change failed: {exception}");
        }
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
        if (!DeviceCombo.IsDropDownOpen)
            RequestClose();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            RequestClose();
    }

    /// <summary>标记窗口已进入关闭流程，覆盖所有外部关闭入口。 / Marks the window as closing for every external close path.</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _isClosing = true;
        base.OnClosing(e);
        if (e.Cancel)
            _isClosing = false;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _deviceApplyVersion++;
        _volumeApplyVersion++;
        _timer.Stop();
        DisposeMetricsSubscription();
        _mediaSessionService.SnapshotChanged -= OnSnapshotChanged;
        SettingsManager.TaskbarExperienceSettingsChanged -= OnTaskbarExperienceSettingsChanged;
        Translations.LanguageChanged -= OnLanguageChanged;
        Closed -= OnClosed;
    }
}
