using System.Collections.ObjectModel;
using System.Windows.Threading;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>歌词呈现、取词来源与对齐设置。 / Lyric presentation, retrieval sources, and alignment settings.</summary>
public partial class LyricsViewModel : ObservableObject
{
    private readonly LocalizationService _localization;

    // 与 ExtraFeaturesViewModel 同理：设置写入可能来自后台线程，而来源列表与第二行列表绑定到界面，
    // 跨线程改它们会被 WPF 的 CollectionView 拒绝，因此重建 MUST 回到 UI 线程。
    // For the same reason as in ExtraFeaturesViewModel: a settings write can come from a background thread, while the source list
    // and the second-line list are bound to the interface, and mutating them across threads is refused by WPF's CollectionView, so
    // rebuilding MUST come back to the UI thread.
    private readonly Dispatcher _dispatcher = DispatcherHelper.Current;
    private bool _isRefreshing;

    /// <summary>
    /// 本视图模型正在写来源设置：写回会同步触发设置变更事件，若此时重建列表，用户刚点的那一行会被换掉（焦点丢失、行闪一下）。
    /// This view model is writing the source settings: the write publishes a settings change synchronously, and rebuilding the list
    /// then would replace the very row the user just clicked (focus lost, the row flickers).
    /// </summary>
    private bool _isSavingSources;

    /// <summary>
    /// 创建歌词页视图模型。
    /// Creates the lyrics page view model.
    /// </summary>
    /// <param name="localization">语言服务，用于在语言变化时重建来源显示名 / Localization service, used to rebuild source display names after a language change.</param>
    public LyricsViewModel(LocalizationService localization)
    {
        _localization = localization;
        SettingsManager.SettingsChanged += OnSettingsChanged;
        _localization.LanguageChanged += OnLanguageChanged;
        RefreshSourceEntries();
        RefreshSecondaryLineEntries();
    }

    public bool LyricsEnabled { get => SettingsManager.Current.LyricsEnabled; set { SettingsManager.SetLyricsEnabled(value); RaiseAll(); } }
    public bool TwoLineLyricsEnabled { get => SettingsManager.Current.TwoLineLyricsEnabled; set { SettingsManager.SetTwoLineLyricsEnabled(value); RaiseAll(); } }
    /// <summary>第二行顺序的可读描述（"下一句 → 翻译 → 音译"），与列表内容同步更新。/ A readable description of the second-line order ("next line, translation, romanization"), kept in step with the list.</summary>
    public string SecondaryLineOrderText => string.Join(
        " → ",
        SecondaryLineEntries.Select(entry => entry.DisplayName));

    /// <summary>
    /// 第二行歌词的来源顺序（列表顺序即优先级）。界面用带上下移按钮的列表调整它，与来源列表同一套写法。
    /// Source order of the second lyric line, where the list order is the priority. The interface adjusts it with a list carrying per-row move
    /// buttons, exactly like the source list.
    /// </summary>
    public ObservableCollection<LyricsSecondaryLineSettingItem> SecondaryLineEntries { get; } = [];
    public LyricsTextAlignment TextAlignment { get => SettingsManager.Current.LyricsTextAlignment; set { SettingsManager.SetLyricsTextAlignment(value); OnPropertyChanged(); } }

    /// <summary>是否启用逐字擦亮。/ Whether syllable highlighting is enabled.</summary>
    public bool SyllableHighlightEnabled
    {
        get => SettingsManager.Current.LyricsSyllableHighlightEnabled;
        set { SettingsManager.SetLyricsSyllableHighlightEnabled(value); RaiseAll(); }
    }

    /// <summary>启用逐字擦亮时底色层（未唱部分）的不透明度百分比。/ Opacity percentage of the base (unsung) layer while highlighting is on.</summary>
    public int UnsungOpacityPercent
    {
        get => SettingsManager.Current.LyricsUnsungOpacityPercent;
        set
        {
            SettingsManager.SetLyricsUnsungOpacityPercent(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(UnsungOpacityText));
        }
    }

    /// <summary>不透明度的读数文本。/ The opacity readout text.</summary>
    public string UnsungOpacityText => $"{UnsungOpacityPercent}%";

    /// <summary>是否丢弃作者、作曲、制作等信息行。/ Whether credit lines are dropped.</summary>
    public bool InfoLineFilterEnabled
    {
        get => SettingsManager.Current.LyricsInfoLineFilterEnabled;
        set { SettingsManager.SetLyricsInfoLineFilterEnabled(value); OnPropertyChanged(); }
    }

    /// <summary>搜索型来源的匹配严格度。/ Match strictness for search-based sources.</summary>
    public LyricsMatchStrictness MatchStrictness
    {
        get => SettingsManager.Current.LyricsMatchStrictness;
        set { SettingsManager.SetLyricsMatchStrictness(value); OnPropertyChanged(); }
    }

    /// <summary>并发取词的结果采纳策略。/ The adoption mode for concurrent retrieval.</summary>
    public LyricsAdoptionMode AdoptionMode
    {
        get => SettingsManager.Current.LyricsAdoptionMode;
        set { SettingsManager.SetLyricsAdoptionMode(value); OnPropertyChanged(); }
    }

    /// <summary>优先级来源每批并发的个数。/ How many priority sources run concurrently per batch.</summary>
    public int ConcurrencyBatchSize
    {
        get => SettingsManager.Current.LyricsConcurrencyBatchSize;
        set
        {
            SettingsManager.SetLyricsConcurrencyBatchSize(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ConcurrencyBatchSizeText));
        }
    }

    /// <summary>批次数的读数文本。/ The batch-size readout text.</summary>
    public string ConcurrencyBatchSizeText => $"{ConcurrencyBatchSize}";

    /// <summary>候补出现后留给默认接口的倒计时（毫秒）。/ Countdown (milliseconds) left to the default interface once a candidate exists.</summary>
    public int AdoptionDeadlineMilliseconds
    {
        get => SettingsManager.Current.LyricsAdoptionDeadlineMilliseconds;
        set
        {
            SettingsManager.SetLyricsAdoptionDeadlineMilliseconds(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(AdoptionDeadlineText));
        }
    }

    /// <summary>倒计时的读数文本（秒）。/ The deadline readout text in seconds.</summary>
    public string AdoptionDeadlineText => $"{AdoptionDeadlineMilliseconds / 1000.0:0.#} s";

    /// <summary>取词来源列表，顺序即优先级。/ The retrieval source list, whose order is the priority.</summary>
    public ObservableCollection<LyricsSourceSettingItem> SourceEntries { get; } = [];

    /// <summary>是否一个来源都没启用：此时不会请求任何歌词服务，页面用提示条说明后果。
    /// Whether no source is enabled: no lyric service is contacted then, and a callout on the page states that consequence.</summary>
    public bool IsEverySourceDisabled => SourceEntries.Count > 0 && SourceEntries.All(entry => !entry.IsEnabled);

    public bool CanConfigureTwoLine => LyricsEnabled;
    public bool CanConfigureSecondary => LyricsEnabled && TwoLineLyricsEnabled;

    /// <summary>未唱部分不透明度只在启用逐字擦亮时可用：没有擦亮时它没有作用对象。/ The unsung opacity only applies while highlighting is on.</summary>
    public bool CanConfigureUnsungOpacity => SyllableHighlightEnabled;

    public void ResetLyrics() => SettingsManager.ResetLyrics();

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        // 重建绑定列表的动作全部回到 UI 线程（写设置的一方可能在后台线程上）。
        // Everything that rebuilds a bound list goes back to the UI thread (whoever writes the settings may be on a background one).
        if (!_dispatcher.CheckAccess())
        {
            DispatcherHelper.Run(_dispatcher, () => OnSettingsChanged(sender, e));
            return;
        }

        if (e.ResetScope is SettingsResetScope.Lyrics or SettingsResetScope.All)
        {
            RefreshSourceEntries();
            RefreshSecondaryLineEntries();
            RaiseAll();
            return;
        }

        if (e.PropertyName == nameof(AppSettings.LyricsSource) && !_isSavingSources)
        {
            RefreshSourceEntries();
        }

        if (e.PropertyName == nameof(AppSettings.LyricsSecondaryLine))
        {
            RefreshSecondaryLineEntries();
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshSourceEntries();
        RefreshSecondaryLineEntries();
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(LyricsEnabled)); OnPropertyChanged(nameof(TwoLineLyricsEnabled));
        OnPropertyChanged(nameof(TextAlignment));
        OnPropertyChanged(nameof(SyllableHighlightEnabled)); OnPropertyChanged(nameof(UnsungOpacityPercent));
        OnPropertyChanged(nameof(UnsungOpacityText)); OnPropertyChanged(nameof(InfoLineFilterEnabled));
        OnPropertyChanged(nameof(MatchStrictness));
        OnPropertyChanged(nameof(AdoptionMode)); OnPropertyChanged(nameof(ConcurrencyBatchSize));
        OnPropertyChanged(nameof(ConcurrencyBatchSizeText)); OnPropertyChanged(nameof(AdoptionDeadlineMilliseconds));
        OnPropertyChanged(nameof(AdoptionDeadlineText));
        OnPropertyChanged(nameof(CanConfigureTwoLine)); OnPropertyChanged(nameof(CanConfigureSecondary));
        OnPropertyChanged(nameof(CanConfigureUnsungOpacity)); OnPropertyChanged(nameof(IsEverySourceDisabled));
    }

    /// <summary>
    /// 按设置重建来源列表（保留用户顺序），并挂上变更回调。
    /// Rebuilds the source list from the settings while keeping the user's order, and wires the change callback.
    ///
    /// 列表始终包含全部已知来源：只列出已启用的话，用户关掉一个来源后它就从这个界面上消失、再也开不回来。顺序是"已启用的
    /// 按用户顺序在前，其余按默认顺序在后"。
    /// The list always contains every known source: listing only the enabled ones would make a source the user turned off disappear
    /// from this page and become impossible to turn back on. The order is "enabled ones in the user's order first, the rest in the
    /// default order after them".
    /// </summary>
    private void RefreshSourceEntries()
    {
        _isRefreshing = true;
        try
        {
            var enabledIds = SettingsManager.Current.LyricsSource.Normalize().EnabledSourceIds;
            var enabled = enabledIds is null
                ? null
                : new HashSet<string>(enabledIds, StringComparer.Ordinal);

            var order = new List<string>(LyricsSourceCatalog.DefaultOrder.Count);
            if (enabledIds is { Count: > 0 })
            {
                foreach (var id in enabledIds)
                {
                    if (LyricsSourceCatalog.IsKnown(id) && !order.Contains(id, StringComparer.Ordinal))
                    {
                        order.Add(id);
                    }
                }
            }

            foreach (var id in LyricsSourceCatalog.DefaultOrder)
            {
                if (!order.Contains(id, StringComparer.Ordinal))
                {
                    order.Add(id);
                }
            }

            foreach (var entry in SourceEntries)
            {
                entry.EnabledChanged -= OnSourceEnabledChanged;
            }

            SourceEntries.Clear();
            foreach (var sourceId in order)
            {
                // 未配置（null）时全部来源都是开启的；配置过之后，列表里没有的 id 视为关闭。
                // While nothing is configured (null) every source is on; once something is configured, an id missing from the list
                // counts as off.
                var isEnabled = enabled?.Contains(sourceId) ?? true;
                var entry = new LyricsSourceSettingItem(sourceId, isEnabled);
                entry.EnabledChanged += OnSourceEnabledChanged;
                SourceEntries.Add(entry);
            }

            OnPropertyChanged(nameof(SourceEntries));
            OnPropertyChanged(nameof(IsEverySourceDisabled));
        }
        finally { _isRefreshing = false; }
    }

    private void OnSourceEnabledChanged(LyricsSourceSettingItem item)
    {
        if (_isRefreshing)
        {
            return;
        }

        SaveSourceEntries();
    }

    /// <summary>
    /// 把列表状态写回设置：恰好是"全部来源按默认顺序"时写回"未配置"，让以后新增的来源自动生效。
    /// Writes the list state back into the settings: an exact "every source in the default order" is stored as "never configured", so
    /// a source added later takes effect on its own.
    /// </summary>
    private void SaveSourceEntries()
    {
        var ordered = SourceEntries.Where(entry => entry.IsEnabled).Select(entry => entry.SourceId).ToArray();
        var isDefaultOrder = ordered.Length == LyricsSourceCatalog.DefaultOrder.Count &&
                             ordered.SequenceEqual(LyricsSourceCatalog.DefaultOrder, StringComparer.Ordinal);
        var settings = new LyricsSourceSettings(isDefaultOrder ? null : ordered);

        _isSavingSources = true;
        try
        {
            SettingsManager.SetLyricsSourceSettings(settings);
        }
        finally
        {
            _isSavingSources = false;
        }

        OnPropertyChanged(nameof(IsEverySourceDisabled));
    }

    [RelayCommand]
    private void MoveSourceUp(LyricsSourceSettingItem? item) => MoveSource(item, -1);

    [RelayCommand]
    private void MoveSourceDown(LyricsSourceSettingItem? item) => MoveSource(item, 1);

    private void MoveSource(LyricsSourceSettingItem? item, int offset)
    {
        if (item is null)
        {
            return;
        }

        var index = SourceEntries.IndexOf(item);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= SourceEntries.Count)
        {
            return;
        }

        SourceEntries.Move(index, target);
        SaveSourceEntries();
    }

    [RelayCommand]
    private void ResetSourceOrder()
    {
        // 逐项切换会各自触发一次写回，因此这里先挂起回调，排序完成后再整体写一次。
        // Toggling each entry would publish one write per entry, so the callback is suspended here and the whole list is written once
        // after the reordering.
        _isRefreshing = true;
        try
        {
            foreach (var entry in SourceEntries)
            {
                entry.IsEnabled = true;
            }

            var ordered = SourceEntries.OrderBy(
                entry => LyricsSourceCatalog.DefaultOrder.ToList().IndexOf(entry.SourceId)).ToArray();
            for (var i = 0; i < ordered.Length; i++)
            {
                var current = SourceEntries.IndexOf(ordered[i]);
                if (current != i)
                {
                    SourceEntries.Move(current, i);
                }
            }
        }
        finally
        {
            _isRefreshing = false;
        }

        SaveSourceEntries();
    }

    /// <summary>
    /// 重建第二行歌词的顺序列表：始终包含三种来源（顺序为用户给定的顺序，其余按默认顺序补在后面）。
    /// Rebuilds the second lyric line's order list: it always contains all three sources, the user's order first and the remaining ones in the
    /// default order after them.
    /// </summary>
    private void RefreshSecondaryLineEntries()
    {
        var order = new List<LyricsSecondaryLineMode>(LyricsSecondaryLinePolicy.DefaultOrder.Count);
        foreach (var mode in LyricsSecondaryLinePolicy.ResolveOrder(SettingsManager.Current.LyricsSecondaryLine))
        {
            if (!order.Contains(mode))
            {
                order.Add(mode);
            }
        }

        foreach (var mode in LyricsSecondaryLinePolicy.DefaultOrder)
        {
            if (!order.Contains(mode))
            {
                order.Add(mode);
            }
        }

        SecondaryLineEntries.Clear();
        foreach (var mode in order)
        {
            SecondaryLineEntries.Add(new LyricsSecondaryLineSettingItem(mode));
        }

        OnPropertyChanged(nameof(SecondaryLineEntries));
        OnPropertyChanged(nameof(SecondaryLineOrderText));
    }

    /// <summary>把列表顺序写回设置：恰好等于默认顺序时写回"未配置"，与来源列表同一处理。/ Writes the list order back into the settings, storing an exact default order as "never configured", the same handling as the source list.</summary>
    private void SaveSecondaryLineEntries()
    {
        var ordered = SecondaryLineEntries.Select(entry => entry.Mode).ToArray();
        var isDefaultOrder = ordered.Length == LyricsSecondaryLinePolicy.DefaultOrder.Count &&
                             ordered.SequenceEqual(LyricsSecondaryLinePolicy.DefaultOrder);
        SettingsManager.SetLyricsSecondaryLineSettings(new LyricsSecondaryLineSettings(isDefaultOrder ? null : ordered));
    }

    private static string SecondaryLineLabelKey(LyricsSecondaryLineMode mode) =>
        LyricsSecondaryLineSettingItem.ResolveLabelKey(mode);

    [RelayCommand]
    private void MoveSecondaryLineUp(LyricsSecondaryLineSettingItem? item) => MoveSecondaryLine(item, -1);

    [RelayCommand]
    private void MoveSecondaryLineDown(LyricsSecondaryLineSettingItem? item) => MoveSecondaryLine(item, 1);

    private void MoveSecondaryLine(LyricsSecondaryLineSettingItem? item, int offset)
    {
        if (item is null)
        {
            return;
        }

        var index = SecondaryLineEntries.IndexOf(item);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= SecondaryLineEntries.Count)
        {
            return;
        }

        SecondaryLineEntries.Move(index, target);
        SaveSecondaryLineEntries();
    }

    [RelayCommand]
    private void ResetSecondaryLineOrder()
    {
        var ordered = SecondaryLineEntries
            .OrderBy(entry => LyricsSecondaryLinePolicy.DefaultOrder.ToList().IndexOf(entry.Mode))
            .ToArray();
        for (var i = 0; i < ordered.Length; i++)
        {
            var current = SecondaryLineEntries.IndexOf(ordered[i]);
            if (current != i)
            {
                SecondaryLineEntries.Move(current, i);
            }
        }

        SaveSecondaryLineEntries();
    }
}
