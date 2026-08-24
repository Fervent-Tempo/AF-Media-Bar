using AFMediaBar.Abstractions;
using AFMediaBar.Interop;
using AFMediaBar.Models;
using AFMediaBar.Services;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI.ViewManagement;
using WinRT;
using WinRT.Interop;

namespace AFMediaBar.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly App _app;
    private readonly SettingsCoordinator _settingsCoordinator;
    private readonly WinUiStringLocalizer _localizer;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly WinUiDispatcher _dispatcher;
    private readonly MediaSessionService _mediaSessionService;
    private readonly LayoutRuntimeService _layoutRuntimeService = new();
    private readonly SystemMetricsService _systemMetricsService = new();
    private DesktopAcrylicController? _acrylicController;
    private SystemBackdropConfiguration? _backdropConfiguration;
    private ICompositionSupportsSystemBackdrop? _backdropTarget;
    private DesktopAcrylicBackdrop? _fallbackBackdrop;
    private readonly AccessibilitySettings? _accessibilitySettings;
    private DispatcherQueueTimer? _highContrastTimer;
    private DispatcherQueueTimer? _componentTimer;
    private DispatcherQueueTimer? _windowResizeTimer;
    private AudioMonitorService? _audioMonitorService;
    private IReadOnlyList<MediaSessionOption> _mediaSessions = [];
    private MediaSnapshot _mediaSnapshot = MediaSnapshot.Disconnected;
    private LayoutProfile? _activeLayoutProfile;
    private MetricSettings _effectiveMetricSettings;
    private MetricsWidgetSettings? _metricsWidgetSettings;
    private SpectrumWidgetSettings? _spectrumWidgetSettings;
    private SystemMetricsSnapshot _metricsSnapshot;
    private readonly float[] _spectrum = new float[AudioMonitorService.BandCount];
    private readonly Border[] _spectrumBars;
    private long _lastMetricsSampleTick;
    private long _lastSpectrumSampleTick;
    private int _metricCycleIndex;
    private int _metricCycleTicks;
    private bool _highContrastEventSubscribed;
    private bool _highContrastReadFailureLogged;
    private bool _mediaInitialized;
    private int _artworkApplyVersion;
    private WinUiArtworkImage? _displayedArtwork;
    private WinUiArtworkImage? _presentedArtwork;
    private bool _closing;
    private bool _updatingControls;
    private bool _windowDragging;
    private bool _windowDragMoved;
    private nint _windowHandle;
    private NativeMethods.Point _windowDragStartCursor;
    private int _windowDragStartLeft;
    private int _windowDragStartTop;
    private AppWindow? _appWindow;
    private bool _pendingSettingsResize;
    private bool _usesTunedAcrylicBackdrop;

    public MainWindow(App app)
    {
        _app = app;
        _settingsCoordinator = app.SettingsCoordinator;
        _localizer = app.Localizer;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _dispatcher = new WinUiDispatcher(_dispatcherQueue);
        _mediaSessionService = new MediaSessionService(
            new WinUiArtworkDecoder(),
            _localizer);
        _mediaSessionService.SnapshotChanged += MediaSessionService_OnSnapshotChanged;
        _mediaSessionService.SessionsChanged += MediaSessionService_OnSessionsChanged;
        try
        {
            _accessibilitySettings = new AccessibilitySettings();
        }
        catch (Exception exception)
        {
            _accessibilitySettings = null;
            DiagnosticsLogService.Write("winui-high-contrast-service-unavailable", exception);
        }
        ViewModel = new ShellViewModel(ShowSettings, _app.RequestShutdown);

        InitializeComponent();
        InitializeWindowBackdrop();
        _spectrumBars =
        [
            SpectrumBar0,
            SpectrumBar1,
            SpectrumBar2,
            SpectrumBar3,
            SpectrumBar4,
            SpectrumBar5,
            SpectrumBar6,
            SpectrumBar7,
            SpectrumBar8
        ];
        InitializeHighContrastMonitoring();
        Activated += MainWindow_OnActivated;
        Closed += MainWindow_OnClosed;
        RefreshLocalizedText();
        ApplyTheme(_settingsCoordinator.Current.Theme);
    }

    public ShellViewModel ViewModel { get; }

    internal void ApplySettings(
        ApplicationSettings settings,
        SettingsSection sections)
    {
        if (_closing)
        {
            return;
        }

        if (sections.HasFlag(SettingsSection.Language))
        {
            RefreshLocalizedText();
        }

        if (sections.HasFlag(SettingsSection.Appearance))
        {
            ApplyTheme(settings.Theme);
        }

        if (sections.HasFlag(SettingsSection.Performance) ||
            sections.HasFlag(SettingsSection.Layout) ||
            sections.HasFlag(SettingsSection.Window))
        {
            ApplyComponentLayout();
        }
    }

    internal void ApplyTheme(ThemeSettings settings)
    {
        Root.RequestedTheme = settings.MenuThemeMode switch
        {
            MenuThemeMode.Light => ElementTheme.Light,
            MenuThemeMode.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        UpdateBackdropConfigurationTheme();
        if (SettingsView.Visibility == Visibility.Visible)
        {
            ApplySettingsBackdrop();
        }
        else
        {
            ApplyPlayerBackdrop();
        }
        UpdateHighContrastStatus();
    }

    internal void DisposeShellResources()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        if (_highContrastEventSubscribed && _accessibilitySettings is not null)
        {
            try
            {
                _accessibilitySettings.HighContrastChanged -= AccessibilitySettings_OnHighContrastChanged;
            }
            catch (Exception exception)
            {
                DiagnosticsLogService.Write("winui-high-contrast-event-unsubscribe", exception);
            }
        }

        if (_highContrastTimer is not null)
        {
            _highContrastTimer.Stop();
            _highContrastTimer.Tick -= HighContrastTimer_OnTick;
            _highContrastTimer = null;
        }

        if (_windowResizeTimer is not null)
        {
            _windowResizeTimer.Stop();
            _windowResizeTimer.Tick -= WindowResizeTimer_OnTick;
            _windowResizeTimer = null;
        }

        StopComponentMonitoring();
        _systemMetricsService.Dispose();

        _mediaSessionService.SnapshotChanged -= MediaSessionService_OnSnapshotChanged;
        _mediaSessionService.SessionsChanged -= MediaSessionService_OnSessionsChanged;
        _mediaSessionService.Dispose();
        _displayedArtwork?.Dispose();
        if (!ReferenceEquals(_presentedArtwork, _displayedArtwork))
        {
            _presentedArtwork?.Dispose();
        }
        _displayedArtwork = null;
        _presentedArtwork = null;

        DisposeWindowBackdrop();

        Activated -= MainWindow_OnActivated;
        Closed -= MainWindow_OnClosed;
        _dispatcher.Shutdown();
    }

    private void MainWindow_OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_backdropConfiguration is not null)
        {
            _backdropConfiguration.IsInputActive =
                args.WindowActivationState != WindowActivationState.Deactivated;
        }

        if (_windowHandle != nint.Zero)
        {
            return;
        }

        _windowHandle = WindowNative.GetWindowHandle(this);
        ConfigureFloatingWindow();
        if (!_mediaInitialized)
        {
            _mediaInitialized = true;
            _ = InitializeMediaAsync();
        }
    }

    private void ConfigureFloatingWindow()
    {
        var appWindow = AppWindow.GetFromWindowId(
            Win32Interop.GetWindowIdFromWindow(_windowHandle));
        _appWindow = appWindow;
        if (appWindow?.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        var extendedStyle = NativeMethods.GetWindowLongPtr(
            _windowHandle,
            NativeMethods.GwlExStyle).ToInt64();
        extendedStyle |= NativeMethods.WsExToolWindow;
        NativeMethods.SetWindowLongPtr(
            _windowHandle,
            NativeMethods.GwlExStyle,
            new nint(extendedStyle));
        var borderless = WinUiNativeMethods.ConfigureBorderlessWindow(_windowHandle);
        DiagnosticsLogService.Write(
            "winui-window-composition",
            details: $"Borderless={borderless};Backdrop={(_usesTunedAcrylicBackdrop ? "tuned-desktop-acrylic" : "desktop-acrylic-fallback")};Handle=0x{_windowHandle.ToInt64():X}");
        var borderColor = 0u;
        _ = WinUiNativeMethods.SetBorderColor(_windowHandle, ref borderColor);
        var cornerPreference = WinUiNativeMethods.DwmWindowCornerDoNotRound;
        _ = WinUiNativeMethods.SetCornerPreference(_windowHandle, ref cornerPreference);
        ApplyComponentLayout();
        ResizeForView(settingsView: false);
        var windowSettings = _settingsCoordinator.Current.Window;
        if (windowSettings.FloatingLeft is { } left &&
            windowSettings.FloatingTop is { } top)
        {
            appWindow?.Move(new Windows.Graphics.PointInt32(left, top));
        }
    }

    private void InitializeWindowBackdrop()
    {
        try
        {
            if (!DesktopAcrylicController.IsSupported())
            {
                throw new NotSupportedException("Desktop Acrylic is not supported.");
            }

            _backdropTarget = this.As<ICompositionSupportsSystemBackdrop>();
            _backdropConfiguration = new SystemBackdropConfiguration
            {
                IsInputActive = true
            };
            UpdateBackdropConfigurationTheme();
            _acrylicController = new DesktopAcrylicController();
            if (!_acrylicController.AddSystemBackdropTarget(_backdropTarget))
            {
                throw new InvalidOperationException("Unable to attach the Acrylic backdrop target.");
            }

            _acrylicController.SetSystemBackdropConfiguration(_backdropConfiguration);
            _usesTunedAcrylicBackdrop = true;
            ApplyPlayerBackdrop();
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("winui-acrylic-controller", exception);
            DisposeWindowBackdrop();
            _fallbackBackdrop = new DesktopAcrylicBackdrop();
            SystemBackdrop = _fallbackBackdrop;
            _usesTunedAcrylicBackdrop = false;
        }
    }

    private void ApplyPlayerBackdrop()
    {
        if (_acrylicController is null)
        {
            return;
        }

        _acrylicController.ResetProperties();
        _acrylicController.Kind = DesktopAcrylicKind.Base;
        _acrylicController.TintColor = Colors.Black;
        _acrylicController.TintOpacity = 0.02f;
        _acrylicController.LuminosityOpacity = 0.08f;
        _acrylicController.FallbackColor = Root.ActualTheme == ElementTheme.Light
            ? Windows.UI.Color.FromArgb(232, 243, 243, 243)
            : Windows.UI.Color.FromArgb(232, 32, 32, 32);
    }

    private void ApplySettingsBackdrop()
    {
        _acrylicController?.ResetProperties();
    }

    private void UpdateBackdropConfigurationTheme()
    {
        if (_backdropConfiguration is null)
        {
            return;
        }

        _backdropConfiguration.Theme = Root.ActualTheme == ElementTheme.Light
            ? SystemBackdropTheme.Light
            : SystemBackdropTheme.Dark;
    }

    private void DisposeWindowBackdrop()
    {
        if (_acrylicController is not null)
        {
            if (_backdropTarget is not null)
            {
                _acrylicController.RemoveSystemBackdropTarget(_backdropTarget);
            }

            _acrylicController.Dispose();
            _acrylicController = null;
        }

        _backdropTarget = null;
        _backdropConfiguration = null;
        _fallbackBackdrop = null;
        SystemBackdrop = null;
    }

    private void Root_OnPointerPressed(
        object sender,
        PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(Root);
        if (!point.Properties.IsLeftButtonPressed ||
            IsInteractivePointerSource(args.OriginalSource as DependencyObject) ||
            _windowHandle == nint.Zero ||
            !NativeMethods.GetCursorPos(out _windowDragStartCursor) ||
            !NativeMethods.GetWindowRect(
                _windowHandle,
                out var windowRect))
        {
            return;
        }

        _windowDragStartLeft = windowRect.Left;
        _windowDragStartTop = windowRect.Top;
        _windowDragMoved = false;
        _windowDragging = Root.CapturePointer(args.Pointer);
        args.Handled = _windowDragging;
    }

    private void Root_OnPointerMoved(
        object sender,
        PointerRoutedEventArgs args)
    {
        if (!_windowDragging ||
            !args.GetCurrentPoint(Root).Properties.IsLeftButtonPressed ||
            !NativeMethods.GetCursorPos(out var cursor))
        {
            return;
        }

        var deltaX = cursor.X - _windowDragStartCursor.X;
        var deltaY = cursor.Y - _windowDragStartCursor.Y;
        _windowDragMoved |= Math.Abs(deltaX) >= 3 || Math.Abs(deltaY) >= 3;
        if (_appWindow is not null)
        {
            _appWindow.Move(new Windows.Graphics.PointInt32(
                _windowDragStartLeft + deltaX,
                _windowDragStartTop + deltaY));
        }

        args.Handled = true;
    }

    private void Root_OnPointerReleased(
        object sender,
        PointerRoutedEventArgs args)
    {
        if (!_windowDragging)
        {
            return;
        }

        FinishWindowDrag(commit: _windowDragMoved);
        args.Handled = true;
    }

    private void Root_OnPointerCanceled(
        object sender,
        PointerRoutedEventArgs args)
    {
        FinishWindowDrag(commit: _windowDragMoved);
    }

    private void Root_OnPointerCaptureLost(
        object sender,
        PointerRoutedEventArgs args)
    {
        FinishWindowDrag(commit: _windowDragMoved);
    }

    private void FinishWindowDrag(bool commit)
    {
        if (!_windowDragging)
        {
            return;
        }

        _windowDragging = false;
        Root.ReleasePointerCaptures();
        if (commit &&
            NativeMethods.GetWindowRect(_windowHandle, out var windowRect))
        {
            try
            {
                _settingsCoordinator.SynchronizeWindow(
                    _settingsCoordinator.Current.Window with
                    {
                        FloatingLeft = windowRect.Left,
                        FloatingTop = windowRect.Top
                    });
            }
            catch (Exception exception)
            {
                DiagnosticsLogService.Write("winui-save-window-position", exception);
            }
        }

        _windowDragMoved = false;
    }

    private static bool IsInteractivePointerSource(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ButtonBase or ComboBox or TextBox)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private async Task InitializeMediaAsync()
    {
        try
        {
            await _mediaSessionService.InitializeAsync();
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("winui-media-session-initialize", exception);
            _dispatcher.Post(
                () => SetMediaStatus(_localizer.Get("Msg.SessionAccessFailed")),
                UiDispatchPriority.Input);
        }
    }

    private void MediaSessionService_OnSnapshotChanged(
        object? sender,
        MediaSnapshot snapshot)
    {
        _dispatcher.Post(
            () => ApplyMediaSnapshot(snapshot),
            UiDispatchPriority.Input);
    }

    private void MediaSessionService_OnSessionsChanged(
        IReadOnlyList<MediaSessionOption> sessions)
    {
        _dispatcher.Post(
            () => ApplyMediaSessions(sessions),
            UiDispatchPriority.Input);
    }

    private void ApplyMediaSnapshot(MediaSnapshot snapshot)
    {
        if (_closing)
        {
            return;
        }

        _mediaSnapshot = snapshot;
        var title = snapshot.IsConnected && !string.IsNullOrWhiteSpace(snapshot.Title)
            ? snapshot.Title
            : _localizer.Get("Main.Placeholder.Title");
        var artist = snapshot.IsConnected && !string.IsNullOrWhiteSpace(snapshot.Artist)
            ? snapshot.Artist
            : _localizer.Get("Main.Placeholder.Subtitle");
        MediaTitleText.Text = title;
        MediaArtistText.Text = artist;
        ToolTipService.SetToolTip(MediaTitleText, title);
        ToolTipService.SetToolTip(MediaArtistText, artist);
        ApplyArtwork(snapshot.Artwork);
        PreviousMediaButton.IsEnabled = snapshot.IsConnected && snapshot.CanSkipPrevious;
        PlayPauseMediaButton.IsEnabled = snapshot.IsConnected && snapshot.CanPlayPause;
        NextMediaButton.IsEnabled = snapshot.IsConnected && snapshot.CanSkipNext;
        PlayPauseIcon.Glyph = snapshot.IsPlaying ? "\uE769" : "\uE768";
        ToolTipService.SetToolTip(
            PreviousMediaButton,
            _localizer.Get("Main.Control.Previous"));
        ToolTipService.SetToolTip(
            PlayPauseMediaButton,
            snapshot.IsPlaying
            ? _localizer.Get("Main.Control.Pause")
            : _localizer.Get("Main.Control.Play"));
        ToolTipService.SetToolTip(
            NextMediaButton,
            _localizer.Get("Main.Control.Next"));
        SetMediaStatus(snapshot.IsConnected
            ? snapshot.SourceName
            : _localizer.Get("Shell.StatusReady"));
    }

    private void ApplyMediaSessions(IReadOnlyList<MediaSessionOption> sessions)
    {
        if (_closing)
        {
            return;
        }

        _mediaSessions = sessions;
        _updatingControls = true;
        try
        {
            MediaSourceComboBox.ItemsSource = sessions;
            MediaSourceComboBox.IsEnabled = sessions.Count > 0;
            MediaSourceComboBox.PlaceholderText = sessions.Count > 0
                ? _localizer.Get("Main.Menu.Sources")
                : _localizer.Get("Main.Menu.NoSessions");
            var selected = sessions.FirstOrDefault(session => session.IsSelected);
            MediaSourceComboBox.SelectedValue = selected?.Key;
        }
        finally
        {
            _updatingControls = false;
        }
    }

    private void SetMediaStatus(string text)
    {
        if (!_closing)
        {
            MediaStatusText.Text = text;
        }
    }

    private void ApplyArtwork(AFMediaBar.Abstractions.IArtworkImage? artwork)
    {
        if (ReferenceEquals(_displayedArtwork, artwork))
        {
            return;
        }

        var previousArtwork = _displayedArtwork;
        _displayedArtwork = artwork as WinUiArtworkImage;
        if (previousArtwork is not null &&
            !ReferenceEquals(previousArtwork, _displayedArtwork) &&
            ReferenceEquals(previousArtwork, _presentedArtwork))
        {
            previousArtwork.Dispose();
            _presentedArtwork = null;
        }
        var version = ++_artworkApplyVersion;
        ArtworkImage.Source = null;
        ArtworkImage.Visibility = Visibility.Collapsed;
        ArtworkPlaceholderIcon.Visibility = Visibility.Visible;
        if (_displayedArtwork is not null)
        {
            _ = ApplyArtworkAsync(_displayedArtwork, version);
        }
    }

    private async Task ApplyArtworkAsync(WinUiArtworkImage artwork, int version)
    {
        try
        {
            var source = new SoftwareBitmapSource();
            await source.SetBitmapAsync(artwork.Bitmap);
            if (_closing ||
                version != _artworkApplyVersion ||
                !ReferenceEquals(_displayedArtwork, artwork))
            {
                return;
            }

            ArtworkImage.Source = source;
            ArtworkImage.Visibility = Visibility.Visible;
            ArtworkPlaceholderIcon.Visibility = Visibility.Collapsed;
            _presentedArtwork = artwork;
        }
        catch (Exception exception)
        {
            if (!_closing &&
                version == _artworkApplyVersion &&
                ReferenceEquals(_displayedArtwork, artwork))
            {
                DiagnosticsLogService.Write("winui-artwork-present", exception);
                ArtworkImage.Source = null;
                ArtworkImage.Visibility = Visibility.Collapsed;
                ArtworkPlaceholderIcon.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            if (!ReferenceEquals(_displayedArtwork, artwork) &&
                !ReferenceEquals(_presentedArtwork, artwork))
            {
                artwork.Dispose();
            }
        }
    }

    private async void MediaSourceComboBox_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        if (_updatingControls || MediaSourceComboBox.SelectedValue is not string key)
        {
            return;
        }

        await RunMediaCommandAsync(() => _mediaSessionService.SelectSessionAsync(key));
    }

    private async void PreviousMediaButton_OnClick(object sender, RoutedEventArgs args)
    {
        await RunMediaCommandAsync(_mediaSessionService.SkipPreviousAsync);
    }

    private async void PlayPauseMediaButton_OnClick(object sender, RoutedEventArgs args)
    {
        await RunMediaCommandAsync(_mediaSessionService.TogglePlayPauseAsync);
    }

    private async void NextMediaButton_OnClick(object sender, RoutedEventArgs args)
    {
        await RunMediaCommandAsync(_mediaSessionService.SkipNextAsync);
    }

    private async Task RunMediaCommandAsync(Func<Task> command)
    {
        try
        {
            await command();
        }
        catch (Exception exception)
        {
            DiagnosticsLogService.Write("winui-media-command", exception);
            SetMediaStatus(_localizer.Get("Msg.MediaControlFailed"));
        }
    }

    private void ApplyComponentLayout()
    {
        if (_closing)
        {
            return;
        }

        var settings = _settingsCoordinator.Current;
        var vertical = settings.Window.LayoutMode == PlayerLayoutMode.Vertical;
        _activeLayoutProfile = _layoutRuntimeService.ResolveProfile(settings.Layout, vertical);
        _effectiveMetricSettings = LayoutRuntimeService.ResolveComponentSettings(
            _activeLayoutProfile,
            settings.Metrics);
        _metricsWidgetSettings = LayoutRuntimeService.FindWidgets(
                _activeLayoutProfile,
                BuiltInWidgetTypeIds.Metrics)
            .Select(widget => widget.Settings)
            .OfType<MetricsWidgetSettings>()
            .FirstOrDefault();
        _spectrumWidgetSettings = LayoutRuntimeService.FindWidgets(
                _activeLayoutProfile,
                BuiltInWidgetTypeIds.Spectrum)
            .Select(widget => widget.Settings)
            .OfType<SpectrumWidgetSettings>()
            .FirstOrDefault();

        var hasArtwork = settings.Window.ShowArtwork &&
            LayoutRuntimeService.ContainsWidget(
                _activeLayoutProfile,
                BuiltInWidgetTypeIds.Artwork);
        var hasMediaInfo = settings.Window.ShowMediaInfo &&
            LayoutRuntimeService.ContainsWidget(
                _activeLayoutProfile,
                BuiltInWidgetTypeIds.MediaText);
        var hasSource = LayoutRuntimeService.ContainsWidget(
            _activeLayoutProfile,
            BuiltInWidgetTypeIds.MediaSource);
        var commands = LayoutRuntimeService.FindWidgets(
                _activeLayoutProfile,
                BuiltInWidgetTypeIds.Command)
            .Select(widget => widget.Settings)
            .OfType<CommandWidgetSettings>()
            .Select(widget => widget.Command)
            .ToHashSet();
        var hasMetrics = _metricsWidgetSettings is not null;
        var hasSpectrum = _spectrumWidgetSettings is not null;

        ArtworkBorder.Visibility = hasArtwork ? Visibility.Visible : Visibility.Collapsed;
        MediaInfoPanel.Visibility = hasMediaInfo ? Visibility.Visible : Visibility.Collapsed;
        MediaSourceComboBox.Visibility = hasSource ? Visibility.Visible : Visibility.Collapsed;
        ArtworkColumn.Width = hasArtwork ? new GridLength(48) : new GridLength(0);
        MediaInfoColumn.MinWidth = hasMediaInfo ? 150 : 0;
        MediaInfoColumn.Width = hasMediaInfo ? new GridLength(150) : new GridLength(0);
        SourceColumn.Width = hasSource ? new GridLength(120) : new GridLength(0);
        ControlsColumn.Width = commands.Count > 0 ? GridLength.Auto : new GridLength(0);
        PreviousMediaButton.Visibility = commands.Contains(MediaCommandKind.Previous)
            ? Visibility.Visible
            : Visibility.Collapsed;
        PlayPauseMediaButton.Visibility = commands.Contains(MediaCommandKind.PlayPause)
            ? Visibility.Visible
            : Visibility.Collapsed;
        NextMediaButton.Visibility = commands.Contains(MediaCommandKind.Next)
            ? Visibility.Visible
            : Visibility.Collapsed;
        MetricsHost.Visibility = hasMetrics ? Visibility.Visible : Visibility.Collapsed;
        SpectrumHost.Visibility = hasSpectrum ? Visibility.Visible : Visibility.Collapsed;
        ComponentIndicatorsHost.Visibility = hasMetrics || hasSpectrum
            ? Visibility.Visible
            : Visibility.Collapsed;

        DiagnosticsLogService.Write(
            "winui-layout-applied",
            details: $"Mode={(vertical ? "vertical" : "horizontal")};Artwork={hasArtwork};MediaInfo={hasMediaInfo};Source={hasSource};Commands={string.Join(',', commands)};Metrics={hasMetrics};Spectrum={hasSpectrum}");

        if (!hasMetrics)
        {
            MetricsText.Text = string.Empty;
        }

        if (!hasSpectrum)
        {
            Array.Clear(_spectrum, 0, _spectrum.Length);
            UpdateSpectrumBars();
        }

        _metricCycleIndex = 0;
        _metricCycleTicks = 0;
        _lastMetricsSampleTick = 0;
        _lastSpectrumSampleTick = 0;
        ScheduleWindowResize(settingsView: false);
        ConfigureComponentMonitoring();
    }

    private void ConfigureComponentMonitoring()
    {
        if (_closing || (_metricsWidgetSettings is null && _spectrumWidgetSettings is null))
        {
            StopComponentMonitoring();
            return;
        }

        _componentTimer ??= _dispatcherQueue.CreateTimer();
        _componentTimer.Interval = TimeSpan.FromMilliseconds(100);
        _componentTimer.IsRepeating = true;
        _componentTimer.Tick -= ComponentTimer_OnTick;
        _componentTimer.Tick += ComponentTimer_OnTick;
        _componentTimer.Start();

        if (_spectrumWidgetSettings is not null)
        {
            _audioMonitorService ??= new AudioMonitorService();
        }
        else
        {
            _audioMonitorService?.Dispose();
            _audioMonitorService = null;
        }
    }

    private void StopComponentMonitoring()
    {
        if (_componentTimer is not null)
        {
            _componentTimer.Stop();
            _componentTimer.Tick -= ComponentTimer_OnTick;
            _componentTimer = null;
        }

        _audioMonitorService?.Dispose();
        _audioMonitorService = null;
    }

    private void ComponentTimer_OnTick(DispatcherQueueTimer sender, object args)
    {
        if (_closing)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (_metricsWidgetSettings is not null)
        {
            var refreshInterval = LayoutRuntimeService.ResolveMetricRefreshInterval(
                _activeLayoutProfile,
                2_500);
            if (_lastMetricsSampleTick == 0 || now - _lastMetricsSampleTick >= refreshInterval)
            {
                _lastMetricsSampleTick = now;
                try
                {
                    var samplingSettings = LayoutRuntimeService.ResolveMetricSamplingSettings(
                        _activeLayoutProfile,
                        _effectiveMetricSettings);
                    _metricsSnapshot = _systemMetricsService.Sample(samplingSettings);
                    var cycle = _metricsWidgetSettings.CycleMetrics is { Count: > 0 }
                        ? _metricsWidgetSettings.CycleMetrics
                        : [_metricsWidgetSettings.Metric];
                    _metricCycleIndex = Math.Clamp(_metricCycleIndex, 0, cycle.Count - 1);
                    MetricsText.Text = MetricTextFormatter.Format(
                        _metricsSnapshot,
                        cycle[_metricCycleIndex]);
                    _metricCycleTicks++;
                    if (cycle.Count > 1 && _metricCycleTicks >= 3)
                    {
                        _metricCycleTicks = 0;
                        _metricCycleIndex = (_metricCycleIndex + 1) % cycle.Count;
                    }
                }
                catch (Exception exception)
                {
                    DiagnosticsLogService.Write("winui-metrics-sample", exception);
                }
            }
        }

        if (_spectrumWidgetSettings is not null && _audioMonitorService is not null)
        {
            var refreshRate = Math.Clamp(_spectrumWidgetSettings.RefreshRateHz, 1, 60);
            if (_lastSpectrumSampleTick == 0 ||
                now - _lastSpectrumSampleTick >= 1_000 / refreshRate)
            {
                _lastSpectrumSampleTick = now;
                try
                {
                    _audioMonitorService.GetSpectrum(_spectrum);
                    UpdateSpectrumBars(_spectrumWidgetSettings.SensitivityPercent);
                }
                catch (Exception exception)
                {
                    DiagnosticsLogService.Write("winui-spectrum-sample", exception);
                    Array.Clear(_spectrum, 0, _spectrum.Length);
                    UpdateSpectrumBars();
                }
            }
        }
    }

    private void UpdateSpectrumBars(int sensitivityPercent = 100)
    {
        var bandCount = Math.Clamp(
            _spectrumWidgetSettings?.BandCount ?? AudioMonitorService.BandCount,
            1,
            AudioMonitorService.BandCount);
        var sensitivity = Math.Clamp(sensitivityPercent, 1, 200) / 100d;
        for (var index = 0; index < _spectrumBars.Length; index++)
        {
            var value = index < bandCount
                ? Math.Clamp(_spectrum[index] * sensitivity, 0f, 1f)
                : 0f;
            _spectrumBars[index].Height = Math.Clamp(
                3 + Math.Sqrt(value) * 20,
                3,
                22);
        }
    }

    private void RefreshLocalizedMediaText()
    {
        ApplyMediaSnapshot(_mediaSnapshot);
        ApplyMediaSessions(_mediaSessions);
    }

    private void OpenDetailedSettingsMenuItem_OnClick(
        object sender,
        RoutedEventArgs args)
    {
        ShowSettings();
    }

    private void ExitContextMenuItem_OnClick(
        object sender,
        RoutedEventArgs args)
    {
        _app.RequestShutdown();
    }

    private void ShowSettings()
    {
        if (_closing)
        {
            return;
        }

        HeaderView.Visibility = Visibility.Collapsed;
        FooterView.Visibility = Visibility.Collapsed;
        ShellView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Visible;
        ApplySettingsBackdrop();
        StopComponentMonitoring();
        ScheduleWindowResize(settingsView: true);
        RefreshLocalizedText();
    }

    private void BackButton_OnClick(object sender, RoutedEventArgs args)
    {
        ApplyPlayerBackdrop();
        HeaderView.Visibility = Visibility.Collapsed;
        FooterView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;
        ShellView.Visibility = Visibility.Visible;
        ApplyComponentLayout();
        RefreshLocalizedText();
    }

    private void ResizeForView(bool settingsView)
    {
        if (_windowHandle == nint.Zero)
        {
            return;
        }

        var normalSize = _activeLayoutProfile is null
            ? new Windows.Graphics.SizeInt32(560, 72)
            : new Windows.Graphics.SizeInt32(
                Math.Clamp(
                    (int)Math.Ceiling(
                        LayoutRuntimeService.CalculateDesiredSize(_activeLayoutProfile).WidthDip + 8),
                    320,
                    900),
                Math.Clamp(
                    (int)Math.Ceiling(
                        LayoutRuntimeService.CalculateDesiredSize(_activeLayoutProfile).HeightDip +
                        (ComponentIndicatorsHost.Visibility == Visibility.Visible ? 28 : 8)),
                    48,
                    180));
        var targetSize = settingsView
            ? new Windows.Graphics.SizeInt32(640, 400)
            : normalSize;
        var resized = WinUiNativeMethods.ResizeClientWindow(
            _windowHandle,
            targetSize.Width,
            targetSize.Height);
        var hasResizedRect = NativeMethods.GetWindowRect(
            _windowHandle,
            out var resizedRect);
        if (!resized ||
            !hasResizedRect ||
            resizedRect.Width < targetSize.Width ||
            resizedRect.Height < targetSize.Height)
        {
            DiagnosticsLogService.Write(
                "winui-window-resize",
                details: $"Handle=0x{_windowHandle.ToInt64():X};Target={targetSize.Width}x{targetSize.Height};Result={resized};Actual={resizedRect.Width}x{resizedRect.Height}");
        }
    }

    private void ScheduleWindowResize(bool settingsView)
    {
        if (_closing || _windowHandle == nint.Zero)
        {
            return;
        }

        _windowResizeTimer ??= _dispatcherQueue.CreateTimer();
        _windowResizeTimer.Stop();
        _windowResizeTimer.Interval = TimeSpan.FromMilliseconds(50);
        _windowResizeTimer.IsRepeating = false;
        _windowResizeTimer.Tick -= WindowResizeTimer_OnTick;
        _windowResizeTimer.Tick += WindowResizeTimer_OnTick;
        _pendingSettingsResize = settingsView;
        _windowResizeTimer.Start();
    }

    private void WindowResizeTimer_OnTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        ResizeForView(_pendingSettingsResize);
    }

    private void ThemeComboBox_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        if (_updatingControls || ThemeComboBox.SelectedIndex < 0)
        {
            return;
        }

        var mode = ThemeComboBox.SelectedIndex switch
        {
            1 => MenuThemeMode.Light,
            2 => MenuThemeMode.Dark,
            _ => MenuThemeMode.Automatic
        };
        _settingsCoordinator.UpdateTheme(
            _settingsCoordinator.Current.Theme with { MenuThemeMode = mode });
    }

    private void LanguageComboBox_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        if (_updatingControls || LanguageComboBox.SelectedIndex < 0)
        {
            return;
        }

        var language = LanguageComboBox.SelectedIndex switch
        {
            1 => AppLanguage.ZhCn,
            2 => AppLanguage.ZhTw,
            3 => AppLanguage.EnUs,
            _ => AppLanguage.FollowSystem
        };
        _settingsCoordinator.UpdateLanguage(language);
    }

    private void AccessibilitySettings_OnHighContrastChanged(
        AccessibilitySettings sender,
        object args)
    {
        if (_closing)
        {
            return;
        }

        _dispatcher.Post(UpdateHighContrastStatus, UiDispatchPriority.Input);
    }

    private void InitializeHighContrastMonitoring()
    {
        if (_accessibilitySettings is null)
        {
            return;
        }

        try
        {
            _accessibilitySettings.HighContrastChanged += AccessibilitySettings_OnHighContrastChanged;
            _highContrastEventSubscribed = true;
        }
        catch (Exception exception)
        {
            // Some Windows configurations expose the property but reject the WinRT
            // event registration with ERROR_NOT_FOUND. Polling keeps startup and
            // high-contrast state changes functional without making the event a gate.
            DiagnosticsLogService.Write("winui-high-contrast-event-unavailable", exception);
            StartHighContrastPolling();
        }
    }

    private void StartHighContrastPolling()
    {
        if (_highContrastTimer is not null || _closing)
        {
            return;
        }

        try
        {
            _highContrastTimer = _dispatcherQueue.CreateTimer();
            _highContrastTimer.Interval = TimeSpan.FromSeconds(1);
            _highContrastTimer.IsRepeating = true;
            _highContrastTimer.Tick += HighContrastTimer_OnTick;
            _highContrastTimer.Start();
        }
        catch (Exception exception)
        {
            _highContrastTimer = null;
            DiagnosticsLogService.Write("winui-high-contrast-polling-unavailable", exception);
        }
    }

    private void HighContrastTimer_OnTick(DispatcherQueueTimer sender, object args)
    {
        if (!_closing)
        {
            UpdateHighContrastStatus();
        }
    }

    private void UpdateHighContrastStatus()
    {
        HighContrastStatusText.Text = _localizer.Get("Shell.StatusHighContrast");
        var hasHighContrast = TryReadHighContrast(out var highContrast) && highContrast;
        HighContrastStatusText.Visibility = hasHighContrast
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (_backdropConfiguration is not null)
        {
            _backdropConfiguration.IsHighContrast = hasHighContrast;
        }
    }

    private bool TryReadHighContrast(out bool highContrast)
    {
        highContrast = false;
        if (_accessibilitySettings is null)
        {
            return false;
        }

        try
        {
            highContrast = _accessibilitySettings.HighContrast;
            return true;
        }
        catch (Exception exception)
        {
            if (!_highContrastReadFailureLogged)
            {
                _highContrastReadFailureLogged = true;
                DiagnosticsLogService.Write("winui-high-contrast-read", exception);
            }

            return false;
        }
    }

    private void RefreshLocalizedText()
    {
        if (_closing)
        {
            return;
        }

        _updatingControls = true;
        try
        {
            TitleText.Text = _localizer.Get("Shell.Title");
            TaglineText.Text = _localizer.Get("Shell.Tagline");
            ExitButtonText.Text = _localizer.Get("Shell.Exit");
            OpenDetailedSettingsMenuItem.Text = _localizer.Get("Shell.OpenDetailedSettings");
            ExitContextMenuItem.Text = _localizer.Get("Shell.Exit");
            SettingsTitleText.Text = _localizer.Get("Shell.SettingsTitle");
            SettingsDescriptionText.Text = _localizer.Get("Shell.SettingsDescription");
            ThemeLabelText.Text = _localizer.Get("Shell.Theme");
            ThemeDescriptionText.Text = _localizer.Get("Shell.ThemeDescription");
            ThemeAutomaticItem.Content = _localizer.Get("Shell.ThemeAutomatic");
            ThemeLightItem.Content = _localizer.Get("Shell.ThemeLight");
            ThemeDarkItem.Content = _localizer.Get("Shell.ThemeDark");
            LanguageLabelText.Text = _localizer.Get("Shell.Language");
            LanguageDescriptionText.Text = _localizer.Get("Shell.LanguageDescription");
            LanguageFollowSystemItem.Content = _localizer.Get("Shell.LanguageFollowSystem");
            LanguageZhCnItem.Content = _localizer.Get("Shell.LanguageZhCn");
            LanguageZhTwItem.Content = _localizer.Get("Shell.LanguageZhTw");
            LanguageEnUsItem.Content = _localizer.Get("Shell.LanguageEnUs");
            BackButtonText.Text = _localizer.Get("Shell.Back");
            CloseButtonText.Text = _localizer.Get("Shell.Close");
            ToolTipService.SetToolTip(
                SettingsNavigationButton,
                _localizer.Get("Shell.OpenSettings"));
            ToolTipService.SetToolTip(BackButton, _localizer.Get("Shell.Back"));
            ViewModel.Status = _localizer.Get("Shell.StatusReady");

            var settings = _settingsCoordinator.Current;
            ThemeComboBox.SelectedIndex = settings.Theme.MenuThemeMode switch
            {
                MenuThemeMode.Light => 1,
                MenuThemeMode.Dark => 2,
                _ => 0
            };
            LanguageComboBox.SelectedIndex = settings.Language switch
            {
                AppLanguage.ZhCn => 1,
                AppLanguage.ZhTw => 2,
                AppLanguage.EnUs => 3,
                _ => 0
            };
            RefreshLocalizedMediaText();
            UpdateHighContrastStatus();
        }
        finally
        {
            _updatingControls = false;
        }
    }

    private void MainWindow_OnClosed(object sender, WindowEventArgs args)
    {
        DiagnosticsLogService.Write("winui-shell-closed");
        DisposeShellResources();
    }
}
