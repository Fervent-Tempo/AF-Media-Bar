using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Resources;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>媒体来源、快捷启动与曲目通知设置，不拥有频谱和性能参数。</summary>
public partial class ExtraFeaturesViewModel : ObservableObject
{
    private readonly MediaSourceRegistry _sources;
    private readonly MediaSessionService _mediaSessions;
    private readonly IDisplayMonitorService _displayMonitorService;
    private readonly LocalizationService _localization;

    // 设置写入会同步通知订阅者，而写设置的那一方可能在后台线程上（更新检查的网络等待之后就是如此）：
    // 本页的 Sources / QuickLaunchEntries 绑定到界面，跨线程改它们会让 WPF 的 CollectionView 抛异常，
    // 因此刷新 MUST 回到 UI 线程。根因侧同样已修（`UpdateService.WriteUpdateSettings`），这里是第二道防线。
    // A settings write notifies its subscribers synchronously, and whoever writes may be on a background thread (that is exactly
    // what happens after the update check's network wait): this page's Sources and QuickLaunchEntries are bound to the interface,
    // and mutating them across threads makes WPF's CollectionView throw, so the refresh MUST come back to the UI thread. The root
    // cause is fixed as well (`UpdateService.WriteUpdateSettings`); this is the second line of defence.
    private readonly Dispatcher _dispatcher = DispatcherHelper.Current;

    private bool _isRefreshing;
    private string? _statusKey;

    public ObservableCollection<MediaSourceSettingItem> Sources { get; } = [];
    public ObservableCollection<QuickLaunchEntry> QuickLaunchEntries { get; } = [];
    public IReadOnlyList<DisplayMonitorOption> MonitorOptions { get; private set; } = [];

    /// <summary>快速启动条目数，显示在「已添加的启动项」行里；单位词属于文案，因此读数由代码拼。/ Number of quick launch entries, shown in the "added entries" row; the unit word is text, so the reading is composed in code.</summary>
    public string QuickLaunchEntryCountText => Translations.Format("Media.QuickLaunch.EntryCount", QuickLaunchEntries.Count);

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
        LocalizationService localization)
    {
        _sources = sources;
        _mediaSessions = mediaSessions;
        _displayMonitorService = displayMonitorService;
        _localization = localization;
        _mediaSessions.DiscoveredSourcesChanged += OnDiscoveredSourcesChanged;
        SettingsManager.SettingsChanged += OnSettingsChanged;
        _displayMonitorService.MonitorsChanged += OnMonitorsChanged;

        // 本页是单例，订阅与进程同寿命，不需要在关闭时取消。
        // This page is a singleton, so the subscription lives as long as the process and needs no unsubscription.
        _localization.LanguageChanged += OnLanguageChanged;

        // 条目数显示在行内，增删后读数必须重算；绑定落在这个属性上而不是 Count 上，因为单位词属于文案。
        // The entry count is shown in a row and has to be recomputed when entries are added or removed; the binding targets
        // this property rather than Count because the unit word is text.
        QuickLaunchEntries.CollectionChanged += (_, _) => OnPropertyChanged(nameof(QuickLaunchEntryCountText));

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

    [RelayCommand]
    private void RemoveQuickLaunch(QuickLaunchEntry? entry)
    {
        if (entry is null) return;
        SaveQuickLaunch(QuickLaunchEntries.Where(candidate => candidate.Id != entry.Id));
    }

    [RelayCommand]
    private void MoveQuickLaunchUp(QuickLaunchEntry? entry) => Move(entry, -1);

    [RelayCommand]
    private void MoveQuickLaunchDown(QuickLaunchEntry? entry) => Move(entry, 1);

    public void AddQuickLaunchFile(string path)
    {
        var extension = Path.GetExtension(path);
        var kind = string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase)
            ? QuickLaunchTargetKind.Executable
            : string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase)
                ? QuickLaunchTargetKind.Shortcut
                : (QuickLaunchTargetKind?)null;
        if (kind is null || !File.Exists(path))
        {
            SetStatus("Media.Status.OnlyExistingFiles");
            return;
        }
        AddQuickLaunch(new QuickLaunchEntry(Guid.NewGuid().ToString("N"), Path.GetFileNameWithoutExtension(path), kind.Value, Path.GetFullPath(path)));
    }

    public void ResetExtraFeatures() => SettingsManager.ResetExtraFeatures();

    /// <summary>
    /// 写状态行。参数是文案键而不是拼好的句子，因此语言变化后这一行会自己跟着换语言。
    /// Writes the status line. The argument is a text key rather than a finished sentence, so the line follows a language
    /// change on its own.
    /// </summary>
    /// <param name="key">状态文案的键。/ Key of the status text.</param>
    private void SetStatus(string key)
    {
        _statusKey = key;
        OnPropertyChanged(nameof(StatusText));
    }

    private void AddQuickLaunch(QuickLaunchEntry entry)
    {
        var before = SettingsManager.Current.QuickLaunch.Entries?.Count ?? 0;
        SaveQuickLaunch(QuickLaunchEntries.Append(entry));
        SetStatus((SettingsManager.Current.QuickLaunch.Entries?.Count ?? 0) == before
            ? "Media.Status.AlreadyInQuickLaunch"
            : "Media.Status.AddedToQuickLaunch");
    }

    private void Move(QuickLaunchEntry? entry, int offset)
    {
        if (entry is null) return;
        var items = QuickLaunchEntries.ToList();
        var index = items.FindIndex(candidate => candidate.Id == entry.Id);
        if (index < 0) return;
        var target = index + offset;
        if (target < 0 || target >= items.Count)
        {
            // 以前这里直接返回，按钮看起来像坏了。现在把原因说出来，用户才知道是到底了而不是没生效。
            // This used to return silently and the button read as broken. Saying why tells the user the list
            // end was reached rather than that the click did nothing.
            SetStatus(offset < 0 ? "Media.Status.AlreadyFirst" : "Media.Status.AlreadyLast");
            return;
        }

        (items[index], items[target]) = (items[target], items[index]);
        SaveQuickLaunch(items);
    }

    private void SaveQuickLaunch(IEnumerable<QuickLaunchEntry> entries) =>
        SettingsManager.SetQuickLaunchSettings(new QuickLaunchSettings(entries.ToArray()));

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

    private void OnDiscoveredSourcesChanged(IReadOnlyList<MediaSourceDescriptor> sources) => RefreshSources();
    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e) => DispatcherHelper.Run(_dispatcher, RefreshAll);
    private void OnMonitorsChanged(object? sender, EventArgs e) => RefreshMonitors();

    private void RefreshAll()
    {
        _isRefreshing = true;
        try
        {
            RefreshSources();
            QuickLaunchEntries.Clear();
            foreach (var entry in SettingsManager.Current.QuickLaunch.Entries ?? []) QuickLaunchEntries.Add(entry);
            RefreshMonitors();
            OnPropertyChanged(nameof(SmtcFilterEnabled));
            UpdateNotification(Notification);
        }
        finally { _isRefreshing = false; }
    }

    private void RefreshSources()
    {
        var allowed = (SettingsManager.Current.SmtcSourceFilter.AllowedSourceIds ?? [])
            .Select(_sources.NormalizeSourceId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var descriptors = _mediaSessions.CurrentDiscoveredSources.ToDictionary(source => source.SourceId, StringComparer.OrdinalIgnoreCase);
        foreach (var sourceId in allowed)
            descriptors.TryAdd(sourceId, new MediaSourceDescriptor(sourceId, MediaSourceNameFormatter.GetDisplayName(sourceId, sourceId), null, null));

        Sources.Clear();
        foreach (var descriptor in descriptors.Values.OrderBy(value => value.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            var item = new MediaSourceSettingItem(descriptor, allowed.Contains(descriptor.SourceId, StringComparer.OrdinalIgnoreCase));
            item.AllowedChanged += OnSourceAllowedChanged;
            Sources.Add(item);
        }
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
