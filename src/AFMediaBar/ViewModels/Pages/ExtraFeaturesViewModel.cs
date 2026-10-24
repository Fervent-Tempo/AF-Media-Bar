using System.Collections.ObjectModel;
using System.Windows.Threading;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Resources;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>媒体来源、加入快速启动的意图与曲目通知设置；小音符参数由小组件页编辑。</summary>
public partial class ExtraFeaturesViewModel : ObservableObject
{
    private readonly MediaSourceRegistry _sources;
    private readonly MediaSessionService _mediaSessions;
    private readonly IDisplayMonitorService _displayMonitorService;
    private readonly LocalizationService _localization;
    private readonly QuickLaunchService _quickLaunch;

    // 设置写入会同步通知订阅者，而写设置的那一方可能在后台线程上（更新检查的网络等待之后就是如此）：
    // 本页的 Sources 绑定到界面，跨线程改它们会让 WPF 的 CollectionView 抛异常，
    // 因此刷新 MUST 回到 UI 线程。根因侧同样已修（`UpdateService.WriteUpdateSettings`），这里是第二道防线。
    // A settings write notifies its subscribers synchronously, and whoever writes may be on a background thread (that is exactly
    // what happens after the update check's network wait): this page's Sources are bound to the interface,
    // and mutating them across threads makes WPF's CollectionView throw, so the refresh MUST come back to the UI thread. The root
    // cause is fixed as well (`UpdateService.WriteUpdateSettings`); this is the second line of defence.
    private readonly Dispatcher _dispatcher = DispatcherHelper.Current;

    private bool _isRefreshing;
    private string? _statusKey;
    // 状态行是瞬态反馈：写上去若干秒后自动清空，否则「已添加到快速启动」会一直挂在那里。
    // The status line is transient feedback: it clears itself a few seconds after being written, otherwise
    // "added to quick launch" would just stay there.
    private readonly DispatcherTimer _statusTimer;

    /// <summary>状态行留存时长：够读一句话，又不至于让用户觉得界面卡住。</summary>
    private static readonly TimeSpan StatusDisplayDuration = TimeSpan.FromSeconds(4);

    public ObservableCollection<MediaSourceSettingItem> Sources { get; } = [];
    public IReadOnlyList<DisplayMonitorOption> MonitorOptions { get; private set; } = [];
    public IReadOnlyList<MediaSourceDescriptor> PreferredSourceOptions { get; private set; } = [];

    public MediaSelectionMode SelectionMode
    {
        get => SettingsManager.Current.MediaSelection.Mode;
        set
        {
            if (_isRefreshing || value == SelectionMode) return;
            SettingsManager.SetMediaSelectionSettings(SettingsManager.Current.MediaSelection with { Mode = value });
        }
    }

    public string PreferredSourceId
    {
        get
        {
            var sourceId = _sources.NormalizeSourceId(SettingsManager.Current.MediaSelection.PreferredSourceId ?? string.Empty);
            return PreferredSourceOptions.FirstOrDefault(option =>
                string.Equals(option.SourceId, sourceId, StringComparison.OrdinalIgnoreCase))?.SourceId ?? sourceId;
        }
        set
        {
            if (_isRefreshing) return;
            var sourceId = string.IsNullOrWhiteSpace(value) ? null : _sources.NormalizeSourceId(value.Trim());
            if (string.Equals(sourceId, SettingsManager.Current.MediaSelection.PreferredSourceId, StringComparison.OrdinalIgnoreCase)) return;
            SettingsManager.SetMediaSelectionSettings(SettingsManager.Current.MediaSelection with { PreferredSourceId = sourceId });
        }
    }

    public bool IsPreferredApplication => SelectionMode == MediaSelectionMode.PreferredApplication;
    public bool IsPreferredSourceBlocked => !string.IsNullOrEmpty(PreferredSourceId) &&
        !_sources.IsAllowed(PreferredSourceId, SettingsManager.Current.SmtcSourceFilter);
    public string SelectionDescription => Translations.Get(SelectionMode switch
    {
        MediaSelectionMode.LatestSession => "Media.Selection.Description.LatestSession",
        MediaSelectionMode.PreferredApplication => "Media.Selection.Description.PreferredApplication",
        _ => "Media.Selection.Description.Automatic"
    });

    /// <summary>
    /// 快速启动列表下方的状态行。存的是文案键而不是已经拼好的句子：语言变化后这一行必须跟着换语言，
    /// 而拼好的句子只会在旧语言上停留，直到用户再点一次按钮。
    /// The status line under the quick launch list. It holds a text key rather than a finished sentence: the line has to
    /// follow a language change, while a finished sentence would stay in the old language until the next click.
    /// </summary>
    public string StatusText => _statusKey is null ? string.Empty : Translations.Get(_statusKey);

    public ExtraFeaturesViewModel(
        MediaSourceRegistry sources,
        MediaSessionService mediaSessions,
        IDisplayMonitorService displayMonitorService,
        LocalizationService localization, QuickLaunchService quickLaunch)
    {
        _quickLaunch = quickLaunch;
        _sources = sources;
        _mediaSessions = mediaSessions;
        _displayMonitorService = displayMonitorService;
        _localization = localization;
        _mediaSessions.DiscoveredSourcesChanged += OnDiscoveredSourcesChanged;
        SettingsManager.SettingsChanged += OnSettingsChanged;
        _displayMonitorService.MonitorsChanged += OnMonitorsChanged;

        _statusTimer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher) { Interval = StatusDisplayDuration };
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            _statusKey = null;
            OnPropertyChanged(nameof(StatusText));
        };

        // 本页是单例，订阅与进程同寿命，不需要在关闭时取消。
        // This page is a singleton, so the subscription lives as long as the process and needs no unsubscription.
        _localization.LanguageChanged += OnLanguageChanged;

        RefreshAll();
    }

    /// <summary>
    /// 语言变化后重取本页在代码里产出的文案。
    ///
    /// 空的属性名让 WPF 重读全部绑定（带单位的读数、状态行都在其中），显示器名称则要先重建一次：它由本页拼出
    /// 「N. 名称（主显示器）」，绑定的列表实例本身没变，光重读会拿到旧语言拼好的字符串。
    /// Re-reads the text this page produces in code after a language change.
    ///
    /// The empty property name makes WPF re-read every binding, which covers the readings with units and the status line.
    /// The monitor names are rebuilt first: this page composes "N. name (primary)" and the bound list instance itself does
    /// not change, so re-reading alone would hand back strings composed in the previous language.
    /// </summary>
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshSources();
        RefreshMonitors();
        OnPropertyChanged(string.Empty);
    }

    public bool SmtcFilterEnabled
    {
        get => SettingsManager.Current.SmtcSourceFilter.Enabled;
        set
        {
            if (_isRefreshing || value == SmtcFilterEnabled) return;
            SettingsManager.SetSmtcSourceFilterSettings(SettingsManager.Current.SmtcSourceFilter with { Enabled = value });
            OnPropertyChanged();
        }
    }

    public bool TrackChangeNotificationEnabled { get => Notification.Enabled; set => UpdateNotification(Notification with { Enabled = value }); }
    public bool ShowTrackChangeNotificationWhenFullscreen { get => Notification.ShowWhenFullscreen; set => UpdateNotification(Notification with { ShowWhenFullscreen = value }); }
    public int TrackChangeNotificationDurationSeconds { get => Notification.DurationMilliseconds / 1000; set => UpdateNotification(Notification with { DurationMilliseconds = value * 1000 }); }

    /// <summary>Notification duration bounds in the display unit, derived from the persistence contract.</summary>
    public double MinimumTrackChangeNotificationDurationSeconds => TrackChangeNotificationSettings.MinimumDurationMilliseconds / 1000d;
    /// <summary>Notification duration upper bound in seconds.</summary>
    public double MaximumTrackChangeNotificationDurationSeconds => TrackChangeNotificationSettings.MaximumDurationMilliseconds / 1000d;

    /// <summary>停留时间滑杆旁的读数，带单位。/ The reading next to the duration slider, with its unit.</summary>
    public string TrackChangeNotificationDurationText => Translations.Format("Media.Notification.Duration.Value", TrackChangeNotificationDurationSeconds);

    public TrackChangeNotificationPosition TrackChangeNotificationPosition { get => Notification.Position; set => UpdateNotification(Notification with { Position = value }); }
    public NotificationTargetMode TrackChangeNotificationTargetMode { get => Notification.TargetMode; set => UpdateNotification(Notification with { TargetMode = value }); }
    public string? TrackChangeNotificationFixedMonitorDeviceId { get => Notification.FixedMonitorDeviceId ?? _displayMonitorService.ResolveFixedMonitor(null)?.DeviceId; set => UpdateNotification(Notification with { FixedMonitorDeviceId = value }); }
    public bool CanSelectTrackChangeNotificationMonitor => TrackChangeNotificationEnabled && TrackChangeNotificationTargetMode == NotificationTargetMode.Fixed;

    private TrackChangeNotificationSettings Notification => SettingsManager.Current.TrackChangeNotification;

    [RelayCommand]
    private void AddDetectedSource(MediaSourceSettingItem? item)
    {
        if (item?.Descriptor is not { CanQuickLaunch: true } descriptor || descriptor.LaunchKind is not { } kind || descriptor.LaunchTarget is null)
        {
            SetStatus("Media.Status.SourceLaunchUnresolved");
            return;
        }
        AddQuickLaunch(new QuickLaunchEntry(Guid.NewGuid().ToString("N"), descriptor.DisplayName, kind, descriptor.LaunchTarget, descriptor.SourceId));
    }

    public async Task AddQuickLaunchFileAsync(string path, MediaSourceDescriptor? source = null)
    {
        try { SetStatus(await _quickLaunch.AddFileAsync(path, source, CancellationToken.None)); }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
            SetStatus("Media.Status.OnlyExistingFiles");
        }
    }

    public void ResetExtraFeatures() => SettingsManager.ResetExtraFeatures();

    /// <summary>
    /// 写状态行。参数是文案键而不是拼好的句子，因此语言变化后这一行会自己跟着换语言。
    /// Writes the status line. The argument is a text key rather than a finished sentence, so the line follows a language
    /// change on its own.
    /// </summary>
    /// <param name="key">状态文案的键；传空表示立即清除。/ Key of the status text; pass null to clear it at once.</param>
    private void SetStatus(string? key)
    {
        _statusKey = key;
        OnPropertyChanged(nameof(StatusText));
        // 每次写入都重新计时：连续操作时状态行从最后一次开始数，不会在中途消失。
        // Every write restarts the countdown, so a burst of operations keeps the line until the last one lands.
        _statusTimer.Stop();
        if (key is not null) _statusTimer.Start();
    }

    private void AddQuickLaunch(QuickLaunchEntry entry)
    {
        SetStatus(_quickLaunch.Add(entry) ? "Media.Status.AddedToQuickLaunch" : "Media.Status.AlreadyInQuickLaunch");
    }

    private void UpdateNotification(TrackChangeNotificationSettings settings)
    {
        SettingsManager.SetTrackChangeNotificationSettings(settings);
        OnPropertyChanged(nameof(TrackChangeNotificationEnabled));
        OnPropertyChanged(nameof(ShowTrackChangeNotificationWhenFullscreen));
        OnPropertyChanged(nameof(TrackChangeNotificationDurationSeconds));
        OnPropertyChanged(nameof(TrackChangeNotificationDurationText));
        OnPropertyChanged(nameof(TrackChangeNotificationPosition));
        OnPropertyChanged(nameof(TrackChangeNotificationTargetMode));
        OnPropertyChanged(nameof(TrackChangeNotificationFixedMonitorDeviceId));
        OnPropertyChanged(nameof(CanSelectTrackChangeNotificationMonitor));
    }

    private void OnDiscoveredSourcesChanged(IReadOnlyList<MediaSourceDescriptor> sources) => DispatcherHelper.Run(_dispatcher, RefreshSources);
    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e) => DispatcherHelper.Run(_dispatcher, RefreshAll);
    private void OnMonitorsChanged(object? sender, EventArgs e) => DispatcherHelper.Run(_dispatcher, RefreshMonitors);

    private void RefreshAll()
    {
        _isRefreshing = true;
        try
        {
            RefreshSources();
            RefreshMonitors();
            OnPropertyChanged(nameof(SmtcFilterEnabled));
            OnPropertyChanged(nameof(SelectionMode));
            OnPropertyChanged(nameof(IsPreferredApplication));
            OnPropertyChanged(nameof(SelectionDescription));
            UpdateNotification(Notification);
        }
        finally { _isRefreshing = false; }
    }

    private void RefreshSources()
    {
        var wasRefreshing = _isRefreshing;
        _isRefreshing = true;
        try
        {
            var allowed = (SettingsManager.Current.SmtcSourceFilter.AllowedSourceIds ?? [])
                .Select(_sources.NormalizeSourceId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var descriptors = _mediaSessions.CurrentDiscoveredSources.ToDictionary(source => source.SourceId, StringComparer.OrdinalIgnoreCase);
            foreach (var sourceId in allowed)
                descriptors.TryAdd(sourceId, new MediaSourceDescriptor(sourceId, MediaSourceNameFormatter.GetDisplayName(sourceId, sourceId), null, null));
            if (!string.IsNullOrEmpty(PreferredSourceId))
                descriptors.TryAdd(PreferredSourceId, new MediaSourceDescriptor(PreferredSourceId,
                    MediaSourceNameFormatter.GetDisplayName(PreferredSourceId, PreferredSourceId), null, null));

            PreferredSourceOptions = new[] { new MediaSourceDescriptor(string.Empty, Translations.Get("Media.Selection.NoPreferred"), null, null) }
                .Concat(descriptors.Values.OrderBy(value => value.DisplayName, StringComparer.CurrentCultureIgnoreCase)).ToArray();
            OnPropertyChanged(nameof(PreferredSourceOptions));
            OnPropertyChanged(nameof(PreferredSourceId));
            OnPropertyChanged(nameof(IsPreferredSourceBlocked));

            Sources.Clear();
            foreach (var descriptor in descriptors.Values.OrderBy(value => value.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            {
                var item = new MediaSourceSettingItem(descriptor, allowed.Contains(descriptor.SourceId, StringComparer.OrdinalIgnoreCase));
                item.AllowedChanged += OnSourceAllowedChanged;
                Sources.Add(item);
            }
        }
        finally { _isRefreshing = wasRefreshing; }
    }

    private void OnSourceAllowedChanged(MediaSourceSettingItem item)
    {
        var allowed = Sources.Where(source => source.IsAllowed).Select(source => source.Descriptor.SourceId).ToArray();
        SettingsManager.SetSmtcSourceFilterSettings(SettingsManager.Current.SmtcSourceFilter with { AllowedSourceIds = allowed });
    }

    private void RefreshMonitors()
    {
        // 显示器名称带「主显示器」后缀，后缀是一句要看给人看的文案，因此按当前语言取一次；它由显示模式页与本页共用
        // （键在 CommonStrings 里），语言变化后由 OnLanguageChanged 再调用本方法重建。
        // The monitor name carries a "primary" suffix, which is text shown to a person, so it is read in the active
        // language; the suffix is shared with the display-mode page (its key lives in CommonStrings) and a language change
        // calls this method again through OnLanguageChanged.
        MonitorOptions = _displayMonitorService.GetMonitors().Select((monitor, index) => new DisplayMonitorOption(
            monitor.DeviceId,
            $"{index + 1}. {monitor.DeviceName}{(monitor.IsPrimary ? Translations.Get("Common.Monitor.PrimarySuffix") : string.Empty)}",
            monitor.IsPrimary)).ToArray();
        OnPropertyChanged(nameof(MonitorOptions));
    }


}

/// <summary>设置页中的可选媒体来源。 / Selectable media source shown on the settings page.</summary>
public partial class MediaSourceSettingItem : ObservableObject
{
    public MediaSourceDescriptor Descriptor { get; }
    [ObservableProperty] private bool _isAllowed;
    public event Action<MediaSourceSettingItem>? AllowedChanged;

    public MediaSourceSettingItem(MediaSourceDescriptor descriptor, bool isAllowed)
    {
        Descriptor = descriptor;
        _isAllowed = isAllowed;
    }

    partial void OnIsAllowedChanged(bool value) => AllowedChanged?.Invoke(this);
}
