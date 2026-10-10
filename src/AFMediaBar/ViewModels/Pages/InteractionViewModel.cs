using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Abstractions;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>任务栏静置内容与托盘图标的点击、滚轮结果绑定。 / Click and wheel-result bindings for taskbar rest content and the tray icon.</summary>
public partial class InteractionViewModel : ObservableObject, IDisposable
{
    /// <summary>Cancels asynchronous UI work when this editor context becomes inactive.</summary>
    public CancellationToken ContextCancellationToken => _configuration.CancellationToken;

    private readonly LocalizationService _localization;
    private readonly ISettingsConfiguration _configuration;
    private bool _disposed;

    public PlayerClickAction ArtworkClickAction
    {
        get => Current.ArtworkClickAction;
        set => Update(Current with { ArtworkClickAction = value });
    }

    public PlayerClickAction TextClickAction
    {
        get => Current.TextClickAction;
        set => Update(Current with { TextClickAction = value });
    }

    /// <summary>
    /// 鼠标停在封面上时封面的放大方式：<see cref="ArtworkHoverMode.Off"/> 保持栏内 1.1 倍紧凑放大，
    /// <see cref="ArtworkHoverMode.Zoom"/> 原地放大 2 倍，<see cref="ArtworkHoverMode.Preview"/> 弹出独立大图。
    /// How the artwork behaves while the pointer rests on it: <see cref="ArtworkHoverMode.Off"/> keeps the compact 1.1× zoom
    /// inside the bar, <see cref="ArtworkHoverMode.Zoom"/> grows it in place to 2×, and <see cref="ArtworkHoverMode.Preview"/>
    /// pops a separate large card.
    /// </summary>
    public ArtworkHoverMode ArtworkHoverMode
    {
        get => Current.ArtworkHoverMode;
        set => Update(Current with { ArtworkHoverMode = value });
    }

    public WheelAction PrimaryWheelAction
    {
        get => Current.PrimaryWheelAction;
        set => Update(Current with { PrimaryWheelAction = value });
    }

    public InteractionModifier Modifier
    {
        get => Current.Modifier;
        set => Update(Current with { Modifier = value });
    }

    public WheelAction ChordWheelAction
    {
        get => Current.ChordWheelAction;
        set => Update(Current with { ChordWheelAction = value });
    }

    public bool ShowWheelTooltips
    {
        get => Current.ShowWheelTooltips;
        set => Update(Current with { ShowWheelTooltips = value });
    }

    public TrayClickAction TrayClickAction
    {
        get => Current.TrayClickAction;
        set => Update(Current with { TrayClickAction = value });
    }

    public TrayWheelBehavior TrayPrimaryWheelAction
    {
        get => Current.TrayPrimaryWheelAction;
        set => Update(Current with { TrayPrimaryWheelAction = value });
    }

    public TrayWheelBehavior TrayChordWheelAction
    {
        get => Current.TrayChordWheelAction;
        set => Update(Current with { TrayChordWheelAction = value });
    }

    private GlobalInteractionSettings Current => _configuration.Current.Interaction;

    /// <summary>
    /// 创建交互页视图模型，并订阅设置变更与界面语言变化。
    ///
    /// 本页的下拉框选项名由 XAML 的动态资源提供，切换语言时页面自己就会换字；订阅语言变化是为了让绑定与
    /// 这些设置派生属性在同一时机重新求值，避免留下半页旧文案。缓存作用域释放时退订。
    /// Creates the interaction view model and subscribes to settings changes and interface-language changes.
    ///
    /// The drop-down option names on this page come from XAML dynamic resources and follow a language change on their own;
    /// subscribing here makes the bindings re-evaluate on the same cue as these setting-derived properties, so no half of the
    /// page is left in the old text. Both subscriptions are released with the page scope.
    /// </summary>
    /// <param name="localization">界面语言服务：本页在它变化后刷新自己产出的文案。/ The interface-language service, whose change this page follows to refresh its own text.</param>
    public InteractionViewModel(LocalizationService localization, ISettingsConfiguration configuration)
    {
        _localization = localization;
        _configuration = configuration;
        configuration.Activated += OnActivated;
        SettingsManager.SettingsChanged += OnSettingsChanged;
        _localization.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);

    private void Update(GlobalInteractionSettings settings)
    {
        _configuration.SetInteractionSettings(settings.Normalize());
        RaiseAll();
    }

    public void ResetInteraction() => _configuration.Reset(SettingsResetScope.Interaction);

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (_disposed || !_configuration.IsActive) return;
        if (e.ResetScope is SettingsResetScope.Interaction or SettingsResetScope.All)
            RaiseAll();
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(ArtworkClickAction));
        OnPropertyChanged(nameof(TextClickAction));
        OnPropertyChanged(nameof(ArtworkHoverMode));
        OnPropertyChanged(nameof(PrimaryWheelAction));
        OnPropertyChanged(nameof(Modifier));
        OnPropertyChanged(nameof(ChordWheelAction));
        OnPropertyChanged(nameof(ShowWheelTooltips));
        OnPropertyChanged(nameof(TrayClickAction));
        OnPropertyChanged(nameof(TrayPrimaryWheelAction));
        OnPropertyChanged(nameof(TrayChordWheelAction));
    }
    private void OnActivated(object? sender, EventArgs e) { RaiseAll(); }
    /// <summary>Releases settings and language subscriptions for this cached editor.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SettingsManager.SettingsChanged -= OnSettingsChanged;
        _localization.LanguageChanged -= OnLanguageChanged;
        _configuration.Activated -= OnActivated;
    }

}
