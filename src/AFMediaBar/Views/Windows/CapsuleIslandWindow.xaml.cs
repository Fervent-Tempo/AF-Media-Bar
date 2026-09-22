using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Appearance;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Services.Layout;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Resources;
using AFMediaBar.ViewModels.Windows;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace AFMediaBar.Views.Windows;

/// <summary>
/// 胶囊式灵动岛窗口：空闲收拢为胶囊（封面缩略图 + 滚动歌名 + 播放暂停），
/// 悬停或点击形变展开为卡片（大封面、标题、作者、进度条 seek、上一首/播放暂停/下一首）。
/// 透明区域（圆角外）通过 WM_NCHITTEST 返回 HTTRANSPARENT 实现鼠标穿透；拖拽结束自动吸附屏幕边缘。
/// Capsule-style dynamic-island window: collapses to a capsule (thumbnail + scrolling title + play/pause) when idle and
/// morphs into a card (large artwork, title, artist, seek slider, transport controls) on hover or click.
/// Transparent regions outside the rounded corners pass clicks through via WM_NCHITTEST/HTTRANSPARENT;
/// dragging ends with edge snapping.
/// </summary>
public partial class CapsuleIslandWindow : Window
{
    private const int WM_NCHITTEST = 0x0084;
    private const int HTTRANSPARENT = -1;
    private const double DragStartThresholdDip = 4;

    private readonly MainWindowViewModel _viewModel;
    private readonly DispatcherTimer _progressTimer;
    private readonly DispatcherTimer _collapseTimer;
    private MediaSnapshot _snapshot = MediaSnapshot.Disconnected;
    private HwndSource? _windowSource;
    private bool _isClosing;
    private bool _isPressed;
    private bool _dragStarted;
    private Point _pressPoint;
    private Point _pressWindowPos;
    private bool _isSeeking;
    /// <summary>
    /// 歌名槽是否处于"曾经由跑马灯驱动"的状态。歌名**不再滚动**：过长的歌名由 <c>CapsuleTitle</c> 的
    /// <c>CharacterEllipsis</c> 在右缘吃掉多出来的字（用户裁定：歌名显示不全无所谓，优先显示歌词）。
    /// 这个标记保留下来只是让既有调用点继续走同一条"复位"路径，不再有任何逐帧推进。
    /// Whether the title slot is in the state the marquee used to drive. The title **no longer scrolls**: an over-long title is trimmed at the
    /// right edge by <c>CapsuleTitle</c>'s <c>CharacterEllipsis</c> (the user's ruling: a partially shown title is fine, lyrics come first).
    /// The flag survives only so the existing call sites keep taking the same "reset" path; nothing advances per frame any more.
    /// </summary>
    private bool _marqueeActive;

    /// <summary>
    /// 胶囊歌名槽的回落文本（"歌名 - 作者"，无媒体时是本地化的"暂无媒体"），由 <c>ApplySnapshot</c> / <c>ApplyPlaceholder</c> 写入。
    ///
    /// **快照不再直接写 <c>CapsuleTitle.Text</c>**：文字槽在歌词态下显示的是**跟随窗口**（一行歌词的一个后缀），快照每约 300ms 来一次，
    /// 直接写那一格会把窗口整条覆盖成歌名、字幕跳回行首，直到下一帧才恢复。因此回落文本先进这个字段，由槽的主人
    /// （<c>RefreshCapsuleSlot</c>，歌名模式时）落到控件上。
    /// The capsule title slot's fallback text ("title - artist", or the localized "no media" without a session), written by
    /// <c>ApplySnapshot</c> / <c>ApplyPlaceholder</c>.
    ///
    /// **A snapshot no longer writes <c>CapsuleTitle.Text</c> directly**: in lyric mode that cell shows a **follow window** (a suffix of one lyric
    /// line), and a snapshot arrives roughly every 300 ms — writing the cell straight away would replace the whole window with the song title and
    /// snap the subtitle back to the line's head until the next frame. The fallback therefore lands in this field first, and the slot's owner
    /// (<c>RefreshCapsuleSlot</c>, in title mode) writes it into the control.
    /// </summary>
    private string _capsuleFallbackTitle = string.Empty;

    private bool _isPlaceholder;
    private Point _normalizedCenter = new(0.5, 0);

    /// <summary>
    /// 灵动岛随屏幕等比例缩放的系数（<see cref="CapsuleIslandScalePolicy.ResolveScale"/>），默认 1.0 = 基准尺寸。
    /// 在 Loaded 与 WM_DPICHANGED 时重算，形态动画的两端尺寸/圆角/字号都读它。
    /// Screen-proportional scale factor for the island (<see cref="CapsuleIslandScalePolicy.ResolveScale"/>); the default of
    /// 1.0 is the baseline size. Recomputed on Loaded and WM_DPICHANGED, and read by the morph's target sizes, radii, and
    /// font sizes.
    /// </summary>
    private double _islandScale = 1.0;

    /// <summary>XAML 里写的基准字号（元素 → 基准值），首次缩放时缓存，避免每次重算都拿已缩放的值再乘一遍。
    /// The baseline font sizes declared in XAML, captured on the first scaling pass so a recompute never multiplies an
    /// already-scaled value again.</summary>
    private readonly Dictionary<DependencyObject, double> _baseFontSizes = [];

    /// <summary>
    /// 创建胶囊岛窗口并挂接媒体视图模型。
    /// Creates the capsule-island window bound to the shared media view model.
    /// </summary>
    /// <param name="viewModel">媒体视图模型：岛内播放控制、媒体源切换与设置/退出命令都来自它。/ Media view model: the in-island playback controls plus the media-source, settings, and exit commands come from it.</param>
    /// <param name="audioInteractionService">设备与当前应用音量的共用操作边界。/ Shared device and current-application-volume action boundary.</param>
    /// <param name="compactFlyoutFactory">紧凑菜单工厂：与任务栏悬停层共用同一份浮层实现。/ Compact-menu factory: the same flyout implementation the taskbar hover layer uses.</param>
    /// <param name="appearanceService">窗口外观服务：右键菜单的材质与主题接入。/ Window appearance service: material and theme wiring for the context menu.</param>
    /// <param name="interactionRouter">滚轮执行器：岛内滚轮与任务栏走同一个实例，动作槽位因此完全同源。/ Wheel executor: the island's wheel shares one instance with the taskbar, so the action slots come from the very same place.</param>
    /// <param name="mouseInputMonitor">全局鼠标监听：滚轮修饰键状态与组合滚轮合成点击的一次性标记都来自它。/ Global mouse monitor: the source of both the wheel modifier state and the one-shot chord-wheel click flag.</param>
    /// <param name="screenBackgroundSampler">屏幕背景采样器：自动前景色按岛内实际背景改判时用它。/ Screen background sampler, used when the automatic foreground is re-decided from the island's real background.</param>
    /// <param name="audioMonitorService">容器单例的音频回环采集：胶囊波形（编号 107）的频段数据由它提供。/ The container's audio loopback capture singleton, which supplies the capsule waveform's band data (item 107).</param>
    public CapsuleIslandWindow(
        MainWindowViewModel viewModel,
        AudioInteractionService audioInteractionService,
        Func<TaskbarCompactFlyoutWindow> compactFlyoutFactory,
        WindowAppearanceService appearanceService,
        GlobalInteractionRouter interactionRouter,
        NativeMouseInputMonitor mouseInputMonitor,
        ScreenBackgroundSampler screenBackgroundSampler,
        AudioMonitorService audioMonitorService)
    {
        _viewModel = viewModel;
        _audioInteractionService = audioInteractionService;
        // 采集服务**只注入、不新建**：自己 new 一个会另开一份 WASAPI 回环采集（接缝 S9），任务栏与岛各占一份毫无意义。
        // The capture service is **injected, never constructed**: building one here would open a second WASAPI loopback capture (seam S9), and the
        // taskbar and the island each holding one is pointless.
        _audioMonitorService = audioMonitorService;
        WindowHelper.SetNoActivate(this);
        InitializeComponent();

        // 输入与自适应前景各自成半（见 CapsuleIslandWindow.Input.cs / .Foreground.cs）：这里只把容器单例转交给它们。
        // Input and adaptive foreground each own a half (see CapsuleIslandWindow.Input.cs / .Foreground.cs); this only hands the
        // container singletons over to them.
        InitializeIslandInput(interactionRouter, mouseInputMonitor);
        InitializeIslandForeground(screenBackgroundSampler);
        // 内容树的圆角裁剪（见 CapsuleIslandWindow.Surface.cs）：圆角外的像素必须真正透明，否则连接媒体后药丸两端会露出方角与方块阴影。
        // The content tree's rounded clip (see CapsuleIslandWindow.Surface.cs): the pixels outside the corners have to be genuinely
        // transparent, or the pill shows square tips and a rectangular shadow as soon as media connects.
        InitializeIslandSurfaceClip();

        _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _progressTimer.Tick += (_, _) => AdvanceProgress();

        // 收起确认用一次性定时器：形变过程中的假 MouseLeave 由它吸收，见 Window_MouseLeave。
        // One-shot timer for collapse confirmation: it absorbs the false MouseLeave events a morph produces — see Window_MouseLeave.
        _collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _collapseTimer.Tick += (_, _) => ConfirmCollapse();

        // Slider 的拖动事件是 Thumb 上的路由事件，在窗口级用附加路由订阅，避免依赖模板查找。
        // Thumb drag events are routed through the slider's thumb; subscribe at window level via attached routed events.
        AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(SeekDragStarted), handledEventsToo: true);
        AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(SeekDragCompleted), handledEventsToo: true);

        // 尚无任何快照时（例如刚从任务栏模式切过来、又恰好没有媒体会话）也必须落到占位态，否则窗口是一片空白。
        // With no snapshot yet — for example right after switching from taskbar mode when there happens to be no media
        // session — the placeholder state is required, otherwise the window is simply blank.
        Loaded += (_, _) =>
        {
            if (!_snapshot.IsConnected)
                ApplyPlaceholder();

            // 顺序不能反：形态落位会把窗口锚到"起点中心"，而起点中心取自当时的 Left/Top。先落位再恢复位置，
            // 锚点就停在 (0,0) 上，随后的 KeepShapeAnchor 会把窗口算到 0 - 宽/2 并被夹回 0——岛跑到屏幕左上角。
            // 必须先恢复位置，再算缩放并落位。
            // The order matters: landing a shape anchors the window to its starting centre, which is read from the current
            // Left/Top. Landing first leaves that anchor at (0,0), and the following KeepShapeAnchor computes 0 − width/2
            // and gets clamped back to 0 — the island jumps to the top-left corner of the screen. The position has to be
            // restored first, and only then is the scale resolved and the shape landed.
            RestoreSavedPosition();

            // 位置复位之后再算缩放：ApplyIslandScale 会落到缩放后的目标尺寸并按新尺寸重新夹取工作区。
            // The scale is resolved after the position is restored: ApplyIslandScale lands on the scaled target size and
            // re-clamps into the work area at that size.
            ApplyIslandScale();

            // 歌词再刷一次：ApplyIslandScale 只调换了字号，文本没变，呈现因此不会重写；而占位/已有歌词的落位要按新字号重算。
            // The lyrics are refreshed once more: ApplyIslandScale only swaps the font size, so the text stays and the presentation is
            // not rewritten, while the fallback/lyric landing has to be recomputed against the new size.
            UpdateLyricLine(_snapshot.Position);
        };

        // 右键菜单与紧凑音频浮层都在 XAML 装载之后接上：菜单需要窗口资源（字体与菜单底色），
        // 浮层则要在岛已经能提供屏幕锚点之后再可能被打开。
        // The context menu and the compact audio flyout are wired after the XAML is loaded: the menu needs window
        // resources (fonts and the menu surface colour), and the flyout can only be opened once the island can supply a
        // screen anchor.
        InitializeIslandMenu(appearanceService);
        InitializeCompactFlyout(compactFlyoutFactory);
    }

    /// <summary>初始化窗口句柄钩子。/ Installs the window message hook.</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _windowSource = (HwndSource)PresentationSource.FromDependencyObject(this);
        _windowSource.AddHook(WindowProc);
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCHITTEST)
        {
            // 圆角外的透明像素判定为穿透，桌面与下层窗口可正常接收点击。
            // Pixels outside the rounded corners are pass-through so the desktop below stays clickable.
            var screenX = (short)(lParam.ToInt64() & 0xFFFF);
            var screenY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
            Point local;
            try
            {
                local = PointFromScreen(new Point(screenX, screenY));
            }
            catch (InvalidOperationException)
            {
                return IntPtr.Zero;
            }

            var radius = RootSurface.CornerRadius.TopLeft;
            if (!RoundedRectHitTest.Contains(ActualWidth, ActualHeight, radius, local.X, local.Y))
            {
                handled = true;
                return (IntPtr)HTTRANSPARENT;
            }

            return IntPtr.Zero;
        }

        if (msg == NativeMethods.WM_DPICHANGED)
        {
            // 显示器或 DPI 变化后按归一化中心恢复到新工作区，再夹回工作区内——只夹不恢复会让窗口在换显示器后
            // 跑到角落，而不是留在用户放它的相对位置上。
            // After a monitor or DPI change the window returns to the normalized center of the new work area and is then
            // clamped: clamping alone would park it in a corner instead of the relative spot the user put it in.
            Dispatcher.BeginInvoke(RestorePositionForCurrentDpi, DispatcherPriority.ContextIdle);
            // 换显示器会换掉工作区，缩放系数必须跟着重算（岛的大小是按新屏幕的等比例来的）。
            // A monitor change swaps the work area, so the scale factor has to be recalculated with it: the island's size
            // is proportional to the screen it lands on.
            Dispatcher.BeginInvoke(ApplyIslandScale, DispatcherPriority.ContextIdle);
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// 应用不可变媒体快照：刷新封面、标题、作者、进度与按钮状态。
    /// Applies an immutable media snapshot: refreshes artwork, title, artist, progress, and button states.
    /// </summary>
    public void ApplySnapshot(MediaSnapshot snapshot)
    {
        Dispatcher.Invoke(() =>
        {
            _snapshot = snapshot;
            _isPlaceholder = !snapshot.IsConnected;
            if (_isPlaceholder)
            {
                ApplyPlaceholder();
                return;
            }

            Visibility = Visibility.Visible;
            // 有媒体（无论是否在播放）就撤掉占位：音符图标收起、封面图重新参与布局。
            // Any media session — playing or not — leaves the placeholder: the note icon goes away and the artwork
            // image takes part in layout again.
            _isPlaceholder = false;
            ApplyArtworkVisibility();
            CapsuleArtwork.Source = snapshot.Artwork;
            CardArtwork.Source = snapshot.Artwork;
            // 封面模糊底与任务栏媒体栏同源：同一个封面，透出表面下方形成同一块播放器表面。
            // The blurred backdrop shares its source with the taskbar media bar: the same artwork, showing through the
            // surface so both read as one player surface.
            BackdropImage.Source = snapshot.Artwork;
            // 回落文本进字段而不是直接写控件：文字槽的主人可能是歌词槽的跟随窗口（见字段的文档）。
            // The fallback goes into the field rather than straight into the control: the text cell may be owned by the lyric slot's follow window
            // (see the field's documentation).
            _capsuleFallbackTitle = string.IsNullOrWhiteSpace(snapshot.Artist)
                ? snapshot.Title
                : $"{snapshot.Title} - {snapshot.Artist}";
            CardTitle.Text = string.IsNullOrWhiteSpace(snapshot.Title) ? "—" : snapshot.Title;
            CardArtist.Text = snapshot.Artist;
            SetPlayPauseIcons(snapshot.IsPlaying);
            CardPrevious.IsEnabled = snapshot.CanSkipPrevious;
            CardNext.IsEnabled = snapshot.CanSkipNext;
            CardPlayPause.IsEnabled = snapshot.CanPlayPause;

            var canSeek = snapshot.CanSeek && snapshot.Duration > 0;
            SeekSlider.IsEnabled = canSeek;
            SeekSlider.Maximum = Math.Max(1, snapshot.Duration);
            SeekTotalText.Text = FormatTime(snapshot.Duration);
            if (!_isSeeking)
                UpdateSeekUi(snapshot.Position);

            // 每来一份新快照都确保帧回调存在：歌词时间轴需要逐帧推进时，帧循环**必须**挂着，不能依赖形变或跑马灯顺手挂上。
            //
            // 这条不变量有代价教训：卡片已展开时从"暂停"切回"播放"，中间不会有形变，标题也通常不溢出（走不到 MeasureMarquee 末尾的挂接），
            // 于是没有任何东西会把刚刚被摘掉的帧回调重新挂上——擦亮不出现，歌词行也永远停在暂停那一刻那一句，直到指针离开再悬停才恢复。
            // EnsureFrameLoop 自身有 _isFrameLoopAttached / _isClosing 守卫，重复调用安全；若这一帧其实不需要推进，
            // 同一帧收尾的 StopFrameLoopWhenIdle 会立刻把回调摘掉，因此不会长期空转。
            // Every new snapshot ensures the frame callback exists: while the lyric timeline needs a frame per step the loop **must** be
            // attached, and it must never depend on a morph or the marquee happening to attach it as a side effect.
            //
            // That invariant has a known failure behind it: resuming playback while the card is already expanded involves no morph and the
            // title usually does not overflow (so MeasureMarquee's attach at the end is never reached), leaving nothing to re-attach the
            // callback the pause just detached — the reveal never appears and the lyric row stays on the line it showed when the pause
            // happened, until the pointer leaves and hovers again. EnsureFrameLoop guards itself with _isFrameLoopAttached/_isClosing, so
            // repeating it is safe, and if nothing needs advancing this frame the same frame's StopFrameLoopWhenIdle detaches it at once,
            // which keeps an idle loop from idling for long.
            //
            // 这里还要先刷新一次歌词与文字槽：暂停时帧循环会被摘掉，若文字槽只在帧循环里刷新，"暂停中切歌"就永远换不掉那一行。
            // The lyrics and the text slot are also refreshed here first: the frame loop is detached while paused, so a text slot refreshed only from
            // the loop would never change the line when the track is switched while paused.
            UpdateLyricLine(snapshot.Position);

            EnsureFrameLoop();
            Dispatcher.BeginInvoke(MeasureMarquee, DispatcherPriority.Loaded);
            _progressTimer.Start();
        });
    }

    /// <summary>
    /// 没有媒体会话时的占位胶囊：清空封面、显示音符图标与本地化"暂无媒体"文案、禁用播放按钮，
    /// 并停掉跑马灯与进度定时器；窗口保持可见，不再整窗收起。
    /// Placeholder capsule for "no media session": clears the artwork, shows a note icon and the localized "no media" text,
    /// disables the play button, and stops the marquee and progress timers. The window stays visible instead of collapsing.
    ///
    /// 占位标记在这里置位：<see cref="ApplySnapshot"/> 之外（窗口刚加载、还没有任何快照）也走这条路，
    /// 若只在快照里置位，封面与音符图标的显隐就会停在默认状态——空封面槽里既没有图也没有图标。
    /// The placeholder flag is set here: this path is also taken outside <see cref="ApplySnapshot"/> (the window has just
    /// loaded and no snapshot has arrived yet), and setting it only from the snapshot would leave the artwork and note
    /// icon at their defaults — an empty cover slot with neither image nor icon.
    /// </summary>
    private void ApplyPlaceholder()
    {
        Visibility = Visibility.Visible;
        _isPlaceholder = true;

        CapsuleArtwork.Source = null;
        CardArtwork.Source = null;
        // 无封面时模糊底也要清掉，否则会把上一首的封面留在表面底下。
        // The blurred backdrop is cleared as well: otherwise the previous track's artwork would linger under the surface.
        BackdropImage.Source = null;

        // 占位文案同样进**回落字段**、由文字槽的主人落到控件上：无媒体时的槽是歌名槽，但它与歌词槽共用同一格，
        // 直接写那一格会和歌词槽的跟随窗口打架（见 _capsuleFallbackTitle 的文档）。
        // The placeholder text goes into the **fallback field** as well and reaches the control through the slot's owner: without a session the slot
        // is the title one, but the two share one cell, and writing it directly would fight the lyric slot's follow window (see the documentation of
        // _capsuleFallbackTitle).
        _capsuleFallbackTitle = Translations.Get("Panel.FullPanel.NoMedia");
        CardTitle.Text = Translations.Get("Panel.FullPanel.NoMedia");
        CardArtist.Text = string.Empty;
        // 取词与文字槽一起刷新：占位态意味着没有歌词文档，槽因此一定回到歌名模式并把上面那句文案写进去。
        // The selection and the text slot refresh together: the placeholder state means there is no lyric document, so the slot returns to title mode and
        // writes the text above into the control.
        UpdateLyricLine(0);

        CardPlayPause.IsEnabled = false;
        CardPrevious.IsEnabled = false;
        CardNext.IsEnabled = false;
        SeekSlider.IsEnabled = false;
        SeekSlider.Value = 0;
        SeekCurrentText.Text = FormatTime(0);
        SeekTotalText.Text = FormatTime(0);

        _progressTimer.Stop();

        // 占位文案同样要过一遍歌名复位：它也可能长于胶囊可视宽度，此时由文本的省略号在右缘截断。
        // The placeholder text goes through the same title reset: it can also be wider than the capsule, in which case the text's ellipsis trims
        // it at the right edge.
        Dispatcher.BeginInvoke(MeasureMarquee, DispatcherPriority.Loaded);

        // 切到占位态时同步一次形态尺寸：卡片高度已包含歌词行，占位态展开同样要按新尺寸呈现。
        // Re-sync the shape on entering the placeholder state: the card now carries a lyric row, so the expanded
        // placeholder has to be laid out at the new size as well.
        ApplyShapeTarget();
    }

    /// <summary>
    /// 应用灵动岛外观设置：背景样式/不透明度与圆角都由 <c>DynamicIslandSurface</c> 决定；
    /// 明暗判定与任务栏模式主窗口（<c>TaskBarMediaControl.ApplyAppearanceSettings</c>）取同一个来源，
    /// 表面颜色交给 <see cref="CapsuleIslandSurfacePolicy"/>，圆角在形态动画中按当前尺寸解析。
    /// Applies dynamic-island appearance settings: the background style/opacity and corner radius both come from
    /// <c>DynamicIslandSurface</c>; the light/dark decision comes from the same source as the taskbar-mode host
    /// (<c>TaskBarMediaControl.ApplyAppearanceSettings</c>) so the two windows agree, the surface colour is resolved by
    /// <see cref="CapsuleIslandSurfacePolicy"/>, and the radius is resolved from the current size inside the shape animation.
    /// </summary>
    public void ApplyAppearanceSettings()
    {
        var surface = SettingsManager.Current.DynamicIslandSurface;
        RootSurface.Background = new SolidColorBrush(ResolveIslandSurfaceColor(surface));
        // iOS 观感没有描边：纯黑表面自带边界，外加一圈半透明白边会把它读成"带框的深色方块"。
        // The iOS look has no outline: a pure black surface already reads as its own edge, and a translucent white ring
        // turns it into "a dark box with a frame".
        RootSurface.BorderBrush = null;
        RootSurface.BorderThickness = new Thickness(0);
        ApplyShapeTarget();

        // 外观一变前景就要重算：表面色、背景样式与前景模式都可能是刚刚改的那一项，采样门控也随它们开合。
        // A change of appearance re-decides the foreground: the surface colour, the background style, and the foreground mode may all be
        // the thing that just changed, and the sampling gate opens and closes with them.
        RequestIslandForegroundRefresh();
    }

    /// <summary>
    /// 重算缩放系数并落到当前形态：工作区（灵动岛所在显示器）→ <see cref="CapsuleIslandScalePolicy.ResolveScale"/>，
    /// 再按系数设置目标尺寸、圆角、胶囊视图尺寸与基准字号。
    ///
    /// DPI/显示器变化时调用；**不打断进行中的形变**——正在跑的那一段动画按它开始时读到的系数走完，这里只是把新系数
    /// 记下来（没有动画在跑时立刻落到新尺寸）。动画途中换系数会让两端尺寸不一致，看起来就是卡一下再跳。
    /// Recomputes the scale factor and lands on the current form: work area of the island's monitor through
    /// <see cref="CapsuleIslandScalePolicy.ResolveScale"/>, then the factor drives the target size, the radii, the capsule
    /// view's size, and the baseline font sizes.
    ///
    /// Called on DPI / monitor changes, and it deliberately **does not interrupt a running morph**: the in-flight animation
    /// finishes with the factor it read when it started, and only the new factor is recorded here (a settled form lands on
    /// the new size immediately). Swapping the factor mid-animation would give the two ends different targets, which reads
    /// as a stutter followed by a jump.
    /// </summary>
    private void ApplyIslandScale()
    {
        if (_isClosing)
            return;

        var workArea = GetCurrentWorkArea();
        _islandScale = CapsuleIslandScalePolicy.ResolveScale(workArea);
        AppLogService.Current?.Info("IslandScale",
            $"工作区 workArea=({workArea.Left},{workArea.Top}) {workArea.Width}x{workArea.Height} scale={_islandScale:0.####} " +
            $"pos=({Left},{Top}) size={Width}x{Height} expanded={_isExpanded}");
        ApplyFontScales();
        if (!_isShapeAnimating)
            ApplyShapeTarget();
    }
    /// <summary>
    /// 按当前缩放系数设置所有文本与图标的字号：基准值取 XAML 里写的 FontSize（首次调用时缓存），乘系数后取整到 0.5。
    /// Sets every text and icon font size from the current scale factor: the baseline is the XAML FontSize (cached on the
    /// first call) and the scaled value is rounded to the nearest 0.5.
    /// </summary>
    private void ApplyFontScales()
    {
        foreach (var element in new DependencyObject[]
        {
            // 歌词三层必须一起缩放：底色层与高亮层是叠在同一格上的，字号一旦不同（scale=1.2889 时 15.5 对 12），
            // 字形宽度与基线就会分叉，逐字擦亮的裁剪边界再也对不上字。
            // All three lyric layers scale together: the base layer and the reveal layer are stacked in one cell, so a font size difference
            // (15.5 against 12 at scale=1.2889) splits their glyph widths and baselines and the reveal's clip edge stops matching the glyphs.
            CapsuleTitle, CardTitle, CardArtist, CardLyric, CardLyricHighlight, CardLyricSecondary, SeekCurrentText, SeekTotalText,
            // 胶囊歌词槽的两层同样必须一起缩放：它们叠在同一格上，字号一旦不同，字形宽度与基线就会分叉，逐字擦亮的裁剪边界再也对不上字
            // （编号 105/106 的擦亮层就在这两层之间）。
            // The capsule lyric slot's two layers have to scale together as well: they are stacked in one cell, so a font size difference splits their
            // glyph widths and baselines and the reveal's clip edge stops matching the glyphs (items 105/106 put the reveal layer between them).
            CapsuleLyricText, CapsuleLyricHighlight,
            CapsulePlaceholderIcon, CardPlaceholderIcon,
            CardPreviousIcon, CardPlayPauseIcon, CardNextIcon, CardOutputDeviceIcon, CardVolumeIcon,
        })
        {
            // TextBlock 必须写全名：Wpf.Ui.Controls 也有一个同名类型，只写 TextBlock 是不明确的引用。
            // TextBlock has to be spelled out: Wpf.Ui.Controls declares a type of the same name, so the short form is ambiguous.
            if (element is not System.Windows.Controls.Control &&
                element is not System.Windows.Controls.TextBlock)
                continue;

            if (!_baseFontSizes.TryGetValue(element, out var baseline))
                _baseFontSizes[element] = baseline = ReadFontSize(element);

            // 取整到 0.5：像素级的小数字号在 WPF 里会被逐字取整，行宽反而更抖。
            // Rounded to 0.5: sub-pixel font sizes get rounded per glyph inside WPF, which makes line widths jitter instead.
            WriteFontSize(element, Math.Round(baseline * _islandScale * 2, MidpointRounding.AwayFromZero) / 2);
        }

        // 字号变了就把歌词的前缀宽度表与亮区宽度一起作废：前缀宽度表虽然自带字形键（字号|字重|字体族）会自行重测，
        // 但**亮区宽度是按旧字号的字形宽度算出来的 DIP**，不归零就会与新字号的前缀宽度表对不上（位移与裁剪会跳一小段）。
        // 这里是岛内唯一写字号的地方（DPI/显示器变化也走它），因此这一处复位就是完整的触发点。
        // A font-size change discards the lyric prefix-width table and the reveal width together: the table carries its own glyph key
        // (size|weight|family) and re-measures itself, but the **reveal width is a DIP value derived from the old font size's glyph widths**, and
        // leaving it would disagree with the new table (the offset and the clip would jump a little). This is the island's only font-size writer
        // (DPI and monitor changes come through it too), so this one reset is the complete trigger.
        _capsuleLyricWidths = CapsuleLyricFollowPolicy.EmptyTable;
        _capsuleLyricRevealWidth = 0;
    }

    private static double ReadFontSize(DependencyObject element) => element switch
    {
        System.Windows.Controls.TextBlock text => text.FontSize,
        System.Windows.Controls.Control control => control.FontSize,
        _ => 0,
    };

    private static void WriteFontSize(DependencyObject element, double size)
    {
        switch (element)
        {
            case System.Windows.Controls.TextBlock text:
                text.FontSize = size;
                break;
            case System.Windows.Controls.Control control:
                control.FontSize = size;
                break;
        }
    }

    /// <summary>
    /// 灵动岛表面颜色，判定顺序与任务栏模式主窗口一致：旧设置 Transparent 时表面近乎全透（露出封面模糊底），
    /// 高对比度取系统窗口色，其余交给策略按**应用主题**解析。
    /// The island surface colour, decided in the same order as the taskbar-mode host: the legacy Transparent setting
    /// leaves the surface almost fully see-through (showing the blurred artwork beneath it), high contrast takes the
    /// system window colour, and everything else resolves through the policy against the **application** theme.
    /// </summary>
    private static Color ResolveIslandSurfaceColor(ModeSurfaceSettings surface)
    {
        if (SettingsManager.Current.DynamicIslandBackgroundMode == DynamicIslandBackgroundMode.Transparent)
            return Color.FromArgb(0x01, 0x00, 0x00, 0x00);

        if (SystemParameters.HighContrast)
            return SystemColors.WindowColor;

        return CapsuleIslandSurfacePolicy.ResolveSurfaceColor(
            surface.Style,
            surface.BackgroundOpacityPercent,
            IsApplicationThemeDark(),
            SystemParameters.WindowGlassColor);
    }

    /// <summary>
    /// 应用主题是否为深色；主题未知时回退到 Windows 主题。与 <c>TaskBarMediaControl</c> 的判定逐句相同。
    /// Whether the application theme is dark, falling back to the Windows theme while it is still unknown. Line for line
    /// the same decision the taskbar-mode host makes.
    /// </summary>
    private static bool IsApplicationThemeDark()
    {
        var applicationTheme = ApplicationThemeManager.GetAppTheme();
        if (applicationTheme != ApplicationTheme.Unknown)
            return applicationTheme == ApplicationTheme.Dark;

        WindowsThemeDetector.GetWindowsTheme(out var windowsTheme, out _);
        return windowsTheme == WindowsThemeDetector.ThemeMode.Dark;
    }

    /// <summary>
    /// 按设置里的圆角解析某一形态的实际半径：配置值非法/为 0 时回落到该形态的默认半径。
    /// Resolves one form's actual radius from the configured dip: an invalid or zero setting falls back to that form's
    /// default radius.
    /// </summary>
    /// <param name="configuredDip">设置里的圆角（DIP）。/ Configured corner radius in DIP.</param>
    /// <param name="fallbackDip">该形态的默认圆角（DIP）。/ This form's default corner radius in DIP.</param>
    /// <param name="width">该形态宽度（DIP）。/ This form's width in DIP.</param>
    /// <param name="height">该形态高度（DIP）。/ This form's height in DIP.</param>
    private static double ResolveCornerRadius(double configuredDip, double fallbackDip, double width, double height) =>
        CapsuleIslandSurfacePolicy.ResolveCornerRadius(
            configuredDip > 0 ? configuredDip : fallbackDip,
            width,
            height);

    /// <summary>释放定时器与消息钩子。/ Releases timers and the message hook.</summary>
    protected override void OnClosed(EventArgs e)
    {
        _isClosing = true;
        _collapseTimer.Stop();
        _progressTimer.Stop();
        // 采样在后台线程上跑，窗口关掉后不得再回写前景，计时器也必须停掉。
        // Sampling runs on a background thread and must not write a foreground back once the window is gone; its timer stops too.
        DisposeIslandForegroundSampling();
        StopShapeAnimation();
        // 音频浮层随岛一起消失：它由岛持有，且它的延迟应用计时器不能在窗口已经关掉之后还往系统写值。
        // The audio flyout goes away with the island: the island owns it, and its deferred-apply timers must not write to
        // the system after the window is gone.
        DisposeCompactFlyout();
        if (_windowSource is not null)
        {
            _windowSource.RemoveHook(WindowProc);
            _windowSource = null;
        }
        base.OnClosed(e);
    }

    #region 鼠标交互 / Mouse interaction

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || _isClosing)
            return;

        // 交互控件（按钮、滑杆及其滑块）自行处理，不进入拖拽。
        // Interactive controls (buttons, slider and its thumb) handle themselves and never start a drag.
        if (e.OriginalSource is DependencyObject source &&
            (FindAncestor<System.Windows.Controls.Primitives.ButtonBase>(source) is not null ||
             FindAncestor<Slider>(source) is not null ||
             FindAncestor<Thumb>(source) is not null))
        {
            return;
        }

        _isPressed = true;
        _dragStarted = false;
        _pressPoint = PointToScreen(e.GetPosition(this));
        _pressWindowPos = new Point(Left, Top);
        // 按下这一刻定下起点区域：抬起时窗口内容可能已经变了（形变、切歌），只有按下时的命中才是用户真正点到的那一块。
        // The starting area is fixed at press time: by the release the window's content may already have changed (a morph, a track
        // change), and only the hit taken when the button went down describes what the user actually clicked.
        CapturePressSurface(e.OriginalSource as DependencyObject);
        CaptureMouse();
    }

    private void Window_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPressed || _isClosing)
            return;

        var current = PointToScreen(e.GetPosition(this));
        var delta = current - _pressPoint;
        if (!_dragStarted &&
            !CapsuleIslandInteractionPolicy.ExceedsDragThreshold(delta.X, delta.Y, DragStartThresholdDip))
        {
            return;
        }

        if (!_dragStarted)
        {
            _dragStarted = true;
            _isPinned = false;
            Expand(animated: true);
        }

        Left = _pressWindowPos.X + delta.X;
        Top = _pressWindowPos.Y + delta.Y;
    }

    private void Window_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // 合成点击抑制的**唯一**消费点，因此放在最前面：标记是一次性的，任何早退路径都必须先把它取走，
        // 否则它会留到 750ms 之后过期，期间真正的一次点击会被误吞。取走的结果存进字段，媒体动作与固定态两条判定都只读它。
        // The **only** consumption point of the chord-wheel suppression, hence at the very top: the flag is one-shot and every early
        // exit has to take it away first, or it would sit there until it expired 750 ms later and swallow a real click in the meantime.
        // The result is stored in a field that both the media-action and the pin decision merely read.
        _suppressClickThisPress = _mouseInputMonitor is { } monitor && monitor.ConsumeSuppressedClick();

        // 按压起点在这里**读一次并立刻清零**，而且必须在 ReleaseMouseCapture() 之前：释放捕获会**同步**触发
        // LostMouseCapture，那里也会复位起点（见 .Input.cs 的 InitializeIslandInput），先释放再读就永远读到"空白处"，
        // 媒体动作永不成立。清零之后这一次抬起不可能被执行两次，也不会有残留值漏给下一次按压。
        // The press's starting area is **taken once and cleared at once** here, and it has to happen before ReleaseMouseCapture():
        // releasing capture **synchronously** raises LostMouseCapture, which resets the starting area too (see InitializeIslandInput in
        // .Input.cs) — releasing first would make every read answer "blank" and no media action could ever run. After the clear this
        // release can neither run twice nor leak a leftover into the next press.
        var (pressOnMediaAction, pressOnArtwork) = _pressSurface.Take();

        if (!_isPressed)
            return;

        _isPressed = false;
        ReleaseMouseCapture();
        if (_dragStarted)
        {
            _dragStarted = false;
            // 拖拽结束后解除固定：拖到的位置就是用户想要的位置，岛不该继续钉在展开态。
            // A finished drag releases the pin: the island now sits where the user dropped it and must not stay pinned open.
            _isPinned = false;
            SaveDraggedPositionAndEdge();
        }
        // 双击的第二次不重复执行绑定：左键抬起在双击里会来两次，封面上的"切歌/暂停"连着执行两遍会互相抵消，
        // 看起来就是"点了没反应"；单击与双击因此都只落实一次绑定。
        // The second click of a double click does not run the binding again: a double click delivers two left-button releases, and
        // running "skip / play-pause" twice in a row cancels itself out, which reads as a click that did nothing. Single and double
        // clicks therefore both land exactly one binding.
        else if (e.ClickCount <= 1)
        {
            // 媒体动作区上的单击执行绑定，并且**不**切换固定态：固定态下指针离开不再收起，而"点封面切歌/暂停"绝不该把岛钉住，
            // 那正是"点一下就卡住不再收起"的来源。卡片空白处（背景、行间空隙、传输控制周围）仍然切换固定。
            // A click on a media-action area runs the binding and deliberately does **not** toggle the pin: a pinned island survives the
            // pointer leaving, and "click the cover to skip or pause" must never pin it open — which is exactly what wedges the island
            // open. Blank areas of the card (background, gaps between rows, around the transport controls) still toggle the pin.
            if (CapsuleIslandClickPolicy.ShouldRunMediaAction(_suppressClickThisPress, pressOnMediaAction))
                ExecuteIslandMediaAction(pressOnArtwork);
            else if (CapsuleIslandClickPolicy.ShouldTogglePin(_suppressClickThisPress, pressOnMediaAction))
                _isPinned = !_isPinned;

            Expand(animated: true);
        }
    }

    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        _collapseTimer.Stop();
        if (!_isExpanded && !_isClosing && !_isDraggingOrPressed)
            Expand(animated: true);
    }

    /// <summary>
    /// 指针离开：先停掉待执行的收起，再判定是否真要收起。
    /// Pointer left: cancels a pending collapse, then decides whether a collapse is really due.
    ///
    /// 收起**不能**在 MouseLeave 里立刻执行：形变自身会让窗口在指针底下改变尺寸，鼠标消息随之再次投递
    /// （指针其实没动），于是展开到一半就被判成"指针已离开"而收起，收起后指针又落回窗口内再次展开——来回打架，
    /// 岛就卡在既收不拢也展不开的中间态。这里用一次性确认：指针确实还在外面，150ms 后才收。
    /// The collapse must **not** run inline from MouseLeave: the morph itself resizes the window under a stationary
    /// pointer, which makes WPF deliver mouse messages again, so a half-finished expansion gets read as "the pointer left"
    /// and collapses — after which the pointer is inside again and it expands — and the island wedges in between. A
    /// one-shot confirmation collapses only after the pointer has really stayed outside for 150 ms.
    /// </summary>
    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_isClosing)
            return;

        _collapseTimer.Stop();
        // 收起判定走纯逻辑：与固定态、拖拽态一并决定，窗口不再各写一份条件。
        // The collapse decision goes through the pure policy so pinning and dragging are decided in one place instead of
        // being re-tested here.
        if (CapsuleIslandInteractionPolicy.ShouldCollapse(_isExpanded, _isPinned, IsCursorWithinWindow(), _isDraggingOrPressed))
            _collapseTimer.Start();
    }

    /// <summary>延时确认期结束：指针仍未回来才真正收起。/ The confirmation window elapsed: collapse only if the pointer has not come back.</summary>
    private void ConfirmCollapse()
    {
        _collapseTimer.Stop();
        if (CapsuleIslandInteractionPolicy.ShouldCollapse(_isExpanded, _isPinned, IsCursorWithinWindow(), _isDraggingOrPressed))
            Collapse(animated: true);
    }

    private bool _isDraggingOrPressed => _isPressed;

    /// <summary>
    /// 指针是否落在**当前形态按布局落位后**的矩形里（圆角外算外）。
    ///
    /// 判定必须用 <see cref="FrameworkElement.Width"/> / <see cref="FrameworkElement.Height"/>（随形变逐帧更新的动画值）
    /// 与 <see cref="Window.Left"/> / <see cref="Window.Top"/> 换算，**不能**用 <c>ActualWidth/ActualHeight</c>：
    /// 后两者是上一次布局的结果，形变过程中滞后于窗口真实尺寸，于是窗口一改尺寸就触发鼠标事件、把"指针其实还在窗口里"
    /// 误判成"指针已离开"，展开/收起来回打架，胶囊就卡住收不回去了。
    /// Whether the pointer is inside the rectangle of the **current form as laid out** (outside the rounded corners counts as
    /// outside). The test uses <see cref="FrameworkElement.Width"/>/<see cref="FrameworkElement.Height"/> — the per-frame
    /// animation values — together with <see cref="Window.Left"/>/<see cref="Window.Top"/>, and deliberately **not**
    /// <c>ActualWidth/ActualHeight</c>: those lag one layout pass behind during a morph, so the size change itself fires
    /// mouse events and a pointer that is still inside gets read as having left, which makes expand and collapse fight each
    /// other and wedges the capsule open.
    /// </summary>
    private bool IsCursorWithinWindow()
    {
        if (!NativeMethods.GetCursorPos(out var cursor) || double.IsNaN(Left) || double.IsNaN(Top))
            return false;

        var localX = cursor.X - Left;
        var localY = cursor.Y - Top;
        return RoundedRectHitTest.Contains(Width, Height, RootSurface.CornerRadius.TopLeft, localX, localY);
    }

    #endregion

    #region 播放控制 / Playback controls

    private void PlayPause_Click(object sender, RoutedEventArgs e) =>
        Execute(_viewModel.TogglePlayPauseCommand);

    private void Previous_Click(object sender, RoutedEventArgs e) =>
        Execute(_viewModel.SkipPreviousCommand);

    private void Next_Click(object sender, RoutedEventArgs e) =>
        Execute(_viewModel.SkipNextCommand);

    private void SeekDragStarted(object sender, DragStartedEventArgs e) => _isSeeking = true;

    private void SeekDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _isSeeking = false;
        Execute(_viewModel.SeekCommand, SeekSlider.Value);
    }

    private static void Execute(ICommand command) => Execute(command, null);

    private static void Execute(ICommand command, object? parameter)
    {
        if (command.CanExecute(parameter))
            command.Execute(parameter);
    }

    private void SetPlayPauseIcons(bool isPlaying)
    {
        // 只有卡片那一枚图标：紧凑态的那枚随按钮一起删掉了（悬停即展开，它不可达，见 CapsuleIslandWindow.xaml 的列注释）。
        // Only the card's icon remains: the compact one was removed together with its button (hover already expands the island, so it
        // was unreachable — see the column comment in CapsuleIslandWindow.xaml).
        CardPlayPauseIcon.Symbol = isPlaying ? SymbolRegular.Pause24 : SymbolRegular.Play24;
    }

    private void UpdateSeekUi(double positionSeconds)
    {
        var value = Math.Clamp(positionSeconds, 0, SeekSlider.Maximum);
        SeekSlider.Value = value;
        SeekCurrentText.Text = FormatTime(value);
    }

    /// <summary>
    /// 播放中本地插值推进进度显示（快照 Position + TimelineUpdatedAt 起算），seek 拖拽期间暂停刷新。
    /// 歌词行不再随这里刷新：逐字擦亮要逐帧推进，卡片歌词因此改由合成帧回调统一驱动（见 <c>CapsuleIslandWindow.Lyrics.cs</c>），
    /// 两个刷新源写同一份文本会互相覆盖。
    /// Advances the progress display locally while playing (from Position + TimelineUpdatedAt); paused during seek drags.
    /// The lyric rows are deliberately no longer refreshed from here: the reveal advances per displayed frame, so the card's lyrics are
    /// driven by the compositor callback alone (see <c>CapsuleIslandWindow.Lyrics.cs</c>); two refresh sources writing the same text
    /// would overwrite each other.
    /// </summary>
    private void AdvanceProgress()
    {
        if (_isClosing || _isSeeking || !_snapshot.IsConnected || _snapshot.Duration <= 0)
            return;

        var position = _snapshot.IsPlaying
            ? _snapshot.Position + (DateTimeOffset.UtcNow - _snapshot.TimelineUpdatedAt).TotalSeconds
            : _snapshot.Position;
        UpdateSeekUi(position);
    }

    private static string FormatTime(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
            return "0:00";
        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes}:{span.Seconds:00}";
    }

    #endregion

    #region 歌名跑马灯 / Title marquee

    /// <summary>
    /// 测量胶囊歌名宽度：文本超出容器时启用跑马灯滚动，否则停在零位。
    /// Measures the capsule title: enables marquee scrolling when the text overflows its host and leaves it at zero
    /// otherwise.
    ///
    /// 文本宽度必须取**无约束测量**的结果：`CapsuleTitle` 处在 Grid 的 Star 列里会被拉伸，它的 `ActualWidth`
    /// 只是容器宽度，拿它比较等于永远不溢出——这正是跑马灯从不滚动的原因。
    /// The text width has to come from an **unconstrained measure**: `CapsuleTitle` sits in a star column and is stretched,
    /// so its `ActualWidth` is merely the container width and comparing that can never overflow — which is exactly why the
    /// marquee never scrolled.
    /// </summary>
    private void MeasureMarquee()
    {
        // 歌名槽不再滚动：超出文字槽时由 CapsuleTitle 的 CharacterEllipsis 在右缘吃掉多出来的字，这一格永远不会有
        // 任何位移或裁剪被写坏。这份方法保留下来只做"第二份收起、位移归零"这一件事——它挂着的几个调用点
        // （快照、占位、从歌词槽切回）原本就是复位时机，去掉调用点反而会让那几处少一次兜底。
        // The title slot no longer scrolls: a title wider than the text cell is trimmed at the right edge by CapsuleTitle's
        // CharacterEllipsis, and nothing in this cell can ever have a broken shift or clip written into it. The method is kept for the one
        // thing it still does — collapsing the second copy and zeroing the shifts — because the call sites it already hangs off (a snapshot, the
        // placeholder, the switch back from the lyric slot) are exactly the reset moments, and removing them would only drop a safety net.
        if (_capsuleSlotMode == CapsuleSlotMode.Lyric)
            return;

        ResetMarquee();
    }

    /// <summary>把歌名槽停在静止态：第二份收起、位移归零、滚动标记清掉。/ Rests the title slot: the second copy collapses, the shifts go to zero, and the scrolling flag is cleared.</summary>
    private void ResetMarquee()
    {
        _marqueeActive = false;
        CapsuleTitleLoop.Visibility = Visibility.Collapsed;
        CapsuleTitleTransform.X = 0;
        CapsuleTitleLoopTransform.X = 0;
        StopFrameLoopWhenIdle();
    }

    #endregion

    #region 位置 / Positioning

    /// <summary>
    /// 恢复保存的胶囊岛位置，然后一律把胶囊水平居中到工作区顶部（见 <see cref="CenterStartupPosition"/>）。
    /// Restores the saved island position and then always centres the capsule horizontally at the top of the work area, see
    /// <see cref="CenterStartupPosition"/>.
    /// </summary>
    private void RestoreSavedPosition()
    {
        if (SettingsManager.Current.DynamicIslandLeft is { } left &&
            SettingsManager.Current.DynamicIslandTop is { } top)
        {
            Left = left;
            Top = top;
        }

        ClampToWorkArea();
        if (SettingsManager.Current.DynamicIslandEdgeDocked)
            _dockedEdge = SettingsManager.Current.DynamicIslandEdge;
        CenterStartupPosition(GetCurrentWorkArea());
    }

    /// <summary>
    /// 启动落位：胶囊一律水平居中于所在显示器的工作区顶部。
    ///
    /// 保存位置只用来决定**纵向**（用户把岛放到屏幕哪一高度），横向每次启动都重新居中——用户实测启动后岛偏在顶部左侧，
    /// 偏的正是"用上一次拖拽留下的横向坐标恢复"这一段。贴左右边时该轴本来就是自由轴，居中与贴边语义一致。
    /// Startup landing: the capsule is always centred horizontally at the top of its monitor's work area.
    ///
    /// The saved position only decides the **vertical** placement (which height the user put the island at); the horizontal axis is
    /// re-centred on every launch, because what the user saw was the island sitting left of centre at the top — exactly what restoring
    /// the horizontal coordinate left behind by an earlier drag produces. With the island docked to the left or right edge that axis is
    /// the free one anyway, so centring agrees with the dock.
    /// </summary>
    /// <param name="area">当前显示器工作区（窗口 DIP 坐标）。/ The current monitor's work area in window DIP coordinates.</param>
    private void CenterStartupPosition(Rect area)
    {
        if (_isClosing || !double.IsFinite(area.Width) || area.Width <= 0)
            return;

        // 高度不在这里解析：启动落位是纵向自由的（岛自己会按当前形态的高度锚定），缺的只是横向居中。
        // The height is deliberately not resolved here: the startup landing is free on the vertical axis, where the island anchors itself
        // against the current form's height, and all that is missing is the horizontal centring.
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + CapsuleIslandMetrics.EdgeGapDip;
        CaptureNormalizedCenter();
    }

    /// <summary>把窗口夹回当前显示器工作区内，并记录新的归一化中心。/ Clamps the window into the current monitor work area and records the new normalized center.</summary>
    private void ClampToWorkArea()
    {
        if (_isClosing || double.IsNaN(Left) || double.IsNaN(Top))
            return;

        var position = DynamicIslandPositionCalculator.ClampPosition(
            GetCurrentWorkArea(),
            Width,
            Height,
            new Point(Left, Top));
        Left = position.X;
        Top = position.Y;
        CaptureNormalizedCenter();
    }

    /// <summary>
    /// 记录窗口中心在当前工作区内的归一化坐标：DPI 或显示器变化后靠它把窗口放回相同的相对位置。
    /// Records the window center normalized inside the current work area, which is what puts the window back at the same
    /// relative spot after a DPI or monitor change.
    /// </summary>
    private void CaptureNormalizedCenter()
    {
        if (_isClosing || double.IsNaN(Left) || double.IsNaN(Top))
            return;

        _normalizedCenter = DynamicIslandPositionCalculator.GetNormalizedCenter(
            GetCurrentWorkArea(),
            new Rect(Left, Top, Width, Height));
    }

    /// <summary>
    /// DPI / 显示器变化后按归一化中心恢复到新工作区，再夹取回工作区内。
    /// Restores the position from the normalized center into the new work area after a DPI or monitor change, then clamps it.
    /// </summary>
    private void RestorePositionForCurrentDpi()
    {
        if (_isClosing || double.IsNaN(Left) || double.IsNaN(Top))
            return;

        var position = DynamicIslandPositionCalculator.GetDpiRestoredPosition(
            GetCurrentWorkArea(),
            Width,
            Height,
            _normalizedCenter,
            _dockedEdge);
        Left = position.X;
        Top = position.Y;
        ClampToWorkArea();
    }

    /// <summary>
    /// 拖拽结束后判定贴边吸附并持久化位置与边缘。
    /// Detects edge snap after a drag and persists position and edge.
    /// </summary>
    private void SaveDraggedPositionAndEdge()
    {
        var area = GetCurrentWorkArea();
        var clamped = DynamicIslandPositionCalculator.ClampPosition(
            area,
            Width,
            Height,
            new Point(Left, Top));
        var edge = IslandEdgeSnap.FindEdge(
            area,
            clamped,
            new Size(Width, Height),
            CapsuleIslandMetrics.EdgeSnapThresholdDip);
        var snapped = edge is null ? clamped : SnapToEdge(edge.Value, area, clamped);

        Left = snapped.X;
        Top = snapped.Y;
        SettingsManager.Current.DynamicIslandLeft = snapped.X;
        SettingsManager.Current.DynamicIslandTop = snapped.Y;
        _dockedEdge = edge;
        if (edge is { } dockedEdge)
        {
            SettingsManager.Current.DynamicIslandEdge = dockedEdge;
            SettingsManager.Current.DynamicIslandEdgeDocked = true;
        }
        else
        {
            SettingsManager.Current.DynamicIslandEdgeDocked = false;
        }

        CaptureNormalizedCenter();
    }

    private Point SnapToEdge(DynamicIslandEdge edge, Rect area, Point clamped)
    {
        return edge switch
        {
            DynamicIslandEdge.Top => new Point(clamped.X, area.Top + CapsuleIslandMetrics.EdgeGapDip),
            DynamicIslandEdge.Bottom => new Point(clamped.X, area.Bottom - Height - CapsuleIslandMetrics.EdgeGapDip),
            DynamicIslandEdge.Left => new Point(area.Left + CapsuleIslandMetrics.EdgeGapDip, clamped.Y),
            _ => new Point(area.Right - Width - CapsuleIslandMetrics.EdgeGapDip, clamped.Y),
        };
    }

    /// <summary>
    /// 取当前显示器工作区并换算到窗口 DIP 坐标系（多显示器 DPI 一致性的关键）。
    /// Returns the current monitor's work area converted into window DIP coordinates (multi-monitor DPI consistency).
    /// </summary>
    private Rect GetCurrentWorkArea()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            return SystemParameters.WorkArea;

        var physicalArea = MonitorUtil.GetMonitor(handle).workArea;
        var transformFromDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice;
        var relativeTopLeft = transformFromDevice?.Transform(physicalArea.TopLeft) ?? physicalArea.TopLeft;
        var relativeBottomRight = transformFromDevice?.Transform(physicalArea.BottomRight) ?? physicalArea.BottomRight;
        return new Rect(
            relativeTopLeft.X,
            relativeTopLeft.Y,
            relativeBottomRight.X - relativeTopLeft.X,
            relativeBottomRight.Y - relativeTopLeft.Y);
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
                return match;
            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    #endregion
}
