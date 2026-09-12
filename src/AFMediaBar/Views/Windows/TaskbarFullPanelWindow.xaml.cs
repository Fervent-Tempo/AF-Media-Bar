using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace AFMediaBar.Views.Windows;

/// <summary>任务栏外的完整媒体、音频与性能面板。 / Full media, audio, and performance panel outside the taskbar.</summary>
public partial class TaskbarFullPanelWindow : FluentWindow
{
    private readonly MediaSessionService _mediaSessionService;
    private readonly AudioInteractionService _audioInteractionService;
    private readonly SystemMetricsService _metricsService;
    private readonly GlobalInteractionRouter _interactionRouter;
    private readonly DispatcherTimer _timer;
    private MediaSnapshot _snapshot = MediaSnapshot.Disconnected;
    private ApplicationVolumeSnapshot? _currentVolume;
    private bool _isLoadingDevices;
    private bool _isSeeking;
    private DateTime _suppressArtworkClickUntilUtc;

    public TaskbarFullPanelWindow(
        MediaSessionService mediaSessionService,
        AudioInteractionService audioInteractionService,
        SystemMetricsService metricsService,
        WindowAppearanceService appearanceService,
        GlobalInteractionRouter interactionRouter)
    {
        InitializeComponent();
        _mediaSessionService = mediaSessionService;
        _audioInteractionService = audioInteractionService;
        _metricsService = metricsService;
        _interactionRouter = interactionRouter;
        appearanceService.Attach(this);
        _mediaSessionService.SnapshotChanged += OnSnapshotChanged;
        SettingsManager.InteractionSettingsChanged += OnInteractionSettingsChanged;
        Closed += OnClosed;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += Timer_Tick;
        _timer.Start();
        ApplySnapshot(_mediaSessionService.CurrentSnapshot ?? MediaSnapshot.Disconnected);
        _ = RefreshAudioAsync();
        ApplyInteractionSettings();
    }

    public void ToggleNear(Rect anchor)
    {
        if (IsVisible)
        {
            Close();
            return;
        }

        Show();
        UpdateLayout();
        var monitor = MonitorUtil.GetSelectedMonitor(SettingsManager.Current.TaskbarBarSelectedMonitor);
        var scale = Math.Max(1d / 96d, monitor.dpiX / 96d);
        var work = monitor.workArea.IsEmpty
            ? SystemParameters.WorkArea
            : new Rect(
                monitor.workArea.Left / scale,
                monitor.workArea.Top / scale,
                monitor.workArea.Width / scale,
                monitor.workArea.Height / scale);
        Left = Math.Clamp(anchor.Left + (anchor.Width - ActualWidth) / 2, work.Left, Math.Max(work.Left, work.Right - ActualWidth));
        var above = anchor.Top - ActualHeight - 8;
        var below = anchor.Bottom + 8;
        Top = above >= work.Top ? above : Math.Min(below, work.Bottom - ActualHeight);
        Activate();
    }

    private void OnSnapshotChanged(object? sender, MediaSnapshot snapshot) => Dispatcher.BeginInvoke(() => ApplySnapshot(snapshot));

    private void ApplySnapshot(MediaSnapshot snapshot)
    {
        _snapshot = snapshot;
        TitleText.Text = string.IsNullOrWhiteSpace(snapshot.Title) ? "暂无媒体" : snapshot.Title;
        ArtistText.Text = string.IsNullOrWhiteSpace(snapshot.Artist) ? snapshot.SourceName : snapshot.Artist;
        SourceText.Text = snapshot.SourceName;
        ArtworkImage.Source = snapshot.Artwork;
        ArtworkPlaceholder.Visibility = snapshot.Artwork is null ? Visibility.Visible : Visibility.Collapsed;
        PreviousButton.IsEnabled = snapshot.CanSkipPrevious;
        NextButton.IsEnabled = snapshot.CanSkipNext;
        PlayButton.IsEnabled = snapshot.CanPlayPause;
        RepeatButton.IsEnabled = snapshot.CanChangeRepeat;
        RepeatButton.ToolTip = snapshot.RepeatMode switch
        {
            MediaRepeatMode.One => "单曲循环",
            MediaRepeatMode.All => "列表循环",
            MediaRepeatMode.Off => "循环关闭",
            _ => "循环不可用"
        };
        PlayIcon.Symbol = snapshot.IsPlaying ? SymbolRegular.Pause24 : SymbolRegular.Play24;
        ProgressSlider.IsEnabled = snapshot.CanSeek;
        ProgressSlider.Maximum = Math.Max(1, snapshot.Duration);
        DurationText.Text = FormatTime(snapshot.Duration);
        UpdateProgress();
        ApplyInteractionSettings();
    }

    private void ApplyInteractionSettings()
    {
        var visible = SettingsManager.Current.Interaction.Mode == MediaInteractionMode.Gestures
            ? Visibility.Collapsed
            : Visibility.Visible;
        PreviousButton.Visibility = PlayButton.Visibility = NextButton.Visibility = visible;
    }

    private void OnInteractionSettingsChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(ApplyInteractionSettings);

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
            VolumeText.Text = _currentVolume is null ? "不可用" : $"{_currentVolume.VolumePercent}%";
        }
        finally
        {
            _isLoadingDevices = false;
        }
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        UpdateProgress();
        var metrics = _metricsService.Sample();
        RamMetric.Text = $"{metrics.SystemMemoryPercent}%";
        CpuMetric.Text = metrics.SystemCpuPercent is int cpu ? $"{cpu}%" : "—";
        GpuMetric.Text = metrics.SystemGpuPercent is int gpu ? $"{gpu}%" : "—";
        ProcessMetric.Text = $"{metrics.ProcessMemoryMegabytes} MB";
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

    private async void PreviousButton_Click(object sender, RoutedEventArgs e) => await _mediaSessionService.SkipPreviousAsync();
    private async void PlayButton_Click(object sender, RoutedEventArgs e) => await _mediaSessionService.TogglePlayPauseAsync();
    private async void NextButton_Click(object sender, RoutedEventArgs e) => await _mediaSessionService.SkipNextAsync();
    private async void RepeatButton_Click(object sender, RoutedEventArgs e) => await _mediaSessionService.CycleRepeatModeAsync();

    private async void ArtworkBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && DateTime.UtcNow >= _suppressArtworkClickUntilUtc &&
            SettingsManager.Current.Interaction.Mode is MediaInteractionMode.Hybrid or MediaInteractionMode.Gestures)
        {
            await _mediaSessionService.TogglePlayPauseAsync();
            e.Handled = true;
        }
    }

    private async void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && IsInteractiveControl(source))
            return;
        if (Mouse.LeftButton == MouseButtonState.Pressed)
            _suppressArtworkClickUntilUtc = DateTime.UtcNow.AddMilliseconds(350);
        var feedback = await _interactionRouter.ExecuteWheelAsync(
            e.Delta,
            Mouse.LeftButton == MouseButtonState.Pressed,
            Mouse.RightButton == MouseButtonState.Pressed);
        if (feedback is not null)
            e.Handled = true;
    }

    private static bool IsInteractiveControl(DependencyObject source)
    {
        while (source is not null)
        {
            if (source is Slider or ComboBox or ButtonBase)
                return true;
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private void ProgressSlider_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _isSeeking = true;
    private async void ProgressSlider_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isSeeking = false;
        if (_snapshot.CanSeek)
            await _mediaSessionService.SeekAsync(ProgressSlider.Value);
    }

    private async void DeviceCombo_DropDownOpened(object sender, EventArgs e) => await RefreshAudioAsync();
    private async void DeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoadingDevices && DeviceCombo.SelectedItem is AudioDeviceOption device)
            await _audioInteractionService.SetOutputDeviceAsync(device);
    }

    private async void VolumeSlider_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_currentVolume is null)
            return;
        var value = (int)Math.Round(VolumeSlider.Value);
        await Task.Run(() => _audioInteractionService.SetApplicationVolume(_currentVolume.ProcessName, value));
        VolumeText.Text = $"{value}%";
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (!DeviceCombo.IsDropDownOpen)
            Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _timer.Stop();
        _mediaSessionService.SnapshotChanged -= OnSnapshotChanged;
        SettingsManager.InteractionSettingsChanged -= OnInteractionSettingsChanged;
        Closed -= OnClosed;
    }
}
