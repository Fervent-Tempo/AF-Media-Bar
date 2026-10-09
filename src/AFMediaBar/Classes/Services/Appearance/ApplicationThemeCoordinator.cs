using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Classes.Settings;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 协调应用主题、系统主题变化和外观资源重应用。
/// Coordinates application themes, system-theme changes, and appearance-resource reapplication.
/// </summary>
public sealed class ApplicationThemeCoordinator : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Action<AppearanceSettings, ApplicationTheme, AccentPalette> _updateResources;
    private DispatcherTimer? _systemThemeRefreshTimer;
    private AccentPalette? _publishedAccent;
    private ApplicationTheme? _publishedTheme;
    /// <summary>
    /// 上一次已发布资源所依据的外观设置。字体（字体族与粗细）也通过这些资源发布，而它既不属于主题也不属于强调色，
    /// 因此去重条件必须把它算进去，否则只改字体时会被判定为"什么都没变"而整段跳过，字体设置看起来完全无效。
    /// Appearance settings the last published resources were derived from. The typeface (family and weight) is published through
    /// the same resources while belonging to neither the theme nor the accent, so the dedupe has to include it: without it a
    /// font-only change looks like "nothing changed" and the whole publish is skipped, which reads as a font setting that does
    /// nothing.
    /// </summary>
    private AppearanceSettings? _publishedAppearance;
    private bool _isPublishing;
    private bool _started;
    private bool _disposed;

    /// <summary>主题、强调色与外观资源完成更新后，通知组合根重新应用窗口材质。</summary>
    public event Action? AppearanceResourcesApplied;

    /// <summary>
    /// 创建主题协调器。
    /// Creates the application theme coordinator.
    /// </summary>
    /// <param name="dispatcher">WPF UI 调度器 / WPF UI dispatcher.</param>
    /// <param name="updateResources">
    /// 资源更新回调，接收外观设置、当前主题与派生好的强调色调色板。
    /// Resource update callback receiving the appearance settings, the current theme, and the derived accent palette.
    /// </param>
    public ApplicationThemeCoordinator(
        Dispatcher dispatcher,
        Action<AppearanceSettings, ApplicationTheme, AccentPalette> updateResources)
    {
        _dispatcher = dispatcher;
        _updateResources = updateResources;
    }

    /// <summary>
    /// 订阅设置、WPF 主题和系统偏好事件。
    /// Subscribes to settings, WPF theme, and system preference events.
    /// </summary>
    public void Start()
    {
        if (_started || _disposed)
            return;

        _started = true;
        SettingsManager.AppearanceSettingsChanged += OnAppearanceSettingsChanged;
        ApplicationThemeManager.Changed += OnApplicationThemeChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>
    /// 立即应用指定外观设置，并发布当前系统强调色。
    /// Applies the specified appearance settings immediately and publishes the current system accent.
    /// </summary>
    public void Apply(AppearanceSettings appearance)
    {
        if (_disposed)
            return;

        PublishAccent(
            appearance,
            ResolveApplicationTheme(appearance.ApplicationThemeMode),
            ResolveSystemAccent());
    }

    /// <summary>
    /// 解析当前系统强调色。优先取 WPF-UI 已应用的系统强调色，它是用户真正设置的那一支；
    /// <c>DwmGetColorizationColor</c> 返回的是旧式窗口着色值，与"强调色"不是同一个口径，
    /// 直接使用它会让我们的画刷（媒体栏、滑杆、进度条）停在偏蓝的着色值上，而 WPF-UI 的控件已是用户颜色。
    /// Resolves the current system accent, preferring the one WPF-UI applied because that is the color the user actually
    /// chose. <c>DwmGetColorizationColor</c> reports the legacy window-colorization value, which is a different quantity:
    /// using it leaves our brushes (media bar, sliders, progress bars) on a bluish colorization value while WPF-UI's own
    /// controls already show the user's accent.
    /// </summary>
    public static Color ResolveSystemAccent()
    {
        var applied = ApplicationAccentColorManager.SystemAccent;
        if (applied.A > 0 && (applied.R | applied.G | applied.B) != 0)
            return applied;

        try
        {
            var colorization = ApplicationAccentColorManager.GetColorizationColor();
            if (colorization.A > 0 && (colorization.R | colorization.G | colorization.B) != 0)
                return colorization;
        }
        catch
        {
            // DWM 取色在会话切换等场景会失败；此时保留上面那支已应用强调色。
            // The DWM query can fail across session transitions; the applied accent above stays authoritative.
        }

        return applied;
    }

    private void PublishAccent(AppearanceSettings appearance, ApplicationTheme theme, Color systemAccent)
    {
        // 库内的强调色/主题应用会同步回调主题事件，重入必须直接返回，否则会递归重发资源。
        // The library's accent and theme application raise the theme event synchronously; a re-entrant call must return
        // immediately, otherwise the resources would be republished recursively.
        if (_isPublishing)
            return;

        _isPublishing = true;
        try
        {
            PublishAccentCore(appearance, theme, systemAccent);
        }
        finally
        {
            _isPublishing = false;
        }
    }

    private void PublishAccentCore(AppearanceSettings appearance, ApplicationTheme theme, Color systemAccent)
    {
        // 原始强调色有两种来源：跟随系统，或用户在设置里选定的自选色。自选色无法从系统读到，因此 HERE 就是它的唯一入口。
        // The raw accent has two sources: the system, or the colour the user picked in the settings. A custom colour cannot be
        // read from the system, so this is its one and only entry point.
        var requestedAccent = AccentColorPolicy.ResolveRequested(systemAccent, appearance.AccentColorMode, appearance.AccentColor);
        var customAccent = appearance.AccentColorMode == AccentColorMode.Custom;

        // 先让 WPF-UI 按本次的原始强调色重刷它自己的资源，再读取结果：这样"我们的画刷"和"库内控件"必然同色，
        // 也不会再出现库内控件是新色、我们的滑杆还是旧色（或反过来）的分裂。
        // Let WPF-UI refresh its own resources from this pass's raw accent first and then read its result: our brushes and the
        // library's controls are then guaranteed to be the same color instead of one being fresh and the other stale.
        if (customAccent)
        {
            ApplyCustomAccent(requestedAccent, theme);
        }
        else
        {
            ApplicationAccentColorManager.ApplySystemAccent();
            systemAccent = ResolveSystemAccent();
            requestedAccent = systemAccent;
        }

        var dark = theme == ApplicationTheme.Dark ||
                   (theme == ApplicationTheme.HighContrast && SystemParameters.HighContrast);
        var palette = AccentColorPolicy.Build(requestedAccent, dark, SystemParameters.HighContrast);

        // 强调色、主题与外观设置都没变时不做任何重应用：DWM 与系统偏好消息会出现成串重复事件。
        // 外观设置参与判断是必须的：字体只通过这一条资源回调生效。
        // Skip everything when neither the accent, the theme, nor the appearance settings changed: DWM and system-preference
        // messages arrive in bursts. The appearance settings must take part in that comparison, because the typeface only ever
        // reaches the interface through this one resource callback.
        if (_publishedAccent == palette && _publishedTheme == theme && _publishedAppearance == appearance)
            return;

        // 先记录本次结果，再调用可能同步回调的库方法，避免 ApplySystemAccent 触发的主题事件把流程递归回来。
        // Record the result first, then call the library method: it can raise the theme event synchronously, and the
        // re-entrant call must already see the new state.
        _publishedAccent = palette;
        _publishedTheme = theme;
        _publishedAppearance = appearance;

        // 主题应用与强调色应用必须一起发生：WPF-UI 只在主题应用时重建主题字典，
        // 库内控件（开关的基础态、窗口边框、导航选中态）才会重新解析强调色。只调用 ApplySystemAccent 时，
        // 那些控件的悬停/按下态会读到新色而基础态停在旧色（实测：悬停一下开关才显示正确颜色）。
        //
        // 但主题应用 MUST NOT 顺带改强调色（`updateAccent: false`）：实测 `updateAccent: true` 会把刚写进去的自选色刷回系统色
        // （#7C3AED → #5378B1），而 `false` 时自选色与它派生出的 Primary/Secondary 全部保留，系统色那条路径也在它之前刚刷新过。
        // Theme application and accent application must happen together: WPF-UI rebuilds its theme dictionaries only when
        // the theme is applied, which is when its controls (a toggle's base state, the window frame, the navigation
        // selection) re-resolve the accent. Calling only ApplySystemAccent updates their hover and pressed states while the
        // base state keeps the old color — hovering a toggle was the only way to make it look right.
        //
        // The theme application must not touch the accent as well (`updateAccent: false`): with `true` it put a just-written
        // custom colour back to the system one (#7C3AED -> #5378B1), while `false` kept the custom colour together with the
        // Primary/Secondary shades derived from it, and the system path has just refreshed itself one step above.
        ApplicationThemeManager.Apply(theme, WindowBackdropType.None, updateAccent: false);

        _updateResources(appearance, theme, palette);
        if (!_disposed) AppearanceResourcesApplied?.Invoke();
    }

    /// <summary>
    /// 把自选强调色写给 WPF-UI。
    ///
    /// 后两个参数分别是"颜色取自系统 Glass Color"与"这是系统强调色"，自选色两者都不是，因此都传 false：
    /// 传 true 会让库对颜色做 HSV 提亮，用户挑的颜色就不再是屏幕上出现的颜色。
    /// Writes the custom accent to WPF-UI.
    ///
    /// The last two arguments mean "the colour came from the system glass colour" and "this is the system accent"; a custom colour
    /// is neither, so both are false. Passing true would let the library brighten the colour in HSV space, and the colour the user
    /// picked would no longer be the colour on screen.
    /// </summary>
    /// <param name="accent">自选强调色。/ Custom accent colour.</param>
    /// <param name="theme">当前应用主题，库用它挑选深浅档。/ Current application theme, which the library uses to pick the shades.</param>
    private static void ApplyCustomAccent(Color accent, ApplicationTheme theme) =>
        ApplicationAccentColorManager.Apply(accent, theme, systemGlassColor: false, systemAccentColor: false);

    /// <summary>
    /// 将应用主题模式转换为当前有效的 WPF 主题。
    /// Resolves an application theme mode to the current effective WPF theme.
    /// </summary>
    public static ApplicationTheme ResolveApplicationTheme(ApplicationThemeMode mode)
    {
        if (SystemParameters.HighContrast)
            return ApplicationTheme.HighContrast;

        if (mode == ApplicationThemeMode.Automatic)
        {
            WindowsThemeDetector.GetWindowsTheme(out var appTheme, out _);
            return appTheme == WindowsThemeDetector.ThemeMode.Dark
                ? ApplicationTheme.Dark
                : ApplicationTheme.Light;
        }

        return mode == ApplicationThemeMode.Dark
            ? ApplicationTheme.Dark
            : ApplicationTheme.Light;
    }

    private void OnAppearanceSettingsChanged(object? sender, AppearanceSettingsChangedEventArgs e)
    {
        if (_dispatcher.CheckAccess())
        {
            Apply(e.Appearance);
            return;
        }

        _dispatcher.BeginInvoke(() => Apply(e.Appearance));
    }

    private void OnApplicationThemeChanged(ApplicationTheme theme, Color accent) =>
        // WPF-UI 报出的 accent 是它自己那一档派生色，强调色仍以 DWM 原始值为准，因此走完整的 Apply 流程重新解析。
        // WPF-UI reports its own derived accent shade, so the raw DWM value stays authoritative: run the full Apply flow.
        Apply(SettingsManager.Current.Appearance);

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        // 手动主题仍需响应系统强调色、高对比度及透明效果的变化；主题解析会保留用户的模式。
        QueueSystemThemeRefresh();
    }

    private void QueueSystemThemeRefresh()
    {
        if (_disposed || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) return;
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(QueueSystemThemeRefresh, DispatcherPriority.DataBind);
            return;
        }

        _systemThemeRefreshTimer ??= new DispatcherTimer(
            TimeSpan.FromMilliseconds(180),
            DispatcherPriority.DataBind,
            (_, _) =>
            {
                _systemThemeRefreshTimer!.Stop();
                if (!_disposed) Apply(SettingsManager.Current.Appearance);
            },
            _dispatcher);

        _systemThemeRefreshTimer.Stop();
        _systemThemeRefreshTimer.Start();
    }

    /// <summary>
    /// 取消主题事件订阅并停止系统主题刷新计时器。
    /// Unsubscribes theme events and stops the system-theme refresh timer.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        AppearanceResourcesApplied = null;
        SettingsManager.AppearanceSettingsChanged -= OnAppearanceSettingsChanged;
        ApplicationThemeManager.Changed -= OnApplicationThemeChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _systemThemeRefreshTimer?.Stop();
        _systemThemeRefreshTimer = null;
    }
}
