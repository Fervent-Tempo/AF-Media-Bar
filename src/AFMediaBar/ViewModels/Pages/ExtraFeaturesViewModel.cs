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

/// <summary>额外功能页的来源、快捷启动、频谱、性能和通知设置。 / Settings for sources, launchers, spectrum, metrics, and notifications.</summary>
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

    public int SpectrumBandCount
    {
        get => SettingsManager.Current.SpectrumComponent.BandCount;
        set { SettingsManager.SetSpectrumComponentSettings(SettingsManager.Current.SpectrumComponent with { BandCount = value }); OnPropertyChanged(); OnPropertyChanged(nameof(SpectrumBandCountText)); }
    }

    /// <summary>柱数滑杆旁的读数。/ The reading next to the bar-count slider.</summary>
    public string SpectrumBandCountText => Translations.Format("Media.Spectrum.BandCount.Value", SpectrumBandCount);

    public int SpectrumRefreshRateHz
    {
        get => SettingsManager.Current.SpectrumComponent.RefreshRateHz;
        set { SettingsManager.SetSpectrumComponentSettings(SettingsManager.Current.SpectrumComponent with { RefreshRateHz = value }); OnPropertyChanged(); }
    }

    public int SpectrumSensitivityPercent
    {
        get => SettingsManager.Current.SpectrumComponent.SensitivityPercent;
        set { SettingsManager.SetSpectrumComponentSettings(SettingsManager.Current.SpectrumComponent with { SensitivityPercent = value }); OnPropertyChanged(); }
    }

    /// <summary>
    /// 频谱呈现样式。柱数决定频谱占用宽度，样式只决定这些宽度怎么画，因此两者互不影响。
    /// Spectrum presentation style. The bar count decides the width the spectrum occupies and the style only decides how that
    /// width is painted, so neither interferes with the other.
    /// </summary>
    public SpectrumStyle SpectrumStyle
    {
        get => SettingsManager.Current.SpectrumComponent.Style;
        set { SettingsManager.SetSpectrumComponentSettings(SettingsManager.Current.SpectrumComponent with { Style = value }); OnPropertyChanged(); }
    }

    /// <summary>
    /// 频谱内容区的横轴尺寸（横向任务栏就是高度）。它只影响柱子能长多高，宽度仍由柱数决定。
    /// Cross-axis size of the spectrum content area, which is the height on a horizontal taskbar. It only decides how tall the bars can grow;
    /// the width still follows the bar count.
    /// </summary>
    public double SpectrumContentHeightDip
    {
        get => SpectrumComponentSettings.SnapContentHeightDip(SettingsManager.Current.SpectrumComponent.ContentHeightDip);
        set
        {
            var dip = SpectrumComponentSettings.SnapContentHeightDip(value);
            SettingsManager.SetSpectrumComponentSettings(SettingsManager.Current.SpectrumComponent with { ContentHeightDip = dip });
            OnPropertyChanged();
            OnPropertyChanged(nameof(SpectrumContentHeightText));
        }
    }

    /// <summary>频谱尺寸滑杆旁的读数。/ The reading next to the spectrum-size slider.</summary>
    public string SpectrumContentHeightText => Translations.Format("Media.Spectrum.ContentHeight.Value", SpectrumContentHeightDip);

    /// <summary>频率尺寸滑杆的下限，来自持久化常量而不是界面字面量。 / Lower bound of the spectrum-size slider, taken from the persistence constant rather than a UI literal.</summary>
    public double MinimumSpectrumContentHeightDip => SpectrumComponentSettings.MinimumContentHeightDip;

    /// <inheritdoc cref="MinimumSpectrumContentHeightDip" />
    public double MaximumSpectrumContentHeightDip => SpectrumComponentSettings.MaximumContentHeightDip;

    /// <inheritdoc cref="MinimumSpectrumContentHeightDip" />
    public double SpectrumContentHeightStepDip => SpectrumComponentSettings.ContentHeightStepDip;

    /// <summary>柱数滑杆的下限，来自持久化常量而不是界面字面量。 / Lower bound of the bar-count slider, taken from the persistence constant rather than a UI literal.</summary>
    public int MinimumSpectrumBandCount => SpectrumComponentSettings.MinimumBandCount;

    /// <inheritdoc cref="MinimumSpectrumBandCount" />
    public int MaximumSpectrumBandCount => SpectrumComponentSettings.MaximumBandCount;

    /// <summary>灵敏度滑杆的下限，来自持久化常量而不是界面字面量。 / Lower bound of the sensitivity slider, taken from the persistence constant rather than a UI literal.</summary>
    public int MinimumSpectrumSensitivityPercent => SpectrumComponentSettings.MinimumSensitivityPercent;

    /// <inheritdoc cref="MinimumSpectrumBandCount" />
    public int MaximumSpectrumSensitivityPercent => SpectrumComponentSettings.MaximumSensitivityPercent;

    /// <inheritdoc cref="MinimumSpectrumBandCount" />
    public int SpectrumSensitivityStepPercent => SpectrumComponentSettings.SensitivityStepPercent;

    /// <summary>
    /// 性能组件的采样间隔，界面以秒为单位。设置里存的仍是毫秒，写入前吸附到滑杆步长上，
    /// 因此读数与滑杆位置永远一致。
    /// Sampling interval of the performance component, expressed in seconds for the interface. The stored value stays in
    /// milliseconds and is snapped onto the slider step before it is written, so the reading and the slider position always
    /// agree.
    /// </summary>
    public double PerformanceRefreshIntervalSeconds
    {
        get => SettingsManager.Current.PerformanceComponent.RefreshIntervalMilliseconds / 1000d;
        set
        {
            var milliseconds = PerformanceComponentSettings.SnapRefreshIntervalMilliseconds((int)Math.Round(value * 1000));
            SettingsManager.SetPerformanceComponentSettings(
                SettingsManager.Current.PerformanceComponent with { RefreshIntervalMilliseconds = milliseconds });
            OnPropertyChanged();
            OnPropertyChanged(nameof(PerformanceRefreshIntervalText));
        }
    }

    /// <summary>采样间隔滑杆旁的读数，带单位。/ The reading next to the sampling-interval slider, with its unit.</summary>
    public string PerformanceRefreshIntervalText => Translations.Format("Media.Performance.RefreshInterval.Value", PerformanceRefreshIntervalSeconds);

    /// <summary>采样间隔滑杆的下限（秒）。 / Lower bound of the sampling-interval slider, in seconds.</summary>
    public double MinimumPerformanceRefreshIntervalSeconds => PerformanceComponentSettings.MinimumRefreshIntervalMilliseconds / 1000d;

    /// <inheritdoc cref="MinimumPerformanceRefreshIntervalSeconds" />
    public double MaximumPerformanceRefreshIntervalSeconds => PerformanceComponentSettings.MaximumRefreshIntervalMilliseconds / 1000d;

    /// <inheritdoc cref="MinimumPerformanceRefreshIntervalSeconds" />
    public double PerformanceRefreshIntervalStepSeconds => PerformanceComponentSettings.RefreshIntervalStepMilliseconds / 1000d;

    public bool OpenTaskManagerOnMetricsClick
    {
        get => SettingsManager.Current.PerformanceComponent.OpenTaskManagerOnClick;
        set { SettingsManager.SetPerformanceComponentSettings(SettingsManager.Current.PerformanceComponent with { OpenTaskManagerOnClick = value }); OnPropertyChanged(); }
    }

    public bool ShowSystemMemory { get => HasMetric(MetricKind.SystemMemory); set => SetMetric(MetricKind.SystemMemory, value); }
    public bool ShowSystemCpu { get => HasMetric(MetricKind.SystemCpu); set => SetMetric(MetricKind.SystemCpu, value); }
    public bool ShowSystemGpu { get => HasMetric(MetricKind.SystemGpu); set => SetMetric(MetricKind.SystemGpu, value); }
    public bool ShowProcessMemory { get => HasMetric(MetricKind.ProcessMemory); set => SetMetric(MetricKind.ProcessMemory, value); }

    /// <summary>
    /// 该指标当前能否取消勾选。性能组件至少需要一个指标，因此最后一个勾选项的复选框必须禁用；
    /// 原实现只是静默忽略取消操作、复选框却照常可点，用户会以为界面失灵。
    /// Whether a metric may currently be unchecked. The performance component needs at least one metric, so the
    /// last checked box must be disabled; the previous behaviour silently ignored the click while leaving the
    /// box enabled, which read as a broken control.
    /// </summary>
    public bool CanUncheckSystemMemory => CanUncheck(MetricKind.SystemMemory);

    /// <inheritdoc cref="CanUncheckSystemMemory" />
    public bool CanUncheckSystemCpu => CanUncheck(MetricKind.SystemCpu);

    /// <inheritdoc cref="CanUncheckSystemMemory" />
    public bool CanUncheckSystemGpu => CanUncheck(MetricKind.SystemGpu);

    /// <inheritdoc cref="CanUncheckSystemMemory" />
    public bool CanUncheckProcessMemory => CanUncheck(MetricKind.ProcessMemory);

    /// <summary>
    /// 频谱组件是否显示在静置层。它和参数放在同一页，这样“这个组件要不要用”和“它怎么表现”
    /// 不会分处两个页面。
    /// Whether the spectrum component shows on the rest layer. It lives on the same page as its parameters so
    /// "should this component exist" and "how does it behave" are never split across two pages.
    /// </summary>
    public bool SpectrumVisible
    {
        get => SettingsManager.Current.TaskbarExperience.SpectrumVisible;
        set
        {
            SettingsManager.SetTaskbarExperienceSettings(
                SettingsManager.Current.TaskbarExperience with { SpectrumVisible = value });
            OnPropertyChanged();
        }
    }

    /// <inheritdoc cref="SpectrumVisible" />
    public bool PerformanceVisible
    {
        get => SettingsManager.Current.TaskbarExperience.PerformanceVisible;
        set
        {
            SettingsManager.SetTaskbarExperienceSettings(
                SettingsManager.Current.TaskbarExperience with { PerformanceVisible = value });
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
    public void ResetComponents() => SettingsManager.ResetComponents();

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

    private bool HasMetric(MetricKind metric) => SettingsManager.Current.PerformanceComponent.Metrics!.Contains(metric);

    private bool CanUncheck(MetricKind metric) =>
        !HasMetric(metric) || SettingsManager.Current.PerformanceComponent.Metrics!.Count > 1;

    private void SetMetric(MetricKind metric, bool enabled)
    {
        var metrics = SettingsManager.Current.PerformanceComponent.Metrics!.ToList();
        if (enabled && !metrics.Contains(metric)) metrics.Add(metric);
        if (!enabled && metrics.Count > 1) metrics.Remove(metric);
        SettingsManager.SetPerformanceComponentSettings(SettingsManager.Current.PerformanceComponent with { Metrics = metrics });
        RaiseMetricProperties();
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
            OnPropertyChanged(nameof(SpectrumBandCount));
            OnPropertyChanged(nameof(SpectrumBandCountText));
            OnPropertyChanged(nameof(SpectrumRefreshRateHz));
            OnPropertyChanged(nameof(SpectrumSensitivityPercent));
            OnPropertyChanged(nameof(SpectrumStyle));
            OnPropertyChanged(nameof(PerformanceRefreshIntervalSeconds));
            OnPropertyChanged(nameof(PerformanceRefreshIntervalText));
            OnPropertyChanged(nameof(OpenTaskManagerOnMetricsClick));
            OnPropertyChanged(nameof(SpectrumVisible));
            OnPropertyChanged(nameof(PerformanceVisible));
            RaiseMetricProperties();
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

    private void RaiseMetricProperties()
    {
        OnPropertyChanged(nameof(ShowSystemMemory));
        OnPropertyChanged(nameof(ShowSystemCpu));
        OnPropertyChanged(nameof(ShowSystemGpu));
        OnPropertyChanged(nameof(ShowProcessMemory));
        OnPropertyChanged(nameof(CanUncheckSystemMemory));
        OnPropertyChanged(nameof(CanUncheckSystemCpu));
        OnPropertyChanged(nameof(CanUncheckSystemGpu));
        OnPropertyChanged(nameof(CanUncheckProcessMemory));
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
