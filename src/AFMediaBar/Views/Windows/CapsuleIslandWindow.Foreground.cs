using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Views.Windows;

/// <summary>
/// 胶囊岛窗口的自适应前景：文字与图标取与任务栏模式**同一份**前景判定，并在自动模式下按岛内实际背景采样改判，
/// 需要时给文字加一层对比阴影。
/// Adaptive foreground half of the capsule-island window: the text and icons take the **same** foreground decision the taskbar-mode host
/// makes, automatic mode re-decides it from the island's real background, and the text gets a contrast shadow when it needs one.
///
/// 岛浮在桌面壁纸上，采样与任务栏不同：任务栏采的是被它盖住的那条任务栏区域，岛采的是**岛内文字与封面区**在屏幕上的实际像素。
/// 采样读的是最终桌面合成结果，因此表面不透明时采到的就是我们自己画的那块表面——门控（<see cref="CapsuleIslandForegroundPolicy"/>）
/// 因此只在"自动模式 + 表面真的透出背景"时才放行，其余情况一律回落到主题判定。会话与计时器的**接法**与
/// <c>DynamicIslandWindow.xaml.cs</c> / <c>TaskbarWindow.xaml.cs</c> 的既有宿主相同：同一个
/// <see cref="AdaptiveForegroundSamplingSession"/> 加一支 1500ms 的 <see cref="DispatcherTimer"/>；
/// 差别在**门控条件**：灵动岛宿主只要求背景设置是 <c>Transparent</c>，岛这里还额外要求表面不透明度低于 100%
/// （岛的表面可以是 1–99% 半透，而灵动岛宿主那种情况下仍会采样）。
/// The island floats over the desktop wallpaper, so its sampling differs from the taskbar's: the taskbar samples the strip it covers,
/// while the island samples the actual on-screen pixels of its own **text and cover areas**. The sampler reads the final desktop
/// composition, so an opaque surface would feed our own paint back — the gate
/// (<see cref="CapsuleIslandForegroundPolicy"/>) therefore only opens in automatic mode with a surface that really shows the background,
/// and falls back to the theme decision everywhere else. The session and timer **wiring** is the same one the existing hosts use
/// (<c>DynamicIslandWindow.xaml.cs</c> / <c>TaskbarWindow.xaml.cs</c>): one <see cref="AdaptiveForegroundSamplingSession"/> plus a
/// 1500 ms <see cref="DispatcherTimer"/>. The **gate condition** is where they differ: the dynamic-island host only requires the
/// background setting to be <c>Transparent</c>, while the island additionally requires a surface opacity below 100% (the island's surface
/// can be 1–99% translucent, a case the dynamic-island host would still sample).
/// </summary>
public partial class CapsuleIslandWindow
{
    private AdaptiveForegroundSamplingSession? _foregroundSamplingSession;
    private DispatcherTimer? _foregroundSamplingTimer;
    private PlayerForegroundDecision? _adaptiveForegroundDecision;

    /// <summary>岛内承载文字的元素，构造后缓存一次（每次刷新都新建数组是白付的分配）。/ The island's text-bearing elements, cached once after construction (building the arrays on every refresh is a needless allocation).</summary>
    private System.Windows.Controls.TextBlock[] _islandForegroundTexts = [];

    /// <summary>岛内的图标元素（Wpf.Ui 的符号图标；它们的前景是 <c>IconElement</c> 自己的属性，不是 <c>TextElement.Foreground</c>），同样缓存一次。/ The island's icon elements (Wpf.Ui symbol icons; their foreground is <c>IconElement</c>'s own property rather than <c>TextElement.Foreground</c>), cached once as well.</summary>
    private Wpf.Ui.Controls.IconElement[] _islandForegroundIcons = [];

    /// <summary>岛内以 <c>Fill</c> 取前景的图形（编号 107 的波形 <c>Path</c>）：<c>Shape</c> 的前景是自己的 <c>Fill</c>，既不是 <c>TextElement.Foreground</c> 也不是 <c>IconElement.Foreground</c>，因此单列一张表。/ The island's shape whose foreground is its <c>Fill</c> (item 107's waveform <c>Path</c>): a <c>Shape</c>'s foreground is its own <c>Fill</c>, neither <c>TextElement.Foreground</c> nor <c>IconElement.Foreground</c>, so it gets its own list.</summary>
    private System.Windows.Shapes.Shape[] _islandForegroundShapes = [];

    /// <summary>需要对比阴影的文字：与任务栏一样只压文字，图标字形不加阴影；缓存一次。/ The texts that take the contrast shadow: as on the taskbar only text is shadowed, never icon glyphs; cached once.</summary>
    private System.Windows.Controls.TextBlock[] _islandShadowedTexts = [];

    /// <summary>
    /// 接上自适应前景：采样会话、1500ms 刷新计时器与首次前景落位。
    /// Wires the adaptive foreground: the sampling session, the 1500 ms refresh timer, and the first landing of the foreground.
    /// </summary>
    /// <param name="screenBackgroundSampler">与任务栏模式共用同一个屏幕采样器（容器单例）。/ The same screen sampler the taskbar mode uses (a container singleton).</param>
    private void InitializeIslandForeground(ScreenBackgroundSampler screenBackgroundSampler)
    {
        // 三个元素清单在构造后缓存一次：它们只由 XAML 生成，运行时不会增删；缓存放在首次前景落位之前。
        // The three element lists are cached once after construction: XAML generates them and nothing adds or removes any of them at
        // runtime, and the cache is filled before the first foreground landing.
        _islandForegroundTexts =
        [
            CapsuleTitle, CardTitle, CardArtist, CardLyric, CardLyricHighlight, CardLyricSecondary,
            // 胶囊歌词槽的两层与歌名同源：它们占的是同一格，分开判断只会在同一块表面上给出两种颜色。
            // The capsule lyric slot's two layers share the title's source: they occupy the same cell, and judging them separately would only produce
            // two colours over one surface.
            CapsuleLyricText, CapsuleLyricHighlight,
            SeekCurrentText, SeekTotalText, CardOutputDeviceIcon, CardVolumeIcon,
        ];
        _islandForegroundIcons =
        [
            CapsulePlaceholderIcon, CardPlaceholderIcon,
            CardPreviousIcon, CardPlayPauseIcon, CardNextIcon,
        ];
        _islandForegroundShapes =
        [
            CapsuleSpectrumWaveform,
        ];
        _islandShadowedTexts =
        [
            CapsuleTitle, CardTitle, CardArtist, CardLyric, CardLyricHighlight, CardLyricSecondary,
            CapsuleLyricText, CapsuleLyricHighlight,
            SeekCurrentText, SeekTotalText,
        ];

        _foregroundSamplingSession = new AdaptiveForegroundSamplingSession(
            screenBackgroundSampler,
            Dispatcher,
            GetAdaptiveForegroundSampleBounds,
            ApplyAdaptiveForegroundDecision);

        // 采样按固定间隔重取：壁纸、窗口下方的其他窗口与显示器都可能变，而指针不动时不会有任何事件提醒我们。
        // Sampling repeats on a fixed interval: the wallpaper, the windows below, and the monitor can all change while a stationary
        // pointer produces no event to tell us about it.
        _foregroundSamplingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _foregroundSamplingTimer.Tick += (_, _) => _foregroundSamplingSession?.RequestRefresh();

        Loaded += (_, _) =>
        {
            _foregroundSamplingTimer.Start();
            RequestIslandForegroundRefresh();
        };

        // 构造完成即落一次位：窗口在做完真正的布局之前也可能被截图或被读到颜色（例如模式刚切换过来的那一帧）。
        // One landing at construction time: the window can be captured or read before it ever finishes a layout pass, for instance on the
        // very frame a mode switch brings it in.
        ApplyPlayerForeground();
    }

    /// <summary>
    /// 按当前设置与自动采样决定刷新前景：门控关闭时把采样决定清掉（前景回落主题判定），门控打开时请求一次采样。
    /// Refreshes the foreground from the current settings and the automatic sample decision: a closed gate drops the sample decision,
    /// so the foreground falls back to the theme, while an open one requests a sample.
    /// </summary>
    private void RequestIslandForegroundRefresh()
    {
        // 先按**现有**决定落一次位：门控关闭时这一次写的是那条可能已经过期的采样决定，紧接着下面的
        // Invalidate(clearDecision: true) 会级联出第二次写（_apply(null)），主题判定由那一次落定。
        // 之所以还要先写这一次：门控**开着**时它保证换外观（表面色/背景样式/前景模式）后立即有正确的颜色可用，
        // 不必等下一次采样回来（采样是异步的，最长要等一个 1500ms 周期）。
        // Land the **current** decision first: with the gate closed this write is the possibly stale sample decision, and the
        // Invalidate(clearDecision: true) right below cascades a second write (_apply(null)) that lands the theme decision.
        // The first write still matters while the gate is **open**: it makes a correct colour available immediately after an appearance
        // change (surface colour, background style, foreground mode) instead of waiting for the next sample, which is asynchronous and
        // can take a whole 1500 ms period.
        ApplyPlayerForeground();

        if (_foregroundSamplingSession is not { } session)
            return;

        if (CapsuleIslandForegroundPolicy.ShouldSample(
                SettingsManager.Current.Appearance.Normalize().PlayerForegroundMode,
                SettingsManager.Current.DynamicIslandBackgroundMode == DynamicIslandBackgroundMode.Transparent,
                SettingsManager.Current.DynamicIslandSurface.BackgroundOpacityPercent,
                Visibility == Visibility.Visible,
                isBusy: _isShapeAnimating || _isPressed))
        {
            Dispatcher.BeginInvoke(session.RequestRefresh, DispatcherPriority.ContextIdle);
            return;
        }

        session.Invalidate(clearDecision: true);
    }

    /// <summary>采样结果到达：决定变了才重写前景（同一条决定反复到达时不碰任何控件）。/ A sample arrived: the foreground is rewritten only when the decision changed, so a repeated decision touches no control.</summary>
    private void ApplyAdaptiveForegroundDecision(PlayerForegroundDecision? decision)
    {
        if (_adaptiveForegroundDecision == decision)
            return;

        _adaptiveForegroundDecision = decision;
        ApplyPlayerForeground();
    }

    /// <summary>
    /// 把前景写进岛内所有文字与图标，并按需加对比阴影。
    ///
    /// 判定与任务栏模式主窗口逐句相同（<c>TaskBarMediaControl.ApplyAppearanceSettings</c>）：高对比度走系统窗口色，其余交给
    /// <see cref="PlayerForegroundPolicy.ResolvePresentation"/>，可见文字二选一（白 / <c>#1C1C1C</c>）。
    /// Writes the foreground into every text and icon on the island and adds the contrast shadow when it is needed.
    ///
    /// The decision is line for line the one the taskbar-mode host makes (<c>TaskBarMediaControl.ApplyAppearanceSettings</c>): high
    /// contrast takes the system window colour, everything else resolves through <see cref="PlayerForegroundPolicy.ResolvePresentation"/>,
    /// and visible text is one of two colours (white or <c>#1C1C1C</c>).
    /// </summary>
    private void ApplyPlayerForeground()
    {
        var presentation = PlayerForegroundPolicy.ResolvePresentation(
            SettingsManager.Current.Appearance.Normalize().PlayerForegroundMode,
            SystemParameters.HighContrast,
            IsApplicationThemeDark(),
            _adaptiveForegroundDecision);

        var foreground = presentation.UsesSystemColors
            ? SystemColors.WindowTextBrush
            : new SolidColorBrush(presentation.UsesLightText ? Colors.White : Color.FromRgb(0x1C, 0x1C, 0x1C));

        // 文字与图标共用同一支前景：它们铺在同一块岛表面上，分开判断只会在同一背景上给出两种颜色。
        // 波形 Path 也在其中（它的"前景"是 Fill）：它同样铺在这块表面上，颜色必须与文字图标同源（裁定 10）。
        // Text, icons, and the waveform Path all share one foreground: they sit on the same island surface, and judging them separately would only
        // produce two colours over one background. The waveform's "foreground" is its Fill, and it has to come from the same source as the text and
        // icons (ruling 10).
        foreach (var text in _islandForegroundTexts)
            text.Foreground = foreground;
        foreach (var icon in _islandForegroundIcons)
            icon.Foreground = foreground;
        foreach (var shape in _islandForegroundShapes)
            shape.Fill = foreground;
        // 柱状/对称柱/像素柱的方块是运行时按样式与段数建的，因此它们另走一张列表（在 EnsureCapsuleSpectrumVisuals 里随视觉树一起重建，
        // 建完立刻调回这里刷新前景）。
        // The bar, mirrored-bar, and pixel blocks are built at run time per style and band count, so they take a separate list that is rebuilt alongside
        // the visual tree in EnsureCapsuleSpectrumVisuals, which calls back into here right after building.
        foreach (var shape in _capsuleSpectrumShapes)
            shape.Fill = foreground;

        ApplyIslandContrastShadow(presentation.NeedsContrastShadow, presentation.UsesLightText);
    }

    /// <summary>
    /// 对比阴影的配方与任务栏逐字相同：模糊半径 1 DIP、偏移 1 DIP（偏移为 0 时模糊副本压在字形正中，笔画边缘会被吃掉，
    /// 看起来就是"文字发虚"）、方向 315、不透明度 0.85，颜色按前景明暗取反；不需要时把 Effect 清成 null。
    /// The contrast-shadow recipe is word for word the taskbar's: a 1 DIP blur, a 1 DIP offset (at zero offset the blurred copy sits on
    /// the glyph strokes and eats their edges, which reads as blurry text), direction 315, opacity 0.85, and a colour opposite to the
    /// foreground; with no shadow needed the effects are cleared to null.
    /// </summary>
    private void ApplyIslandContrastShadow(bool enabled, bool usesLightText)
    {
        Effect? effect = null;
        if (enabled)
        {
            var shadow = new DropShadowEffect
            {
                Color = usesLightText ? Colors.Black : Colors.White,
                BlurRadius = 1,
                ShadowDepth = 1,
                Direction = 315,
                Opacity = 0.85,
                RenderingBias = RenderingBias.Quality,
            };
            shadow.Freeze();
            effect = shadow;
        }

        foreach (var text in _islandShadowedTexts)
            text.Effect = effect;
    }

    /// <summary>
    /// 岛内文字与封面区在屏幕上的物理像素矩形（多块区域的并集），供 <see cref="ScreenBackgroundSampler"/> 稀疏采样。
    /// The physical-pixel rectangle of the island's text and cover areas on screen (the union of several regions), which
    /// <see cref="ScreenBackgroundSampler"/> samples sparsely.
    ///
    /// 换算照抄任务栏的 <c>TryGetPhysicalBounds</c>：<c>PointToScreen</c> + <c>VisualTreeHelper.GetDpi</c>，因为采样器读的是物理像素。
    /// 无媒体占位态同样能取到矩形（占位文字与占位音符就在这两块区域里），占位态的可读性因此与播放态一样被照顾到。
    /// The conversion is copied from the taskbar's <c>TryGetPhysicalBounds</c> (<c>PointToScreen</c> plus
    /// <c>VisualTreeHelper.GetDpi</c>) because the sampler reads physical pixels. The placeholder state resolves a rectangle just as
    /// well — both the placeholder text and the placeholder note sit inside these two regions — so readability is covered with no media
    /// exactly as it is while playing.
    /// </summary>
    private Int32Rect? GetAdaptiveForegroundSampleBounds()
    {
        if (_isClosing ||
            !CapsuleIslandForegroundPolicy.ShouldSample(
                SettingsManager.Current.Appearance.Normalize().PlayerForegroundMode,
                SettingsManager.Current.DynamicIslandBackgroundMode == DynamicIslandBackgroundMode.Transparent,
                SettingsManager.Current.DynamicIslandSurface.BackgroundOpacityPercent,
                Visibility == Visibility.Visible,
                isBusy: _isShapeAnimating || _isPressed))
        {
            return null;
        }

        var regions = new List<Int32Rect>();
        if (_isExpanded)
        {
            AddSampleRegion(CardArtworkSlot, regions);
            AddSampleRegion(CardTitle, regions);
            AddSampleRegion(CardArtist, regions);
            AddSampleRegion(CardLyricContainer, regions);
        }
        else
        {
            AddSampleRegion(CapsuleArtworkSlot, regions);
            AddSampleRegion(CapsuleTitleHost, regions);
        }

        if (regions.Count == 0)
            return null;

        var left = regions.Min(region => region.X);
        var top = regions.Min(region => region.Y);
        var right = regions.Max(region => region.X + region.Width);
        var bottom = regions.Max(region => region.Y + region.Height);
        return new Int32Rect(left, top, right - left, bottom - top);
    }

    /// <summary>把一个已布局元素的屏幕矩形换算成物理像素并加入采样区；元素不可见或尚无有效尺寸（<c>Actual*</c> ≤ 1）时跳过。/ Converts one laid-out element's screen rectangle into physical pixels and adds it; an invisible element or one without a usable size (<c>Actual*</c> ≤ 1) is skipped.
    ///
    /// 注意占位态的空白作者行**不**会被跳过：<c>ApplyPlaceholder</c> 只把 <c>CardArtist.Text</c> 置空，元素仍然可见、仍有实际高度，
    /// 因此它那块表面像素会进入采样区（那里没有文字，采到的就是岛表面本身）。
    /// Note that the placeholder state's empty artist line is **not** skipped: ApplyPlaceholder only clears CardArtist.Text while the element
    /// stays visible with a real height, so its surface pixels do enter the sample region (there is no text there, so the samples read the
    /// island surface itself).</summary>
    private static void AddSampleRegion(System.Windows.FrameworkElement element, List<Int32Rect> regions)
    {
        if (!element.IsVisible || element.ActualWidth <= 1 || element.ActualHeight <= 1 ||
            PresentationSource.FromVisual(element) is null)
        {
            return;
        }

        Point origin;
        try
        {
            origin = element.PointToScreen(new Point(0, 0));
        }
        catch (InvalidOperationException)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(element);
        if (!double.IsFinite(origin.X) || !double.IsFinite(origin.Y))
            return;

        var bounds = new Int32Rect(
            (int)Math.Round(origin.X),
            (int)Math.Round(origin.Y),
            (int)Math.Round(element.ActualWidth * dpi.DpiScaleX),
            (int)Math.Round(element.ActualHeight * dpi.DpiScaleY));
        if (bounds.Width > 1 && bounds.Height > 1)
            regions.Add(bounds);
    }

    /// <summary>停表并释放采样会话（采样在后台线程上跑，窗口关掉后不得再回写）。/ Stops the timer and disposes the sampling session: sampling runs on a background thread and must not write back after the window is gone.</summary>
    private void DisposeIslandForegroundSampling()
    {
        _foregroundSamplingTimer?.Stop();
        _foregroundSamplingSession?.Dispose();
    }
}
