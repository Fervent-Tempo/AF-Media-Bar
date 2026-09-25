using System.Collections.ObjectModel;
using System.Windows.Threading;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>歌词呈现、取词来源与对齐设置。 / Lyric presentation, retrieval sources, and alignment settings.</summary>
public partial class LyricsViewModel : ObservableObject
{
    private readonly LocalizationService _localization;
    private readonly IMediaSessionSourceScanner _mediaSessionService;

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
    /// <param name="mediaSessionService">会话来源快照，供"扫描正在播放"读取当前播放器 / The session-source snapshot whose current players the scan reads.</param>
    public LyricsViewModel(LocalizationService localization, IMediaSessionSourceScanner mediaSessionService)
    {
        _localization = localization;
        _mediaSessionService = mediaSessionService;
        SettingsManager.SettingsChanged += OnSettingsChanged;
        _localization.LanguageChanged += OnLanguageChanged;
        RefreshSourceEntries();
        RefreshSecondaryLineEntries();
        RefreshBindingEntries();
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

    /// <summary>歌词字距（字号的百分比）。/ Lyric character spacing as a percentage of the font size.</summary>
    public int CharacterSpacingPercent
    {
        get => SettingsManager.Current.LyricsCharacterSpacingPercent;
        set
        {
            SettingsManager.SetLyricsCharacterSpacingPercent(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(CharacterSpacingText));
        }
    }

    /// <summary>字距的读数文本。/ The character-spacing readout text.</summary>
    public string CharacterSpacingText => $"{CharacterSpacingPercent}%";

    /// <summary>双行歌词额外增加的行距（字号的百分比）。/ Extra line gap for two-line lyrics as a percentage of the font size.</summary>
    public int LineGapPercent
    {
        get => SettingsManager.Current.LyricsLineGapPercent;
        set
        {
            SettingsManager.SetLyricsLineGapPercent(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(LineGapText));
        }
    }

    /// <summary>行距的读数文本：加号说明这是"额外"增加的间距，百分号说明它随字号缩放。/ The line-gap readout: the plus sign marks spacing added on top, the percent sign that it scales with the font.</summary>
    public string LineGapText => $"+{LineGapPercent}%";

    /// <summary>歌词框是否固定长度。/ Whether the lyric box keeps a fixed length.</summary>
    public bool FixedWidthEnabled
    {
        get => SettingsManager.Current.LyricsFixedWidthEnabled;
        set
        {
            SettingsManager.SetLyricsFixedWidthEnabled(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanConfigureFixedWidthDip));
        }
    }

    /// <summary>歌词框固定长度（DIP）。/ Fixed lyric-box length in DIP.</summary>
    public int FixedWidthDip
    {
        get => SettingsManager.Current.LyricsFixedWidthDip;
        set
        {
            SettingsManager.SetLyricsFixedWidthDip(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(FixedWidthText));
        }
    }

    /// <summary>固定长度的读数文本。/ The fixed-width readout text.</summary>
    public string FixedWidthText => $"{FixedWidthDip}";

    /// <summary>歌词框固定长度只在启用歌词时有意义。/ The fixed lyric-box length only applies while lyrics are on.</summary>
    public bool CanConfigureFixedWidth => LyricsEnabled;

    /// <summary>长度滑杆只在"固定"开启时可用。/ The length slider is available only while the fixed mode is on.</summary>
    public bool CanConfigureFixedWidthDip => FixedWidthEnabled;

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

    /// <summary>
    /// 默认接口绑定列表：内置播放器在前，用户条目与手动添加的在后；扫描到的播放器追加进来。
    /// The default-binding list: built-in players first, then the user's entries and manual additions; scanned players are
    /// appended.
    /// </summary>
    public ObservableCollection<LyricsDefaultBindingItem> BindingEntries { get; } = [];

    /// <summary>手动添加播放器的输入框内容。/ The text of the manual-add player input.</summary>
    [ObservableProperty]
    private string _newPlayerId = string.Empty;

    /// <summary>是否一个来源都没启用：此时不会请求任何歌词服务，页面用提示条说明后果。
    /// Whether no source is enabled: no lyric service is contacted then, and a callout on the page states that consequence.</summary>
    public bool IsEverySourceDisabled => SourceEntries.Count > 0 && SourceEntries.All(entry => !entry.IsEnabled);

    public bool CanConfigureTwoLine => LyricsEnabled;
    public bool CanConfigureSecondary => LyricsEnabled && TwoLineLyricsEnabled;

    /// <summary>未唱部分不透明度只在启用逐字擦亮时可用：没有擦亮时它没有作用对象。/ The unsung opacity only applies while highlighting is on.</summary>
    public bool CanConfigureUnsungOpacity => SyllableHighlightEnabled;

    /// <summary>行距只在双行歌词开启时可用：单行时没有"两行之间"可调。/ The line gap applies only while two-line lyrics are on: with a single line there is no "between" to adjust.</summary>
    public bool CanConfigureLineGap => LyricsEnabled && TwoLineLyricsEnabled;

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
            RefreshBindingEntries();
            RaiseAll();
            return;
        }

        if (e.PropertyName == nameof(AppSettings.LyricsSource) && !_isSavingSources)
        {
            RefreshSourceEntries();
        }

        if (e.PropertyName == nameof(AppSettings.LyricsDefaultBindings) && !_isSavingBindings)
        {
            RefreshBindingEntries();
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
        RefreshBindingEntries();
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(LyricsEnabled)); OnPropertyChanged(nameof(TwoLineLyricsEnabled));
        OnPropertyChanged(nameof(TextAlignment));
        OnPropertyChanged(nameof(SyllableHighlightEnabled)); OnPropertyChanged(nameof(UnsungOpacityPercent));
        OnPropertyChanged(nameof(UnsungOpacityText)); OnPropertyChanged(nameof(InfoLineFilterEnabled));
        OnPropertyChanged(nameof(CharacterSpacingPercent)); OnPropertyChanged(nameof(CharacterSpacingText));
        OnPropertyChanged(nameof(LineGapPercent)); OnPropertyChanged(nameof(LineGapText));
        OnPropertyChanged(nameof(FixedWidthEnabled)); OnPropertyChanged(nameof(FixedWidthDip));
        OnPropertyChanged(nameof(FixedWidthText));
        OnPropertyChanged(nameof(CanConfigureFixedWidth)); OnPropertyChanged(nameof(CanConfigureFixedWidthDip));
        OnPropertyChanged(nameof(MatchStrictness));
        OnPropertyChanged(nameof(AdoptionMode)); OnPropertyChanged(nameof(ConcurrencyBatchSize));
        OnPropertyChanged(nameof(ConcurrencyBatchSizeText)); OnPropertyChanged(nameof(AdoptionDeadlineMilliseconds));
        OnPropertyChanged(nameof(AdoptionDeadlineText));
        OnPropertyChanged(nameof(CanConfigureTwoLine)); OnPropertyChanged(nameof(CanConfigureSecondary));
        OnPropertyChanged(nameof(CanConfigureUnsungOpacity)); OnPropertyChanged(nameof(CanConfigureLineGap));
        OnPropertyChanged(nameof(IsEverySourceDisabled));
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

    // ---- 默认取词接口绑定 ----

    /// <summary>
    /// 本视图模型正在写绑定表：写回会同步触发设置变更事件，若此时重建列表会与来源列表同一问题。
    /// This view model is writing the binding table: the write publishes a settings change synchronously, which would
    /// rebuild the list mid-edit exactly like the source list does.
    /// </summary>
    private bool _isSavingBindings;

    /// <summary>
    /// 重建绑定列表：设置从未配置（null）时按内置绑定预填；一旦配置过，设置文件就是唯一权威，逐条还原行
    /// （备注回落到内置播放器名或 AppID 原文）。内置条目没有任何特殊分支。
    /// Rebuilds the binding list: while the settings were never configured (null) the rows are prefilled from the built-in
    /// bindings; once configured the settings file is the only authority and every row is restored from an entry (the remark
    /// falls back to the built-in player's name or the raw AppID). Built-in entries take no special branch at all.
    /// </summary>
    private void RefreshBindingEntries()
    {
        _isRefreshing = true;
        try
        {
            BindingEntries.Clear();
            var entries = SettingsManager.Current.LyricsDefaultBindings.Normalize().Bindings;
            if (entries is null)
            {
                foreach (var builtin in LyricsConcurrencyPolicy.BuiltInBindings)
                {
                    AddBindingRow(
                        builtin.CanonicalAppId,
                        Translations.Get($"Lyrics.DefaultBindings.Player.{builtin.NameKey}"),
                        builtin.DefaultSourceId);
                }

                return;
            }

            foreach (var entry in entries)
            {
                var fallbackName = LyricsConcurrencyPolicy.BuiltInBindings
                    .Where(builtin => entry.AppId.Contains(builtin.CanonicalAppId, StringComparison.OrdinalIgnoreCase))
                    .Select(builtin => Translations.Get($"Lyrics.DefaultBindings.Player.{builtin.NameKey}"))
                    .FirstOrDefault();
                AddBindingRow(
                    entry.AppId,
                    entry.Remark ?? fallbackName ?? entry.AppId,
                    entry.SourceId);
            }
        }
        finally { _isRefreshing = false; }
    }

    private void AddBindingRow(string appId, string displayName, string? selectedSourceId)
    {
        var item = new LyricsDefaultBindingItem(appId, displayName, selectedSourceId);
        item.BindingChanged += OnBindingChanged;
        BindingEntries.Add(item);
    }

    private void OnBindingChanged(LyricsDefaultBindingItem item)
    {
        if (_isRefreshing || _isSavingBindings)
        {
            return;
        }

        SaveBindingEntries();
    }

    /// <summary>
    /// 把列表状态写回绑定表，所有行一律按用户条目处理：绑定写来源，"不绑定"与空备注写 null。
    /// 写回后配置文件即为唯一权威，内置映射不再参与。
    /// Writes the list state back into the binding table, treating every row as a plain user entry: a bound row stores its
    /// source, "not bound" and an empty remark store null. After the write the settings file is the only authority and the
    /// built-in mapping no longer applies.
    /// </summary>
    private void SaveBindingEntries()
    {
        var entries = BindingEntries
            .Select(static row => new LyricsDefaultBinding(
                row.AppId,
                string.IsNullOrWhiteSpace(row.SelectedSourceId) ? null : row.SelectedSourceId,
                string.IsNullOrWhiteSpace(row.DisplayName) || string.Equals(row.DisplayName, row.AppId, StringComparison.Ordinal)
                    ? null
                    : row.DisplayName))
            .ToArray();

        _isSavingBindings = true;
        try
        {
            // 写回总是显式数组（哪怕为空）：null 表示"从未配置"，会退回内置映射，语义相反。
            // The write-back is always an explicit array (even empty): null means "never configured", which would fall back
            // to the built-in mapping — the opposite of what the user asked for.
            SettingsManager.SetLyricsDefaultBindingSettings(new LyricsDefaultBindingSettings(entries));
        }
        finally
        {
            _isSavingBindings = false;
        }
    }

    /// <summary>
    /// 扫描当前活动的 SMTC 会话，把尚未出现过的播放器追加成绑定行。
    /// Scans the active SMTC sessions and appends a binding row for every player not listed yet.
    /// </summary>
    [RelayCommand]
    private void ScanPlayingPlayers()
    {
        _isRefreshing = true;
        try
        {
            foreach (var option in _mediaSessionService.CurrentSessionOptions)
            {
                var sourceId = option.SourceId;
                if (string.IsNullOrWhiteSpace(sourceId) ||
                    BindingEntries.Any(row =>
                        sourceId.Contains(row.AppId, StringComparison.OrdinalIgnoreCase) ||
                        row.AppId.Contains(sourceId, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                // 扫描行的备注默认用 AppID 原文填充：我们拿不到播放器的"真实名字"，用户可自行改备注。
                // A scanned row's remark defaults to the raw AppID: we cannot know the player's real name, and the user can
                // edit the remark themselves.
                AddBindingRow(sourceId, sourceId, null);
            }
        }
        finally { _isRefreshing = false; }
    }

    [RelayCommand]
    private void AddPlayer()
    {
        var appId = NewPlayerId.Trim();
        if (appId.Length == 0 ||
            BindingEntries.Any(row => row.AppId.Equals(appId, StringComparison.OrdinalIgnoreCase)))
        {
            NewPlayerId = string.Empty;
            return;
        }

        AddBindingRow(appId, appId, null);
        NewPlayerId = string.Empty;
    }

    /// <summary>
    /// 删除一行绑定：内置行删除后留下移除标记（行消失、内置映射被压制），扫描或手动添加可让它回来。
    /// Deletes one binding row: a deleted built-in row leaves a removal marker behind (the row disappears and the built-in
    /// mapping is suppressed), and a scan or a manual add brings it back.
    /// </summary>
    [RelayCommand]
    private void RemoveBinding(LyricsDefaultBindingItem? item)
    {
        if (item is null)
        {
            return;
        }

        if (BindingEntries.Remove(item))
        {
            SaveBindingEntries();
        }
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
