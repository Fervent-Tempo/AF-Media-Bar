using System.Collections.ObjectModel;
using System.Windows.Threading;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>歌词呈现、取词来源与对齐设置。 / Lyric presentation, retrieval sources, and alignment settings.</summary>
public partial class LyricsViewModel : ObservableObject, IDisposable
{
    /// <summary>Cancels asynchronous UI work when this editor context becomes inactive.</summary>
    public CancellationToken ContextCancellationToken => _configuration.CancellationToken;

    private readonly LocalizationService _localization;
    private readonly ISettingsConfiguration _configuration;
    private bool _disposed;

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
    public LyricsViewModel(LocalizationService localization, ISettingsConfiguration configuration)
    {
        _localization = localization;
        _configuration = configuration;
        configuration.Activated += OnActivated;
        SettingsManager.SettingsChanged += OnSettingsChanged;
        _localization.LanguageChanged += OnLanguageChanged;
        RefreshSourceEntries();
        RefreshSecondaryLineEntries();
    }

    /// <summary>已保存的艺术家分隔符；弹窗编辑草稿由呈现层持有。</summary>
    public string ArtistSeparatorsText => _configuration.Current.LyricsArtistSeparators;

    /// <summary>内置分隔符，供编辑弹窗恢复草稿使用，不覆盖当前配置。</summary>
    public string DefaultArtistSeparatorsText => new AppSettings().LyricsArtistSeparators;

    /// <summary>用户确认后保存分隔符并重新匹配歌词。</summary>
    public void ApplyArtistSeparators(string separators) => _configuration.SetLyricsArtistSeparators(separators);

    public bool LyricsEnabled { get => _configuration.Current.LyricsEnabled; set { _configuration.SetLyricsEnabled(value); RaiseAll(); } }
    public bool AllowBrowserAndVideoLyrics { get => _configuration.Current.AllowBrowserAndVideoLyrics; set { _configuration.SetAllowBrowserAndVideoLyrics(value); OnPropertyChanged(); } }
    public bool TwoLineLyricsEnabled { get => _configuration.Current.TwoLineLyricsEnabled; set { _configuration.SetTwoLineLyricsEnabled(value); RaiseAll(); } }
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
    public LyricsTextAlignment TextAlignment { get => _configuration.Current.LyricsTextAlignment; set { _configuration.SetLyricsTextAlignment(value); OnPropertyChanged(); } }

    /// <summary>是否启用逐字擦亮。/ Whether syllable highlighting is enabled.</summary>
    public bool SyllableHighlightEnabled
    {
        get => _configuration.Current.LyricsSyllableHighlightEnabled;
        set { _configuration.SetLyricsSyllableHighlightEnabled(value); RaiseAll(); }
    }

    /// <summary>启用逐字擦亮时底色层（未唱部分）的不透明度百分比。/ Opacity percentage of the base (unsung) layer while highlighting is on.</summary>
    public int UnsungOpacityPercent
    {
        get => _configuration.Current.LyricsUnsungOpacityPercent;
        set
        {
            _configuration.SetLyricsUnsungOpacityPercent(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(UnsungOpacityText));
        }
    }

    /// <summary>不透明度的读数文本。/ The opacity readout text.</summary>
    public string UnsungOpacityText => $"{UnsungOpacityPercent}%";

    /// <summary>歌词字距（字号的百分比）。/ Lyric character spacing as a percentage of the font size.</summary>
    public int CharacterSpacingPercent
    {
        get => _configuration.Current.LyricsCharacterSpacingPercent;
        set
        {
            _configuration.SetLyricsCharacterSpacingPercent(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(CharacterSpacingText));
        }
    }

    /// <summary>字距的读数文本。/ The character-spacing readout text.</summary>
    public string CharacterSpacingText => $"{CharacterSpacingPercent}%";

    /// <summary>双行歌词额外增加的行距（字号的百分比）。/ Extra line gap for two-line lyrics as a percentage of the font size.</summary>
    public int LineGapPercent
    {
        get => _configuration.Current.LyricsLineGapPercent;
        set
        {
            _configuration.SetLyricsLineGapPercent(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(LineGapText));
        }
    }

    /// <summary>行距的读数文本：加号说明这是"额外"增加的间距，百分号说明它随字号缩放。/ The line-gap readout: the plus sign marks spacing added on top, the percent sign that it scales with the font.</summary>
    public string LineGapText => $"+{LineGapPercent}%";

    /// <summary>歌词框是否固定长度。/ Whether the lyric box keeps a fixed length.</summary>
    public bool FixedWidthEnabled
    {
        get => _configuration.Current.LyricsFixedWidthEnabled;
        set
        {
            _configuration.SetLyricsFixedWidthEnabled(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanConfigureFixedWidthDip));
        }
    }

    /// <summary>歌词框固定长度（DIP）。/ Fixed lyric-box length in DIP.</summary>
    public int FixedWidthDip
    {
        get => _configuration.Current.LyricsFixedWidthDip;
        set
        {
            _configuration.SetLyricsFixedWidthDip(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(FixedWidthText));
        }
    }

    /// <summary>固定长度的读数文本。/ The fixed-width readout text.</summary>
    public string FixedWidthText => $"{FixedWidthDip}";

    /// <summary>歌词框固定长度只在启用歌词时有意义。/ The fixed lyric-box length only applies while lyrics are on.</summary>
    public bool CanConfigureFixedWidth => LyricsEnabled;

    /// <summary>长度滑杆只在"固定"开启时可用。/ The length slider is available only while the fixed mode is on.</summary>
    public bool CanConfigureFixedWidthDip => LyricsEnabled && FixedWidthEnabled;

    /// <summary>是否丢弃作者、作曲、制作等信息行。/ Whether credit lines are dropped.</summary>
    public bool InfoLineFilterEnabled
    {
        get => _configuration.Current.LyricsInfoLineFilterEnabled;
        set { _configuration.SetLyricsInfoLineFilterEnabled(value); OnPropertyChanged(); }
    }

    /// <summary>
    /// 用户上次选过的方向：总开关关掉时方向也一起失效（存 <c>None</c>），再打开时能回到原方向，不必重选。
    /// The direction the user last picked: turning the master switch off clears the direction as well (stored as <c>None</c>), so
    /// turning it back on restores that direction instead of asking again.
    /// </summary>
    private LyricsChineseConversionMode _lastConversionDirection = LyricsChineseConversionMode.SimplifiedToTraditional;

    /// <summary>
    /// 歌词繁简转换的总开关：关掉就是 <c>None</c>，歌词按来源原样显示。
    /// Master switch for the Chinese conversion of lyrics: off means <c>None</c>, so lyrics show exactly as their source wrote them.
    ///
    /// 开关与下拉写的是同一个设置，两者不会各说各话：下拉选到"不转换"时开关自然变成关。
    /// The switch and the list write one and the same setting, so they can never disagree — choosing "off" in the list turns the
    /// switch off by itself.
    /// </summary>
    public bool ChineseConversionEnabled
    {
        get => _configuration.Current.LyricsChineseConversion != LyricsChineseConversionMode.None;
        set
        {
            if (value == ChineseConversionEnabled)
                return;
            if (!value)
                _lastConversionDirection = ChineseConversion;
            ChineseConversion = value ? _lastConversionDirection : LyricsChineseConversionMode.None;
        }
    }

    /// <summary>
    /// 转换方向。<c>None</c> 表示不转换 / Conversion direction. <c>None</c> means no conversion.
    /// </summary>
    public LyricsChineseConversionMode ChineseConversion
    {
        get => _configuration.Current.LyricsChineseConversion;
        set
        {
            if (!_configuration.IsActive || value == ChineseConversion)
                return;
            if (value != LyricsChineseConversionMode.None)
            {
                _lastConversionDirection = value;
            }

            if (value != LyricsChineseConversionMode.None)
                LyricsChineseConverter.WarmUp();
            _configuration.SetLyricsChineseConversion(value);
        }
    }

    /// <summary>方向下拉只在总开关打开时可改。/ The direction list is editable only while the master switch is on.</summary>
    public bool CanConfigureChineseConversion => ChineseConversionEnabled;

    /// <summary>取词来源启用列表，执行顺序由代码策略决定。/ Enabled sources; execution order belongs to the retrieval policy.</summary>
    public ObservableCollection<LyricsSourceSettingItem> SourceEntries { get; } = [];

    /// <summary>是否一个来源都没启用：此时不会请求任何歌词服务，页面用提示条说明后果。
    /// Whether no source is enabled: no lyric service is contacted then, and a callout on the page states that consequence.</summary>
    public bool IsEverySourceDisabled => SourceEntries.Count > 0 && SourceEntries.All(entry => !entry.IsEnabled);

    public bool CanConfigureTwoLine => LyricsEnabled;
    public bool CanConfigureSecondary => LyricsEnabled && TwoLineLyricsEnabled;

    /// <summary>未唱部分不透明度只在启用逐字擦亮时可用：没有擦亮时它没有作用对象。/ The unsung opacity only applies while highlighting is on.</summary>
    public bool CanConfigureUnsungOpacity => SyllableHighlightEnabled;

    /// <summary>行距只在双行歌词开启时可用：单行时没有"两行之间"可调。/ The line gap applies only while two-line lyrics are on: with a single line there is no "between" to adjust.</summary>
    public bool CanConfigureLineGap => LyricsEnabled && TwoLineLyricsEnabled;

    public void ResetLyrics() => _configuration.Reset(SettingsResetScope.Lyrics);

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (_disposed || !_configuration.IsActive) return;
        // 重建来源列表的动作全部回到 UI 线程（写设置的一方可能在后台线程上）。
        // Everything that rebuilds a bound list goes back to the UI thread (whoever writes the settings may be on a background one).
        if (!_dispatcher.CheckAccess())
        {
            DispatcherHelper.Run(_dispatcher, () => OnSettingsChanged(sender, e));
            return;
        }

        if (e.ResetScope is SettingsResetScope.Lyrics or SettingsResetScope.All)
        {
            OnPropertyChanged(nameof(ArtistSeparatorsText));
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
        if (e.PropertyName == nameof(AppSettings.LyricsArtistSeparators))
            OnPropertyChanged(nameof(ArtistSeparatorsText));
        if (e.PropertyName == nameof(AppSettings.LyricsChineseConversion))
            RaiseChineseConversionProperties();
        if (e.ResetScope == SettingsResetScope.Components || e.PropertyName is nameof(AppSettings.LyricsEnabled)
            or nameof(AppSettings.TwoLineLyricsEnabled) or nameof(AppSettings.LyricsTextAlignment)
            or nameof(AppSettings.LyricsFixedWidthEnabled) or nameof(AppSettings.LyricsFixedWidthDip))
            RaiseAll();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshSourceEntries();
        RefreshSecondaryLineEntries();
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(LyricsEnabled)); OnPropertyChanged(nameof(TwoLineLyricsEnabled));
        OnPropertyChanged(nameof(AllowBrowserAndVideoLyrics));
        OnPropertyChanged(nameof(TextAlignment));
        OnPropertyChanged(nameof(SyllableHighlightEnabled)); OnPropertyChanged(nameof(UnsungOpacityPercent));
        OnPropertyChanged(nameof(UnsungOpacityText)); OnPropertyChanged(nameof(InfoLineFilterEnabled));
        OnPropertyChanged(nameof(CharacterSpacingPercent)); OnPropertyChanged(nameof(CharacterSpacingText));
        OnPropertyChanged(nameof(LineGapPercent)); OnPropertyChanged(nameof(LineGapText));
        OnPropertyChanged(nameof(FixedWidthEnabled)); OnPropertyChanged(nameof(FixedWidthDip));
        OnPropertyChanged(nameof(FixedWidthText));
        OnPropertyChanged(nameof(CanConfigureFixedWidth)); OnPropertyChanged(nameof(CanConfigureFixedWidthDip));
        OnPropertyChanged(nameof(CanConfigureTwoLine)); OnPropertyChanged(nameof(CanConfigureSecondary));
        OnPropertyChanged(nameof(CanConfigureUnsungOpacity)); OnPropertyChanged(nameof(CanConfigureLineGap));
        OnPropertyChanged(nameof(IsEverySourceDisabled));
        RaiseChineseConversionProperties();
    }

    private void RaiseChineseConversionProperties()
    {
        OnPropertyChanged(nameof(ChineseConversionEnabled));
        OnPropertyChanged(nameof(ChineseConversion));
        OnPropertyChanged(nameof(CanConfigureChineseConversion));
    }

    /// <summary>
    /// 按代码目录顺序显示全部来源并恢复启用状态；旧配置中的排序不控制获取阶段。
    /// Lists every source in catalogue order and restores enabled flags; old ordering does not control dispatch.
    /// </summary>
    private void RefreshSourceEntries()
    {
        _isRefreshing = true;
        try
        {
            var enabledIds = _configuration.Current.LyricsSource.Normalize().EnabledSourceIds;
            var enabled = enabledIds is null
                ? null
                : new HashSet<string>(enabledIds, StringComparer.Ordinal);

            var order = LyricsSourceCatalog.DefaultOrder;

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
        if (_disposed || !_configuration.IsActive || _isRefreshing)
        {
            return;
        }

        SaveSourceEntries();
    }

    /// <summary>
    /// 把列表状态写回设置：恰好是"全部来源启用"时写回"未配置"，让以后新增的来源自动生效。
    /// Writes the list state back into the settings: "every source enabled" is stored as "never configured", so
    /// a source added later takes effect on its own.
    /// </summary>
    private void SaveSourceEntries()
    {
        var enabled = SourceEntries.Where(entry => entry.IsEnabled).Select(entry => entry.SourceId).ToArray();
        var settings = new LyricsSourceSettings(SourceEntries.All(entry => entry.IsEnabled) ? null : enabled);

        _isSavingSources = true;
        try
        {
            _configuration.SetLyricsSourceSettings(settings);
        }
        finally
        {
            _isSavingSources = false;
        }

        OnPropertyChanged(nameof(IsEverySourceDisabled));
    }

    [RelayCommand]
    private void EnableAllSources()
    {
        // 暂停逐项写回；全部启用后仅发布一次设置变更。
        _isRefreshing = true;
        try
        {
            foreach (var entry in SourceEntries)
            {
                entry.IsEnabled = true;
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
        foreach (var mode in LyricsSecondaryLinePolicy.ResolveOrder(_configuration.Current.LyricsSecondaryLine))
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
        _configuration.SetLyricsSecondaryLineSettings(new LyricsSecondaryLineSettings(isDefaultOrder ? null : ordered));
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
    private void OnActivated(object? sender, EventArgs e) { RefreshSourceEntries(); RefreshSecondaryLineEntries(); RaiseAll(); }
    /// <summary>Releases settings and language subscriptions for this cached editor.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SettingsManager.SettingsChanged -= OnSettingsChanged;
        _localization.LanguageChanged -= OnLanguageChanged;
        _configuration.Activated -= OnActivated;
        foreach (var item in SourceEntries) item.EnabledChanged -= OnSourceEnabledChanged;
    }

}
