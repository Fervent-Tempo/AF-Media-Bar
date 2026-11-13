// 媒体卡片设置：编辑独立窗口的显隐、配色与组件开关，不参与任务栏栏位布局。
// 频谱参数可选跟随主程序或单独自定义；性能指标只做显隐开关，采样间隔与轮换方式仍跟随主程序。
using System.Windows.Media;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>媒体卡片设置：显隐、外观、组件开关与三语自定义字体。</summary>
public sealed class MediaCardViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsConfiguration _configuration;
    private IReadOnlyList<FontFamilyChoice> _fontChoices;
    private bool _isRefreshingFonts;
    private int _fontRefreshGeneration;
    private bool _disposed;

    public MediaCardViewModel(ISettingsConfiguration configuration)
    {
        _configuration = configuration;
        _fontChoices = BuildInitialFontChoices();
        SettingsManager.SettingsChanged += OnSettingsChanged;
    }

    private static MediaCardSettings Current => SettingsManager.Current.MediaCard;

    /// <summary>页面作用域的取消令牌，与外观页等其它设置页共用同一来源。</summary>
    public CancellationToken ContextCancellationToken => _configuration.CancellationToken;

    /// <summary>本机字体列表。页面呈现后才补齐全量清单，构造页面时不枚举字体。</summary>
    public IReadOnlyList<FontFamilyChoice> FontChoices => _fontChoices;

    /// <summary>扫描完成前只给系统项和已保存字体，让当前选择立刻可见。</summary>
    private static IReadOnlyList<FontFamilyChoice> BuildInitialFontChoices()
    {
        var sample = InstalledFontCatalog.MultilingualFontSample;
        var choices = new List<FontFamilyChoice>
        {
            new(string.Empty, Translations.Get("Settings.MediaCard.Font.FollowSystem"), sample, SystemFonts.MessageFontFamily)
        };
        foreach (var name in SavedFontNames())
            choices.Add(new FontFamilyChoice(name, name, sample, new FontFamily(name)));
        return choices;
    }

    private static IEnumerable<string> SavedFontNames() =>
        new[] { Current.ChineseFontFamily, Current.JapaneseFontFamily, Current.EnglishFontFamily }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase);

    /// <summary>由页面在呈现完成后调用；一次通知补齐全量字体，避免逐项插入造成通知风暴。</summary>
    public async Task RefreshInstalledFontsAsync(CancellationToken cancellationToken)
    {
        var generation = ++_fontRefreshGeneration;
        try
        {
            var installed = await InstalledFontCatalog.RefreshAllFontChoicesAsync(cancellationToken);
            if (_disposed || generation != _fontRefreshGeneration || cancellationToken.IsCancellationRequested)
                return;

            var existing = _fontChoices.ToDictionary(choice => choice.Name, StringComparer.OrdinalIgnoreCase);
            var merged = new List<FontFamilyChoice>(_fontChoices.Count + installed.Count);
            // 系统项与已保存字体复用原对象并置顶；替换期间门控住 SelectedValue 的空值回写。
            merged.AddRange(_fontChoices);
            foreach (var choice in installed)
            {
                if (!existing.ContainsKey(choice.Name))
                    merged.Add(choice);
            }

            _isRefreshingFonts = true;
            try
            {
                _fontChoices = merged;
                OnPropertyChanged(nameof(FontChoices));
                OnPropertyChanged(nameof(ChineseFontFamily));
                OnPropertyChanged(nameof(JapaneseFontFamily));
                OnPropertyChanged(nameof(EnglishFontFamily));
            }
            finally { _isRefreshingFonts = false; }
        }
        catch (OperationCanceledException) { /* 离页，保留初始列表即可。 */ }
        catch (Exception) { /* 扫描失败时保留系统项与已保存字体，不影响其它设置项。 */ }
    }

    /// <summary>字体显示异常时把三语字体一次性恢复为跟随系统。</summary>
    public void ResetFonts() => Update(Current with
    {
        ChineseFontFamily = string.Empty,
        JapaneseFontFamily = string.Empty,
        EnglishFontFamily = string.Empty
    });

    public string ChineseFontFamily
    {
        get => Current.ChineseFontFamily;
        set { if (value is not null && !_isRefreshingFonts) Update(Current with { ChineseFontFamily = value }); }
    }

    public string JapaneseFontFamily
    {
        get => Current.JapaneseFontFamily;
        set { if (value is not null && !_isRefreshingFonts) Update(Current with { JapaneseFontFamily = value }); }
    }

    public string EnglishFontFamily
    {
        get => Current.EnglishFontFamily;
        set { if (value is not null && !_isRefreshingFonts) Update(Current with { EnglishFontFamily = value }); }
    }

    public bool Enabled
    {
        get => Current.Enabled;
        set => Update(Current with { Enabled = value });
    }

    public bool OnlyCard
    {
        get => Current.OnlyCard;
        set => Update(Current with { OnlyCard = value });
    }

    public bool Topmost
    {
        get => Current.Topmost;
        set => Update(Current with { Topmost = value });
    }

    public MediaCardColorTheme ColorTheme
    {
        get => Current.ColorTheme;
        set => Update(Current with { ColorTheme = value });
    }

    public bool ShowRomanization
    {
        get => Current.ShowRomanization;
        set => Update(Current with { ShowRomanization = value });
    }

    public bool ShowTrackInfoOnEmptyLines
    {
        get => Current.ShowTrackInfoOnEmptyLines;
        set => Update(Current with { ShowTrackInfoOnEmptyLines = value });
    }

    public bool ShowSpectrum
    {
        get => Current.ShowSpectrum;
        set => Update(Current with { ShowSpectrum = value });
    }

    public bool ImmersiveSpectrum
    {
        get => Current.ImmersiveSpectrum;
        set => Update(Current with { ImmersiveSpectrum = value });
    }

    public bool WavyProgressBar
    {
        get => Current.WavyProgressBar;
        set => Update(Current with { WavyProgressBar = value });
    }

    public DesktopSpectrumMode SpectrumMode
    {
        get => Current.SpectrumMode;
        set => Update(Current with { SpectrumMode = value });
    }

    public SpectrumStyle SpectrumStyle
    {
        get => Current.SpectrumStyle;
        set => Update(Current with { SpectrumStyle = value });
    }

    public int SpectrumBarCount
    {
        get => Current.SpectrumBarCount;
        set => Update(Current with { SpectrumBarCount = value });
    }

    public int MinimumSpectrumBarCount => SpectrumComponentSettings.MinimumBandCount;

    public int MaximumSpectrumBarCount => SpectrumComponentSettings.MaximumBandCount;

    public int SpectrumSensitivity
    {
        get => Current.SpectrumSensitivity;
        set => Update(Current with { SpectrumSensitivity = value });
    }

    public int MinimumSpectrumSensitivity => SpectrumComponentSettings.MinimumSensitivityPercent;

    public int MaximumSpectrumSensitivity => SpectrumComponentSettings.MaximumSensitivityPercent;

    public int SpectrumSensitivityStep => SpectrumComponentSettings.SensitivityStepPercent;

    public bool IsCustomSpectrum => Current.SpectrumMode == DesktopSpectrumMode.Custom;

    public bool ShowCpu
    {
        get => Current.ShowCpu;
        set => Update(Current with { ShowCpu = value });
    }

    public bool ShowGpu
    {
        get => Current.ShowGpu;
        set => Update(Current with { ShowGpu = value });
    }

    public bool ShowMemory
    {
        get => Current.ShowMemory;
        set => Update(Current with { ShowMemory = value });
    }

    public bool ShowNetwork
    {
        get => Current.ShowNetwork;
        set => Update(Current with { ShowNetwork = value });
    }

    private void Update(MediaCardSettings value)
    {
        if (_configuration.IsActive)
            SettingsManager.Current.MediaCard = value;
    }

    // 外部重置或换语言时刷新所有绑定；系统项文案跟着语言走，只改现有选项对象的属性。
    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        var followSystem = Translations.Get("Settings.MediaCard.Font.FollowSystem");
        foreach (var choice in _fontChoices)
        {
            if (choice.Name.Length == 0)
                choice.DisplayName = followSystem;
        }
        OnPropertyChanged(string.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        SettingsManager.SettingsChanged -= OnSettingsChanged;
    }
}
