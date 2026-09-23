using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Layout;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;
using AFMediaBar.Resources;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace AFMediaBar.Components
{
    /// <summary>播放器表面滚轮及鼠标按键状态。 / Wheel delta and mouse-button state on a player surface.</summary>
    public sealed class PlayerSurfaceWheelEventArgs(
        int delta,
        bool isShiftDown,
        bool isLeftButtonDown,
        bool isRightButtonDown) : EventArgs
    {
        public int Delta { get; } = delta;
        public bool IsShiftDown { get; } = isShiftDown;
        public bool IsLeftButtonDown { get; } = isLeftButtonDown;
        public bool IsRightButtonDown { get; } = isRightButtonDown;
    }

    /// <summary>
    /// 任务栏媒体控制组件：显示当前播放媒体的信息和封面，响应用户交互。
    /// Taskbar media control component: displays currently playing media info and artwork, responds to user interactions.
    ///
    /// 职责 Responsibilities:
    /// 1. 接收 MediaSnapshot 并更新 UI（标题、艺术家、封面、歌词）
    ///    Receive MediaSnapshot and update UI (title, artist, artwork, lyrics)
    /// 2. 根据任务栏方向（横向/竖向）和大小调整布局
    ///    Adjust layout based on taskbar orientation (horizontal/vertical) and size
    /// 3. 显示当前歌词行（歌词可用时替换标题）
    ///    Display current lyric line (replaces title when lyrics are available)
    /// 4. 处理悬停效果和动画
    ///    Handle hover effects and animations
    ///
    /// ⚠️ 架构约束 Architecture Constraints:
    /// - 此组件只负责 UI 呈现，不包含业务逻辑
    ///   This component is responsible for UI presentation only, no business logic
    /// - 用户操作通过请求事件交给宿主窗口，再由 MainWindowViewModel 执行
    ///   User actions are raised as request events and executed by the host through MainWindowViewModel
    /// - 不直接调用服务，所有数据通过 UpdateSongInfo 方法传入
    ///   Does not call services directly; all data is passed via UpdateSongInfo method
    /// </summary>
    public partial class TaskBarMediaControl : UserControl
    {
        // === 布局渲染引擎 Layout Render Engine ===
        private LayoutRenderEngine? _layoutEngine;
        private WindowMode _currentMode = WindowMode.Taskbar;  // 当前窗口模式 Current window mode

        /// <summary>
        /// 初始化共享媒体控件及其组件级悬停、文本覆盖层和尺寸测量状态。
        /// Initializes the shared media control and its component hover, text-overlay, and size-measurement state.
        /// </summary>
        public TaskBarMediaControl()
        {
            InitializeComponent();

            _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _progressTimer.Tick += (_, _) => UpdateTaskbarProgress();
            _hoverOpenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _hoverOpenTimer.Tick += (_, _) =>
            {
                _hoverOpenTimer.Stop();
                ShowTaskbarHoverLayer();
            };
            _hoverCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _hoverCloseTimer.Tick += (_, _) =>
            {
                _hoverCloseTimer.Stop();
                HideTaskbarHoverLayer();
            };
            // 悬停层收起的兜底：动画回调可能因为渲染时钟停走或动画被顶掉而永远不来，那时悬停层会卡在半途留在屏幕上。
            // 计时器只依赖 Dispatcher，所以无论渲染是否在跑，状态都会被收干净。
            // Fallback for collapsing the hover layer: the animation callback may never arrive when the render clock stops or the animation is
            // replaced, which would leave the layer stuck on screen halfway. The timer only depends on the dispatcher, so the state is
            // cleaned up whether or not rendering runs.
            _hoverHideFallbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _hoverHideFallbackTimer.Tick += (_, _) =>
            {
                _hoverHideFallbackTimer.Stop();
                FinishTaskbarHoverLayerHide();
            };
            // 滚轮提示的按键轮询：只在指针位于媒体栏上时运行，指针一离开就在下一个 tick 自停。
            // The wheel tooltip's key poll: it only runs while the pointer is over the bar and stops itself on the first tick after
            // the pointer leaves.
            _wheelTooltipTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _wheelTooltipTimer.Tick += (_, _) => AdvanceWheelTooltip();
            // 跑马灯按帧推进：位置是连续的小数，窗口字符串只在整数位置跨过时改写，小数部分由渲染变换补上，
            // 因此滚动是连续的（不是一个字一个字地跳），也不依赖动画时钟是否被渲染目标驱动。
            // The marquee advances frame by frame: the position is a continuous fraction, the window string is rewritten only when the
            // integer position crosses, and the fraction is drawn with a render transform. The scroll is therefore continuous instead of
            // jumping one character at a time, and it no longer depends on an animation clock being driven by a rendering target.
            AddMarqueeText(SongTitle);
            AddMarqueeText(SongArtist);
            _marqueeTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = MarqueeTiming.FrameInterval };
            _marqueeTimer.Tick += (_, _) => AdvanceMarqueeStep();
            InitializeWebLyrics();
            // 提示用同一个实例承载，内容随手势与结果实时改写。
            // One tooltip instance carries the text, which is rewritten live as the gesture and its result change.
            _wheelTooltip = new ToolTip { Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
            InteractionSurface.ToolTip = _wheelTooltip;
            // 无媒体时的高频动作是"在音符上滚轮挑播放器"，因此音符也持一个自己的提示实例：只有实例固定，
            // 内容才能在不关闭气泡的情况下改写（见 SetQuickLaunchPreview）。
            // With no media the frequent action is "pick a player by wheeling over the note", so the note carries a tooltip instance of its own:
            // only a fixed instance lets the content change without closing the bubble (see SetQuickLaunchPreview).
            _quickLaunchTooltip = new ToolTip { Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
            SongImageBorder.ToolTip = _quickLaunchTooltip;
            // 进度条计时器按"当前是否处于剪枝档位"启动：窗口可能在档位已经生效时才加载出来（任务栏在屏幕关闭期间重建），
            // 那时直接 Start 会让剪枝白做。
            // The progress timer starts according to whether a prune level is in effect: the window can be loaded while the level already holds —
            // a taskbar rebuilt during a dark screen, for instance — and starting it outright there would undo the prune.
            Loaded += (_, _) => ResumeBackgroundTimers();
            Unloaded += (_, _) =>
            {
                _progressTimer.Stop();
                _hoverOpenTimer.Stop();
                _hoverCloseTimer.Stop();
                _hoverHideFallbackTimer.Stop();
                _wheelTooltipTimer.Stop();
                _marqueeTimer.Stop();
                StopWebLyrics();
                StopMarqueeAnimations();
            };

            // 计时器必须先于布局初始化；布局会立即应用已持久化的悬停层开关。
            // Timers must exist before layout initialization, which immediately applies persisted hover settings.
            InitializeLayoutEngine();
        }

        // === 内部状态缓存 Internal State Cache ===
        private string _actualTitle = string.Empty;   // 实际标题（不含歌词）Actual title (without lyrics)
        private string _actualArtist = string.Empty;  // 实际艺术家 Actual artist

        private bool _isPaused;        // 是否暂停 Whether paused
        private bool _isConnected;
        private bool _isVertical;      // 任务栏是否竖向 Whether taskbar is vertical
        private bool _isSmallTaskbar;  // 是否小任务栏 Whether taskbar is small
        private bool _canPlayPause;
        private bool _canSkipPrevious;
        private bool _canSkipNext;
        private string _lastSizeFingerprint = string.Empty;
        /// <summary>参与跑马灯的文本元素；没有任何一个在推进时计时器必须停止。/ Text elements taking part in the marquee; the timer must be stopped while none of them is advancing.</summary>
        private readonly List<MarqueeTextState> _marqueeTexts = [];
        private readonly DispatcherTimer _marqueeTimer;
        private double _minimumPrimaryLength = 120;
        private readonly DispatcherTimer _progressTimer;
        private readonly DispatcherTimer _hoverOpenTimer;
        private readonly DispatcherTimer _hoverCloseTimer;
        private readonly DispatcherTimer _hoverHideFallbackTimer;

        /// <summary>宿主传达的后台剪枝档位；控件只据此停表，不自行查询电源状态。/ The background prune level the host publishes; the control only stops timers from it and never queries the power state itself.</summary>
        private MemoryPruneLevel _backgroundPruneLevel = MemoryPruneLevel.None;

        /// <summary>兜底收起比退出动画多等的余量，确保正常动画回调先跑。/ Extra margin the fallback waits beyond the exit animation, so the normal animation callback runs first.</summary>
        private static readonly TimeSpan HoverHideFallbackMargin = TimeSpan.FromMilliseconds(150);
        private readonly DispatcherTimer _wheelTooltipTimer;
        private readonly ToolTip _wheelTooltip;

        /// <summary>音符上的快速启动提示；与滚轮提示一样只改内容、不换实例。/ The quick-launch tooltip on the note; like the wheel tooltip its content changes while the instance stays put.</summary>
        private readonly ToolTip _quickLaunchTooltip;
        private WheelGestureSlot? _appliedWheelSlot;
        private bool _wheelResultShown;

        /// <summary>最近一次写入提示的滚轮结果；曲名随媒体变化的结果靠它重新读一次。/ The last wheel result written into the tooltip, used to re-read a title that follows the media.</summary>
        private WheelTooltipResult? _wheelResult;
        private string _songInfoTooltip = string.Empty;
        private MediaSnapshot _snapshot = MediaSnapshot.Disconnected;
        private bool _isTaskbarHoverVisible;
        private PlayerForegroundDecision? _adaptiveForegroundDecision;
        private IReadOnlyList<QuickLaunchEntry> _quickLaunchEntries = Array.Empty<QuickLaunchEntry>();
        private DateTime _suppressSurfaceClickUntilUtc;
        private const double TaskbarPerformanceWidth = 74;
        private const double TaskbarTrailingMargin = 4;
        private const double TaskbarCoveredBlurRadius = 4;
        private const double TaskbarCoveredOpacity = 0.32;

        public event EventHandler? TogglePlayPauseRequested;
        public event EventHandler? SkipPreviousRequested;
        public event EventHandler? SkipNextRequested;
        public event EventHandler? ActivateSourceRequested;
        public event EventHandler? OpenFullPanelRequested;

        /// <summary>
        /// 宿主提供的"这次点击应当被吞掉"查询。组合滚轮（按住鼠标左键或右键再滚动）松键时会合成一次点击，
        /// 用户按下组合键的意图只是滚轮，因此该点击必须被抑制；判定由全局鼠标钩子完成，控件只负责在点击入口询问。
        /// Host-supplied query for "this click has to be swallowed". Releasing the button after a chord wheel synthesizes a click
        /// while the user only meant to scroll, so it has to be suppressed; the global mouse hook makes that call and the control
        /// merely asks at its click entries.
        /// </summary>
        public Func<bool>? SuppressedClickSource { get; set; }
        public event EventHandler? OutputDeviceMenuRequested;
        public event EventHandler<PlayerSurfaceWheelEventArgs>? OutputDeviceWheelRequested;
        public event EventHandler? VolumeMenuRequested;
        public event EventHandler<PlayerSurfaceWheelEventArgs>? VolumeWheelRequested;
        public event EventHandler? QuickLaunchMenuRequested;
        public event EventHandler<PlayerSurfaceWheelEventArgs>? QuickLaunchWheelRequested;
        public event EventHandler? OpenTaskManagerRequested;
        public event EventHandler<PlayerSurfaceWheelEventArgs>? WheelRequested;
        public event EventHandler<MediaBarSizeRequestEventArgs>? DesiredSizeChanged;

        /// <summary>指针进入输出设备按钮，宿主应刷新该按钮提示。 / Pointer entered the output-device button; the host should refresh its tooltip.</summary>
        public event EventHandler? OutputDeviceInfoRequested;

        /// <summary>指针进入音量按钮，宿主应刷新该按钮提示。 / Pointer entered the volume button; the host should refresh its tooltip.</summary>
        public event EventHandler? VolumeInfoRequested;

        /// <summary>当前横向任务栏悬停层和固定组件所需的最小长度。 / Current minimum length required by the horizontal taskbar hover layer and fixed components.</summary>
        public double MinimumPrimaryLength => _minimumPrimaryLength;

        /// <summary>
        /// 返回当前可见文字表面的物理屏幕像素矩形，供宿主采样实际背景。
        /// Returns the physical screen-pixel rectangle of the visible text surface for host background sampling.
        /// </summary>
        /// <remarks>
        /// 频谱表面刻意不参与采样：它自己就画在这个矩形里，而频谱取的是同一个自动前景，把画着前景色的区域喂回策略会形成
        /// 自反馈（亮背景下按白色频谱采样，样本中位数被抬高，决定就会一直停在白色）。性能块参与采样是因为它的底色几乎透明，
        /// 采样到的仍是它背后的实际背景。
        /// The spectrum surface deliberately stays out of the sample: the bars are painted inside that very rectangle and the
        /// spectrum takes the same automatic foreground, so feeding a region covered in the foreground colour back into the
        /// policy would close a feedback loop (sampling white bars over a bright background lifts the median and pins the
        /// decision to white). The performance chip does participate because its background is almost transparent, so the
        /// samples still read the real background behind it.
        /// </remarks>
        public bool TryGetForegroundSampleBounds(out Int32Rect bounds)
        {
            bounds = Int32Rect.Empty;
            if (_isTaskbarHoverVisible)
            {
                return false;
            }

            try
            {
                var regions = new List<Int32Rect>();
                if (_isConnected && TryGetPhysicalBounds(SongInfoSurface, out var mediaBounds))
                    regions.Add(mediaBounds);
                if (TaskbarPerformanceSurface.IsVisible && TryGetPhysicalBounds(TaskbarPerformanceSurface, out var metricBounds))
                    regions.Add(metricBounds);
                if (regions.Count == 0)
                    return false;
                var left = regions.Min(region => region.X);
                var top = regions.Min(region => region.Y);
                var right = regions.Max(region => region.X + region.Width);
                var bottom = regions.Max(region => region.Y + region.Height);
                bounds = new Int32Rect(left, top, right - left, bottom - top);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static bool TryGetPhysicalBounds(FrameworkElement element, out Int32Rect bounds)
        {
            bounds = Int32Rect.Empty;
            if (!element.IsVisible || element.ActualWidth <= 1 || element.ActualHeight <= 1 || PresentationSource.FromVisual(element) is null)
                return false;
            var origin = element.PointToScreen(new Point(0, 0));
            var dpi = VisualTreeHelper.GetDpi(element);
            if (!double.IsFinite(origin.X) || !double.IsFinite(origin.Y)) return false;
            bounds = new Int32Rect((int)Math.Round(origin.X), (int)Math.Round(origin.Y),
                (int)Math.Round(element.ActualWidth * dpi.DpiScaleX),
                (int)Math.Round(element.ActualHeight * dpi.DpiScaleY));
            return bounds.Width > 1 && bounds.Height > 1;
        }

        public void ApplyQuickLaunchEntries(IReadOnlyList<QuickLaunchEntry> entries)
        {
            _quickLaunchEntries = entries.ToArray();
        }

        /// <summary>更新音符的快速启动预览提示。 / Updates the note tooltip with the quick-launch preview.</summary>
        // 文案在指针悬停的这一刻取：控件是可复用的，拿不到依赖注入，而宿主每次悬停都会重新调用这里，
        // 因此语言变化后提示自然跟着变，不需要控件自己订阅语言事件。
        // 提示只改**同一个 ToolTip 实例的内容**，并在这里重新打开气泡：给 SongImageBorder.ToolTip 直接赋字符串会先关掉
        // 已经打开的气泡，而滚轮停下之后指针不会移动，于是"滚一下看候选"的提示根本不会出现（这正是"快速启动没有提示"的原因）。
        // 实现与整条媒体栏的滚轮提示一致。
        // The text is read at the moment the pointer hovers: the control is reusable and receives no dependency injection, and
        // the host calls this again on every hover, so the tooltip follows a language change on its own without the control
        // subscribing to any language event.
        // Only the **content of one ToolTip instance** changes, and the bubble is reopened here: assigning a string straight to
        // SongImageBorder.ToolTip first closes the bubble that is already open, and since the pointer does not move after the wheel stops, the
        // "scroll once and see the candidate" tooltip never appeared at all — which is exactly why quick launch felt like it had no tooltip.
        // This matches the wheel tooltip used for the rest of the bar.
        public void SetQuickLaunchPreview(QuickLaunchEntry entry)
        {
            var text = WheelTooltipPolicy.BuildQuickLaunchPreview(entry.DisplayName);
            if (_quickLaunchTooltip.Content as string != text)
            {
                _quickLaunchTooltip.Content = text;
            }

            ReassertQuickLaunchTooltip();
        }

        /// <summary>
        /// 指针仍在音符上时重新打开快速启动提示气泡。
        ///
        /// 指针不动就没有新的鼠标事件，气泡在内容改写或鼠标按下之后不会自己回来；滚轮预览因此必须每次主动重申一次。
        /// Reopens the quick-launch bubble while the pointer is still over the note.
        ///
        /// A stationary pointer produces no new mouse events, so the bubble does not come back by itself after its content is rewritten or a mouse
        /// button is pressed; a wheel preview therefore has to re-assert it on every step.
        /// </summary>
        private void ReassertQuickLaunchTooltip()
        {
            if (!SongImageBorder.IsMouseOver)
            {
                return;
            }

            _quickLaunchTooltip.PlacementTarget = SongImageBorder;
            if (!_quickLaunchTooltip.IsOpen)
            {
                _quickLaunchTooltip.IsOpen = true;
            }
        }

        /// <summary>
        /// 刷新整条媒体栏的滚轮提示。悬停时说明当前绑定的滚轮动作，按住组合键后换成组合滚轮的动作，
        /// 滚动之后由 <see cref="SetWheelResult"/> 换成刚刚发生的结果。
        /// Refreshes the whole bar's wheel tooltip. While hovering it states the bound wheel action, switches to the chord wheel's
        /// action once the modifier is held, and after a scroll <see cref="SetWheelResult"/> replaces it with the result.
        /// </summary>
        public void RefreshWheelTooltip()
        {
            var slot = ChordWheelHeld ? WheelGestureSlot.Chord : WheelGestureSlot.Primary;
            // 刚滚过的结果要留在屏幕上：只有按键状态真正变了（用户准备用另一个槽位）或指针重新进入时才换回提示。
            // The result of the last scroll stays on screen: the hint only returns once the modifier state actually changed (the user
            // is preparing the other slot) or the pointer re-enters the bar.
            if (_wheelResultShown && slot == _appliedWheelSlot)
                return;

            _appliedWheelSlot = slot;
            _wheelResultShown = false;
            var settings = SettingsManager.Current.Interaction.Normalize();
            SetWheelTooltipText(WheelTooltipPolicy.BuildHint(
                slot,
                settings.Modifier,
                WheelTooltipPolicy.BuildActionName(
                    slot == WheelGestureSlot.Chord ? settings.ChordWheelAction : settings.PrimaryWheelAction)));
        }

        /// <summary>把最近一次滚轮动作的结果写入提示。/ Writes the result of the most recent wheel action into the tooltip.</summary>
        /// <param name="result">滚轮手势结果。/ Wheel gesture result.</param>
        public void SetWheelResult(WheelTooltipResult result)
        {
            _appliedWheelSlot = ChordWheelHeld ? WheelGestureSlot.Chord : WheelGestureSlot.Primary;
            _wheelResultShown = true;
            _wheelResult = result;
            SetWheelTooltipText(WheelTooltipPolicy.BuildResult(result.ActionName, result.Detail));
        }

        /// <summary>
        /// 曲名会随后续快照变化的结果（切歌）在轮询里重新读一次：切歌是异步的，滚动的那一刻会话还没换，
        /// 因此提示要先显示当时的结果，等新会话到达后再改写成真正的曲名。
        /// A result whose title follows later snapshots — a skip — is re-read on every poll: the skip is asynchronous and the session has not
        /// switched yet when the wheel is turned, so the tooltip shows the result right away and rewrites it with the real title once the new
        /// session arrives.
        /// </summary>
        private void RefreshWheelResultDetail()
        {
            if (_wheelResult is not { DetailFollowsMedia: true } result)
            {
                return;
            }

            var title = string.IsNullOrWhiteSpace(_snapshot.Title) ? null : _snapshot.Title;
            if (string.Equals(result.Detail, title, StringComparison.Ordinal))
            {
                return;
            }

            _wheelResult = result with { Detail = title };
            SetWheelTooltipText(WheelTooltipPolicy.BuildResult(result.ActionName, title));
        }

        /// <summary>
        /// 当前是否按住了共用组合键。组合键可能是 Shift，也可能是鼠标左键或右键；任务栏窗口永远不激活、收不到键盘事件，
        /// 因此三种情况都读取 WPF 的实时输入状态，判定规则本身与托盘共用一处。
        /// Whether the shared chord key is currently held. It can be Shift, the left button, or the right button; the taskbar window
        /// never activates and therefore receives no key events, so all three read WPF's live input state while the rule itself is
        /// shared with the tray.
        /// </summary>
        private bool ChordWheelHeld => GlobalWheelGesturePolicy.IsChordHeld(
            SettingsManager.Current.Interaction,
            Keyboard.Modifiers.HasFlag(ModifierKeys.Shift),
            Mouse.LeftButton == MouseButtonState.Pressed,
            Mouse.RightButton == MouseButtonState.Pressed);

        private void SetWheelTooltipText(string text)
        {
            // 媒体信息作为第二段跟在滚轮提示后面：截断的标题与"滚轮会做什么"是悬停同一处时想知道的全部。
            // The media info follows the wheel line as a second paragraph: a truncated title and "what the wheel does" are everything the
            // user wants while hovering that one spot.
            var content = string.IsNullOrWhiteSpace(_songInfoTooltip) ? text : $"{text}\n\n{_songInfoTooltip}";
            if (_wheelTooltip.Content as string != content)
            {
                // 只改同一个 ToolTip 实例的内容：更改 ToolTip 属性本身会先关掉已打开的气泡，而这里恰恰要求
                // "滚一下就看到结果"——内容变化会立刻反映在已经显示出来的气泡上。
                // Only the content of one ToolTip instance changes: reassigning the ToolTip property would first close the open bubble,
                // while what is wanted here is exactly "scroll once and see the result" — a content change shows up in the bubble that is
                // already on screen.
                _wheelTooltip.Content = content;
            }

            ReassertChordWheelTooltip();
        }

        /// <summary>
        /// 组合键是鼠标左键或右键时，按下那一刻会先关掉已经打开的气泡（点击等于"用户要操作了"），
        /// 于是"按住组合键后提示立刻消失、按住滚动也不再出现"。这里在组合键按住期间重新打开气泡，
        /// 并在每次内容变化与每次轮询时重申一次，直到指针离开。
        /// When the chord key is a mouse button, pressing it closes the bubble that was already open (a click means the user is about
        /// to act), which is why the hint vanished the moment the key was held and never came back while scrolling with it. This
        /// reopens the bubble while the chord is held and re-asserts it on every content change and every poll tick, until the pointer
        /// leaves.
        /// </summary>
        private void ReassertChordWheelTooltip()
        {
            if (!ChordWheelHeld || !InteractionSurface.IsMouseOver)
                return;

            _wheelTooltip.PlacementTarget = InteractionSurface;
            if (!_wheelTooltip.IsOpen)
                _wheelTooltip.IsOpen = true;
        }

        /// <summary>记录当前曲目的完整信息，并把它并入媒体栏提示。/ Records the current track's full info and folds it into the bar tooltip.</summary>
        private void UpdateSongInfoTooltip(string? title, string? artist)
        {
            var parts = new List<string>(2);
            if (!string.IsNullOrWhiteSpace(title))
                parts.Add(title.Trim());
            if (!string.IsNullOrWhiteSpace(artist))
                parts.Add(artist.Trim());
            var info = parts.Count == 0 ? string.Empty : string.Join("\n", parts);
            if (info == _songInfoTooltip)
                return;

            _songInfoTooltip = info;
            RefreshWheelTooltip();
        }

        /// <summary>
        /// 指针进入媒体栏时启动滚轮提示的轮询。轮询只做一件事：按键状态变了就把提示换到那个槽位，
        /// 因为"按住 Shift 不动鼠标"不会产生任何鼠标事件。指针离开后第一个 tick 就会自停。
        /// Starts the wheel tooltip's poll when the pointer enters the bar. The poll does one thing: switch the hint to the slot whose
        /// modifier just changed, because "hold Shift without moving the mouse" produces no mouse event at all. It stops itself on
        /// the first tick after the pointer leaves.
        /// </summary>
        private void InteractionSurface_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_wheelTooltipTimer.IsEnabled)
            {
                // 重新进入时先忘掉上一次的结果，否则会带着旧结果显示给用户。
                // Forget the previous result on re-entry, otherwise the user is shown a stale one.
                _appliedWheelSlot = null;
                _wheelResultShown = false;
                RefreshWheelTooltip();
                _wheelTooltipTimer.Start();
            }
        }

        private void AdvanceWheelTooltip()
        {
            if (!InteractionSurface.IsMouseOver)
            {
                _wheelTooltipTimer.Stop();
                _wheelTooltip.IsOpen = false;
                return;
            }

            RefreshWheelResultDetail();
            RefreshWheelTooltip();
            // 组合键按住期间每一次轮询都重申气泡：指针不动时不会有鼠标事件，而按下鼠标键本身会把气泡关掉。
            // Every poll tick re-asserts the bubble while the chord is held: a stationary pointer produces no mouse events, and pressing
            // a mouse button itself closes the bubble.
            ReassertChordWheelTooltip();
        }

        /// <summary>
        /// 刷新输出设备按钮提示；文本与托盘图标提示来自同一策略，指针悬停与滚轮预览都经过这里。
        /// Refreshes the output-device button tooltip; the text comes from the same policy as the tray icon tooltip, and
        /// both hover and wheel previews go through it.
        /// </summary>
        public void SetOutputDevicePreview(AudioDeviceOption device) =>
            TaskbarDeviceButton.ToolTip = AudioTooltipPolicy.BuildOutputDevice(device);

        /// <summary>提示当前没有可用输出设备。 / Indicates that no output device is available.</summary>
        public void SetOutputDeviceUnavailable() =>
            TaskbarDeviceButton.ToolTip = AudioTooltipPolicy.BuildOutputDevice(null);

        /// <summary>
        /// 刷新音量按钮提示；滚轮预览的候选值也走这里，因此提示总是先于延迟应用更新。
        /// Refreshes the volume button tooltip; wheel-preview candidates go through it too, so the tooltip always updates
        /// before the deferred apply.
        /// </summary>
        public void SetVolumePreview(int volume)
        {
            // 音量可读但媒体快照暂时没有来源名时给出通用标签，而不是谎报“不可用”。
            // When the volume is readable but the media snapshot has no source name yet, use a generic label instead of
            // reporting the value as unavailable.
            var sourceName = string.IsNullOrWhiteSpace(_snapshot.SourceName)
                ? Translations.Get("Panel.Volume.CurrentMedia")
                : _snapshot.SourceName;
            TaskbarVolumeButton.ToolTip = AudioTooltipPolicy.BuildMediaVolume(sourceName, volume);
        }

        /// <summary>提示当前媒体没有可匹配音频会话。 / Indicates that the current media has no matching audio session.</summary>
        public void SetVolumeUnavailable() =>
            TaskbarVolumeButton.ToolTip = AudioTooltipPolicy.BuildMediaVolume(null, null);

        /// <summary>返回快速启动菜单的物理屏幕锚点。 / Returns the physical screen anchor for the quick-launch menu.</summary>
        public TrayIconBounds GetQuickLaunchAnchor() => GetScreenBounds(SongImageBorder);

        /// <summary>返回输出设备菜单的物理屏幕锚点。 / Returns the physical screen anchor for the output-device menu.</summary>
        public TrayIconBounds GetOutputDeviceAnchor() => GetScreenBounds(TaskbarDeviceButton);

        /// <summary>返回音量菜单的物理屏幕锚点。 / Returns the physical screen anchor for the volume menu.</summary>
        public TrayIconBounds GetVolumeAnchor() => GetScreenBounds(TaskbarVolumeButton);

        private static TrayIconBounds GetScreenBounds(FrameworkElement element)
        {
            var point = element.PointToScreen(new Point(0, 0));
            var dpi = VisualTreeHelper.GetDpi(element);
            return new TrayIconBounds(
                (int)Math.Round(point.X),
                (int)Math.Round(point.Y),
                (int)Math.Round(point.X + element.ActualWidth * dpi.DpiScaleX),
                (int)Math.Round(point.Y + element.ActualHeight * dpi.DpiScaleY));
        }

        public void ApplyPerformanceText(string text, bool canOpenTaskManager)
        {
            TaskbarPerformanceText.Text = text;
            var cursor = canOpenTaskManager ? Cursors.Hand : Cursors.Arrow;
            TaskbarPerformanceSurface.Cursor = cursor;
            TaskbarPerformanceHoverSurface.Cursor = cursor;
        }

        /// <summary>
        /// 判断一次命中的元素是否位于性能组件内部。宿主的拖动逻辑按「左键按下是否落在媒体动作上」决定要不要开始拖动，
        /// 而性能组件本身不是媒体动作，因此它必须能单独被识别出来。
        /// Reports whether a hit element lies inside the performance component. The host's drag logic decides whether to start
        /// a drag from whether the press landed on a media action, and the performance component is deliberately not one, so it
        /// has to be recognisable on its own.
        /// </summary>
        /// <param name="source">鼠标事件给出的原始命中元素。/ Original hit element reported by the mouse event.</param>
        public bool IsPerformanceComponentClick(DependencyObject? source)
        {
            while (source is not null)
            {
                if (ReferenceEquals(source, TaskbarPerformanceSurface) ||
                    ReferenceEquals(source, TaskbarPerformanceHoverSurface))
                {
                    return true;
                }

                source = VisualTreeHelper.GetParent(source);
            }

            return false;
        }

        /// <summary>
        /// 请求宿主打开任务管理器。抬起事件会被宿主的拖动预览处理器标记为已处理，因此组件自身声明的 MouseLeftButtonUp
        /// 永远不会执行——这也是该功能此前点不开的原因。
        /// Requests the host to open Task Manager. The mouse-up event is marked handled by the host's drag preview handler, so a
        /// MouseLeftButtonUp declared on the component itself never runs, which is why the feature never worked before.
        /// </summary>
        public void RequestOpenTaskManager() => OpenTaskManagerRequested?.Invoke(this, EventArgs.Empty);

        /// <summary>
        /// 应用宿主从实际屏幕背景解析出的自动前景决定；null 恢复主题回退。
        /// Applies the automatic foreground decision resolved from the real screen background; null restores theme fallback.
        /// </summary>
        public void ApplyAdaptiveForegroundDecision(PlayerForegroundDecision? decision)
        {
            if (_adaptiveForegroundDecision == decision)
                return;
            _adaptiveForegroundDecision = decision;
            ApplyAppearanceSettings();
        }

        /// <summary>
        /// 初始化布局渲染引擎：将 MainBorder 和 BackgroundImage 传入引擎以便动态调整布局。
        /// Initialize layout render engine: pass MainBorder and BackgroundImage to engine for dynamic layout adjustment.
        /// </summary>
        private void InitializeLayoutEngine()
        {
            _layoutEngine = new LayoutRenderEngine(
                mainBorder: MainBorder,
                contentCanvas: MainCanvas,
                backgroundImage: BackgroundImage,
                artworkBorder: SongImageBorder,
                songInfoPanel: SongInfoStackPanel,
                artworkPlaceholder: SongImagePlaceholder,
                songTitle: SongTitle,
                songArtist: SongArtist,
                songTitleContainer: SongTitleContainer,
                songArtistContainer: SongArtistContainer,
                lyricsHost: SongLyricsPanel
            );

            // 应用默认布局（任务栏横向）
            // Apply default layout (taskbar horizontal)
            ApplyLayout(WindowMode.Taskbar, LayoutOrientation.Horizontal);
        }

        /// <summary>
        /// 应用布局：根据窗口模式和方向选择并应用对应的布局配置。
        /// Apply layout: select and apply corresponding layout config based on window mode and orientation.
        /// </summary>
        /// <param name="mode">窗口模式（任务栏/灵动岛）/ Window mode (taskbar/dynamic island)</param>
        /// <param name="orientation">布局方向（横向/竖向）/ Layout orientation (horizontal/vertical)</param>
        public void ApplyLayout(WindowMode mode, LayoutOrientation orientation)
        {
            ApplyLayout(
                mode,
                orientation,
                SettingsManager.Current.LayoutLengthScalePercent,
                SettingsManager.Current.LayoutThicknessScalePercent);
        }

        /// <summary>
        /// 使用宿主解析后的缩放百分比应用布局。
        /// Applies layout with scale percentages resolved by the host.
        /// </summary>
        /// <param name="mode">窗口模式 / Window mode</param>
        /// <param name="orientation">布局方向 / Layout orientation</param>
        /// <param name="lengthScalePercent">主轴长度缩放百分比 / Primary-axis length scale percentage</param>
        /// <param name="thicknessScalePercent">横轴厚度缩放百分比 / Cross-axis thickness scale percentage</param>
        public void ApplyLayout(
            WindowMode mode,
            LayoutOrientation orientation,
            double lengthScalePercent,
            double thicknessScalePercent)
        {
            _currentMode = mode;

            // 从预设中获取布局
            // Get layout from presets
            var layout = LayoutPresets.GetLayout(mode, orientation);

            // 字号设置只作用于任务栏静置层的媒体文字；灵动岛沿用预设字号。
            // The font-size setting only scales taskbar rest-layer media text; the island keeps its preset sizes.
            var mediaFontScale = mode == WindowMode.Taskbar
                ? SettingsManager.Current.TaskbarExperience.Normalize().MediaFontSizePercent / 100.0
                : 1.0;

            // 应用布局
            // Apply layout
            _layoutEngine?.ApplyLayout(
                layout,
                lengthScalePercent / 100.0,
                thicknessScalePercent / 100.0,
                mediaFontScale);

            // 更新内部状态标志以保持兼容
            // Update internal state flags to maintain compatibility
            _isVertical = orientation == LayoutOrientation.Vertical;
            ApplyTaskbarExperienceSettings();
            ApplyTaskbarSectionGeometry(MainBorder.Width);
            RaiseDesiredSizeChanged();
        }

        /// <summary>
        /// 获取当前应用的布局配置。
        /// Get currently applied layout configuration.
        /// </summary>
        public LayoutSchema? CurrentLayout => _layoutEngine?.CurrentLayout;

        /// <summary>应用自动计算的主轴长度。/ Applies an auto-calculated primary-axis length.</summary>
        public void ApplyPrimaryLength(double primaryLength)
        {
            _layoutEngine?.ApplyPrimaryLength(primaryLength);
            ApplyTaskbarSectionGeometry(primaryLength);
        }

        /// <summary>
        /// 在宿主更新方向、缩放或字号状态后强制重新发布尺寸请求。
        /// 宿主在这里明确要求重算，因此必须绕过内容指纹去重：布局状态变更期间的早期请求可能因为宿主尚未就绪而被丢弃，
        /// 若被记入指纹，随后这次权威刷新会被去重丢弃，媒体栏就会停在预设画布长度上。
        /// Force-republishes the size request after the host updates orientation, scale, or font-size state. The host is
        /// explicitly asking for a recomputation here, so the content fingerprint must not suppress it: an earlier request
        /// raised while the host was not ready yet can be dropped after being recorded, and this authoritative refresh would
        /// then leave the bar at its preset canvas length.
        /// </summary>
        public void RefreshDesiredSize() => RaiseDesiredSizeChanged(isForcedRefresh: true);

        /// <summary>
        /// 应用横向任务栏的层级和交互设置，不改变原有封面、文字或布局引擎。
        /// Applies horizontal-taskbar layer and interaction settings without replacing the original artwork, text, or layout engine.
        /// </summary>
        public void ApplyTaskbarExperienceSettings()
        {
            var isHorizontalTaskbar = _currentMode == WindowMode.Taskbar && !_isVertical;
            var experience = SettingsManager.Current.TaskbarExperience.Normalize();
            var metrics = TaskbarDensityMetrics.From(experience.Density);
            // 频谱的柱数与样式决定它占用的宽度，因此必须在任何宽度计算之前先重建视觉树。
            // The spectrum's bar count and style decide how much width it occupies, so the visual tree is rebuilt before any
            // width is computed.
            ConfigureSpectrum();
            var progressVisible = _snapshot.Duration > 0;
            var controls = experience.HoverControls;
            var spectrumVisible = isHorizontalTaskbar && experience.SpectrumVisible && TaskbarExperiencePolicy.ShouldShowSpectrum(_snapshot);
            // 性能组件与频谱同样由"外层悬停表面 + 内层外观"组成：显隐、命中测试与 hover 都落在外层，
            // 否则悬停表面的 1 DIP 留白点不到，hover 也会在光标移到边缘时闪断。
            // The performance component is built like the spectrum, with an outer hover surface and an inner look: visibility, hit testing, and
            // hover all belong to the outer one, otherwise the surface's one-DIP padding is not clickable and hover flickers at the edges.
            var performanceVisible = isHorizontalTaskbar && experience.PerformanceVisible;
            TaskbarPerformanceSurface.Visibility = performanceVisible ? Visibility.Visible : Visibility.Collapsed;
            TaskbarPerformanceHoverSurface.Visibility = performanceVisible ? Visibility.Visible : Visibility.Collapsed;
            TaskbarPerformanceHoverSurface.IsHitTestVisible = performanceVisible;
            if (!performanceVisible)
                AnimateComponentHover(TaskbarPerformanceHoverSurface, false);

            TaskbarSpectrumHoverSurface.Visibility = spectrumVisible ? Visibility.Visible : Visibility.Collapsed;
            TaskbarSpectrumHoverSurface.IsHitTestVisible = spectrumVisible;
            if (!spectrumVisible)
            {
                AnimateComponentHover(TaskbarSpectrumHoverSurface, false);
                ApplySpectrum(ReadOnlySpan<float>.Empty);
            }
            // 静置层进度条只在媒体报告了时长、且开关打开时显示；开关是用户对"静置层要不要这条进度"的回答。
            // The rest-layer progress bar appears only while the session reports a duration and the switch is on; the switch is the
            // user's answer to "should the rest layer carry this progress line at all".
            TaskbarRestProgress.Visibility = isHorizontalTaskbar && progressVisible && experience.RestProgressVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
            TaskbarPreviousButton.Visibility = controls.PreviousNextVisible ? Visibility.Visible : Visibility.Collapsed;
            TaskbarNextButton.Visibility = controls.PreviousNextVisible ? Visibility.Visible : Visibility.Collapsed;
            TaskbarPlayPauseButton.Visibility = controls.PlayPauseVisible ? Visibility.Visible : Visibility.Collapsed;
            TaskbarTransportButtons.Visibility = controls.PreviousNextVisible || controls.PlayPauseVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
            TaskbarDeviceButton.Visibility = controls.OutputDeviceVisible ? Visibility.Visible : Visibility.Collapsed;
            TaskbarVolumeButton.Visibility = controls.AudioControlVisible ? Visibility.Visible : Visibility.Collapsed;
            TaskbarHoverProgress.Visibility = controls.ProgressVisible && progressVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
            // 完整层入口开关同时作用于悬停层的完整层按钮与静置层文字区顶部那条细杠：用户看到的只是"要不要这个入口"，
            // 因此两个入口必须一起消失，否则关掉开关后细杠没了、悬停按钮还留着。
            // The full-layer entry switch covers both the hover layer's button and the thin bar above the rest-layer text: what the
            // user asks for is "do I want this entry", so both have to disappear together, otherwise turning it off would remove
            // the thin bar while leaving the hover button behind.
            TaskbarFullPanelHandle.Visibility = experience.FullLayerEnabled && experience.FullPanelEntryVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
            var directFullPanelHandleVisible = CanShowDirectFullPanelHandle();
            TaskbarDirectFullPanelHandle.Visibility = directFullPanelHandleVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
            TaskbarDirectFullPanelHandle.IsHitTestVisible = directFullPanelHandleVisible;
            if (!directFullPanelHandleVisible)
                AnimateDirectFullPanelHandle(false, immediate: true);
            else
                AnimateDirectFullPanelHandle(
                    SongInfoStackPanel.IsMouseOver || TaskbarDirectFullPanelHandle.IsMouseOver,
                    immediate: true);

            foreach (var button in FindVisualChildren<System.Windows.Controls.Button>(TaskbarHoverActions))
            {
                if (ReferenceEquals(button, TaskbarFullPanelHandle))
                    continue;
                button.Width = metrics.ButtonSize;
                button.Height = metrics.ButtonSize;
            }
            TaskbarHoverProgress.Width = metrics.ProgressWidth;
            TaskbarHoverLayer.Height = metrics.HoverLayerHeight;
            ApplyTaskbarSectionGeometry(MainBorder.Width);

            SongMetadataPanel.Orientation = Orientation.Vertical;
            SongArtistContainer.Margin = new Thickness(0, -1.5, 0, 0);
            var metadataAlignment = experience.MediaTextAlignment switch
            {
                TaskbarMediaTextAlignment.Center => TextAlignment.Center,
                TaskbarMediaTextAlignment.Right => TextAlignment.Right,
                _ => TextAlignment.Left
            };
            ApplyConfiguredTextAlignment(SongTitle, metadataAlignment);
            ApplyConfiguredTextAlignment(SongArtist, metadataAlignment);
            ApplyWebLyricsStyle();
            if (isHorizontalTaskbar && SongMetadataPanel.Visibility == Visibility.Visible)
            {
                if (experience.ContentLayout == TaskbarContentLayout.CompactInline &&
                    !string.IsNullOrEmpty(_actualArtist))
                {
                    SongTitle.Text = string.IsNullOrEmpty(_actualTitle)
                        ? _actualArtist
                        : $"{_actualTitle} · {_actualArtist}";
                    SongArtistContainer.Visibility = Visibility.Collapsed;
                }
                else
                {
                    SongTitle.Text = _actualTitle;
                    SongArtistContainer.Visibility = !_isSmallTaskbar && !string.IsNullOrEmpty(_actualArtist)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }
            }
            else if (!isHorizontalTaskbar)
            {
                SongTitle.Text = _actualTitle;
            }

            if (!isHorizontalTaskbar || !experience.HoverLayerEnabled)
                HideTaskbarHoverLayer(immediate: true);
            if (!isHorizontalTaskbar)
            {
                StopMarqueeAnimations();
                AnimateComponentHover(SongImageHoverOverlay, false);
                AnimateComponentHover(SongInfoHoverOverlay, false);
                AnimateComponentHover(TaskbarSpectrumHoverSurface, false);
            }

            RaiseDesiredSizeChanged();

            // 跑马灯 MUST 放在本方法所有文字写入之后重跑：上面按内容布局写的标题会把正在滚动的窗口顶掉，而
            // ApplyTaskbarSectionGeometry（以及它内部的跑马灯配置）发生在那之前，于是屏幕上会先留下原文开头，
            // 直到下一帧推进才跳回窗口的位置——指针移入文字区（悬停进入也调用本方法）或每次快照轮询都会这样闪一下。
            // The marquee must be re-applied after every text write in this method: the title written above for the content layout
            // replaces the scrolling window, while ApplyTaskbarSectionGeometry — including the marquee configuration inside it — runs
            // before that. The head of the content would then stay on screen until the next advance frame snaps back to the window's
            // position, which is one visible jump each time the pointer enters the text area (hover entry calls this method too) and on
            // every snapshot poll.
            if (isHorizontalTaskbar)
                ApplyMarqueeLayout(Math.Max(0, SongInfoStackPanel.Width));
        }

        /// <summary>
        /// 将统一密度间隔应用到封面、文字和频谱，并让悬停控件只占据文字区域。
        /// Applies one density-controlled gap to artwork, text, and spectrum while constraining hover controls to the text region.
        /// </summary>
        private void ApplyTaskbarSectionGeometry(double primaryLength)
        {
            // 先按封面自身的比例纠正封面框：下面的文字起点与整条宽度都按封面右缘计算，封面宽度必须是最终值。
            // The artwork box is corrected to the artwork's own aspect first, because the text start and the whole length below are derived
            // from the artwork's right edge, which therefore has to be final.
            ApplyTaskbarArtworkAspect();
            if (_currentMode != WindowMode.Taskbar || _isVertical || !double.IsFinite(primaryLength))
            {
                // 几何拿不到有效长度时仍然按现有文字宽度重跑一次跑马灯：一次无效的长度不该让正在滚动的文字退回被裁状态，
                // 而宽度与裁剪方式只由这里和几何写入，跳过就等于把上一次的结果留在屏幕上。
                // Even without a usable length the marquee is re-applied from the text width currently in place: one invalid length
                // must not drop a scrolling text back to a cut-off state, because this method and the geometry are the only writers of
                // that width, and skipping it leaves the previous result on screen.
                if (_currentMode == WindowMode.Taskbar && !_isVertical)
                    ApplyMarqueeLayout(Math.Max(0, SongInfoStackPanel.Width));
                return;
            }

            var metrics = TaskbarDensityMetrics.From(SettingsManager.Current.TaskbarExperience.Density);
            var artworkRight = GetTaskbarArtworkRight();
            var experience = SettingsManager.Current.TaskbarExperience.Normalize();
            var spectrumVisible = experience.SpectrumVisible && TaskbarExperiencePolicy.ShouldShowSpectrum(_snapshot);
            var sectionGap = Math.Clamp(SettingsManager.Current.TaskbarExperience.ComponentSpacingDip,
                TaskbarExperienceSettings.MinimumComponentSpacingDip,
                TaskbarExperienceSettings.MaximumComponentSpacingDip);
            var textLeft = artworkRight + (_isConnected ? sectionGap : 0);
            var spectrumWidth = SpectrumSurfaceWidth;
            var reservedRight = (spectrumVisible ? sectionGap + spectrumWidth : 0) +
                                (experience.PerformanceVisible ? sectionGap + TaskbarPerformanceWidth : 0) +
                                TaskbarTrailingMargin;
            var textWidth = _isConnected
                ? Math.Max(0, primaryLength - textLeft - reservedRight)
                : 0;
            var textTop = Canvas.GetTop(SongInfoStackPanel);
            if (!double.IsFinite(textTop))
                textTop = 0;

            Canvas.SetLeft(SongInfoStackPanel, textLeft);
            SongInfoStackPanel.Width = textWidth;
            SongInfoSurface.Width = textWidth;
            SongTitleContainer.Width = textWidth;
            SongArtistContainer.Width = textWidth;
            SongLyricsPanel.Width = textWidth;

            // 任务栏的媒体文字宽度由本节几何唯一决定：布局引擎按布局 schema 写入的 TextBlock 宽度仍包含
            // 频谱与性能组件占用的区间，比真实文字区更宽，会让标题按错误宽度裁剪、在容器边缘被硬切；
            // 悬停路径会通过跑马灯配置重新写回正确宽度，所以这个错误只在设置变更后显现。
            // This section owns the taskbar media-text width: the layout engine writes TextBlock widths from the layout
            // schema, which still covers the spectrum and performance reserve and is wider than the real text area, so the
            // title trims against the wrong width and is hard-cut at the container edge. The hover path rewrites the correct
            // width through the marquee configuration, which is why the defect only shows up after a settings change.
            SongTitle.Width = textWidth;
            SongArtist.Width = textWidth;

            Canvas.SetLeft(SongInfoHoverOverlay, textLeft);
            Canvas.SetTop(SongInfoHoverOverlay, textTop);
            SongInfoHoverOverlay.Width = textWidth;
            SongInfoHoverOverlay.Height = SongInfoStackPanel.Height;

            // 性能组件与频谱的悬停表面取文字区悬停块的横轴尺寸，三个组件的 hover 因此是同一高度，
            // 而不是各自一个固定值（频谱原来是 32、性能块没有 hover）。频谱内容区比文字区还高时以内容为准，保证画布装得下。
            // The performance and spectrum hover surfaces take the text hover block's cross-axis size, so the three components share one
            // hover height instead of each carrying its own fixed value (the spectrum was 32 and the performance block had no hover at all).
            // A spectrum content area taller than the text block wins, so the canvas always fits.
            var hoverHeight = Math.Max(
                ResolveRestHoverHeight(),
                SpectrumPresentationPolicy.ResolveContentHeightDip(SettingsManager.Current.SpectrumComponent) +
                SpectrumPresentationPolicy.SurfacePaddingDip * 2);
            TaskbarSpectrumHoverSurface.Height = hoverHeight;
            TaskbarSpectrumHoverSurface.Width = spectrumWidth;
            TaskbarSpectrumHoverSurface.Margin = new Thickness(0, 0,
                TaskbarTrailingMargin + (experience.PerformanceVisible ? TaskbarPerformanceWidth + sectionGap : 0), 0);
            TaskbarPerformanceHoverSurface.Height = hoverHeight;
            TaskbarPerformanceHoverSurface.Width = TaskbarPerformanceWidth + SpectrumPresentationPolicy.SurfacePaddingDip * 2;
            TaskbarPerformanceHoverSurface.Margin = new Thickness(0, 0, TaskbarTrailingMargin, 0);

            TaskbarRestProgress.Margin = new Thickness(textLeft, 0, reservedRight, 1);

            HoverRevealHost.Margin = new Thickness(textLeft, 1, 0, 1);
            HoverRevealHost.Height = Math.Max(0, MainBorder.Height - 2);
            TaskbarHoverLayer.Width = textWidth;
            TaskbarDirectFullPanelHandle.Width = textWidth;
            TaskbarDirectFullPanelHandle.Margin = new Thickness(textLeft, 1, 0, 0);
            if (HoverRevealHost.Visibility == Visibility.Visible)
            {
                HoverRevealClip.BeginAnimation(RectangleGeometry.RectProperty, null);
                HoverRevealHost.Width = textWidth;
                HoverRevealClip.Rect = new Rect(0, 0, textWidth, HoverRevealHost.Height);
            }

            ApplyMarqueeLayout(textWidth);
        }

        private double GetTaskbarArtworkRight()
        {
            var artworkLeft = Canvas.GetLeft(SongImageBorder);
            if (!double.IsFinite(artworkLeft))
                artworkLeft = 0;
            return artworkLeft + Math.Max(0, SongImageBorder.Width);
        }

        /// <summary>
        /// 静置层各组件的 hover 块在横轴（横向任务栏就是高度）上的尺寸：与文字区的悬停块一致，
        /// 取值失败时退回到整条媒体栏的内高，避免 hover 块塌成一条线。
        /// Cross-axis extent of the rest layer's hover blocks, which for a horizontal taskbar is the height: it matches the text region's
        /// hover block and falls back to the bar's inner height when the value is unusable, so a hover block never collapses to a line.
        /// </summary>
        private double ResolveRestHoverHeight()
        {
            var height = SongInfoStackPanel.Height;
            if (double.IsFinite(height) && height > 0)
                return height;

            var barHeight = MainBorder.Height;
            return double.IsFinite(barHeight) && barHeight > 2 ? barHeight - 2 : 0;
        }

        /// <summary>
        /// 按封面自身的宽高比调整封面框：高度取布局引擎给的尺寸，宽度按比例算，因此视频类宽封面不再被裁掉左右两边、
        /// 竖版封面也不再被裁掉上下两边。比例超出允许范围时改用 <see cref="Stretch.Uniform"/>（留白也不裁切）。
        /// 只作用于任务栏横向模式：其余模式（竖向任务栏、灵动岛）的封面尺寸仍由布局引擎唯一决定。
        /// Adjusts the artwork box to the artwork's own aspect: the height comes from the layout engine and the width follows the ratio, so a
        /// wide video cover is no longer cropped left and right and a portrait cover is no longer cropped top and bottom. Beyond the allowed
        /// range it switches to <see cref="Stretch.Uniform"/>, which letterboxes instead of cropping. This only applies to the horizontal
        /// taskbar: in the other modes (vertical taskbar, dynamic island) the artwork size stays the layout engine's decision alone.
        /// </summary>
        private void ApplyTaskbarArtworkAspect()
        {
            if (_currentMode != WindowMode.Taskbar || _isVertical)
                return;

            var height = SongImageBorder.Height;
            if (!double.IsFinite(height) || height <= 0)
                return;

            var artwork = SongImage.ImageSource as BitmapSource;
            var box = ArtworkBoxPolicy.Resolve(height, artwork?.PixelWidth ?? 0, artwork?.PixelHeight ?? 0);
            if (Math.Abs(SongImageBorder.Width - box.Width) > 0.01)
                SongImageBorder.Width = box.Width;

            // 比例没被夹取时封面框与封面同比例，UniformToFill 正好铺满且不裁切；被夹取时只能留白。
            // While the aspect is not clamped the box matches the artwork exactly, so UniformToFill fills it without cropping; once clamped,
            // letterboxing is the only way to avoid cropping.
            var stretch = box.Letterbox ? Stretch.Uniform : Stretch.UniformToFill;
            if (SongImage.Stretch != stretch)
                SongImage.Stretch = stretch;
        }

        /// <summary>当前是否有已连接且正在播放的媒体。/ Indicates whether connected media is currently playing.</summary>
        public bool IsPlaying => _isConnected && !_isPaused;

        /// <summary>
        /// 按宿主传达的后台剪枝档位停止或恢复本控件自己的计时器。
        /// Stops or restores this control's own timers for the background prune level the host publishes.
        ///
        /// 控件不认识电源状态，也不去查询它：档位由宿主（任务栏窗口）传进来，和外观、布局设置的传达方式一致。屏幕熄灭时进度条、跑马灯与
        /// Web 歌词推进也没有人看得到，停掉它们省下的是每分钟几百次唤醒；恢复时从下一帧继续推进，用户看不到空档。
        /// The control neither knows the power state nor asks for it: the host — the taskbar window — passes the level in, exactly as it passes the
        /// appearance and layout settings. While the display is dark nothing can see the progress bar, marquee, or web lyrics, so
        /// stopping them saves several hundred wakeups a minute; on restore they advance again from the next frame, which the
        /// user never notices.
        /// </summary>
        /// <param name="level">宿主当前应用的剪枝档位。/ The prune level the host currently applies.</param>
        public void ApplyBackgroundPruneLevel(MemoryPruneLevel level)
        {
            _backgroundPruneLevel = level;
            if (level == MemoryPruneLevel.None)
            {
                ResumeBackgroundTimers();
                return;
            }

            // 空闲档停进度条与两支推进计时器：空闲的前提是既没有在播的媒体、用户也已经离开十分钟以上，此时进度条不动，
            // 暂停曲目的标题也只会为一个不在座位上的人滚动。悬停层与滚轮提示的计时器不在这里停——它们只在指针停在栏上时运行，
            // 那意味着用户刚刚动过鼠标，档位本来就会立刻回到常规。
            // The idle level stops the progress timer and both advance timers: idle requires no playing media and a user who has been away for over ten
            // minutes, so the progress bar does not move and a paused track's title would only scroll for somebody who is not there. The hover and
            // wheel-tooltip timers are left alone: they run only while the pointer rests on the bar, which means the mouse has just moved and the level
            // is about to return to normal anyway.
            _progressTimer.Stop();
            if (level < MemoryPruneLevel.DisplayOff)
            {
                RefreshWebLyricsTimer();
                ApplyMarqueeLayout(Math.Max(0, SongInfoStackPanel.Width));
                return;
            }

            _hoverOpenTimer.Stop();
            _hoverCloseTimer.Stop();
            _wheelTooltipTimer.Stop();

            // 推进类计时器交给各自的判定路径收尾：跑马灯必须恢复原文，Web 歌词必须保留最新目标帧供恢复时继续。
            // The advance timers wind down through their own decision paths: the marquee restores its source text, while web lyrics retain
            // the latest target frame for the next resume.
            RefreshWebLyricsTimer();
            ApplyMarqueeLayout(Math.Max(0, SongInfoStackPanel.Width));
        }

        /// <summary>
        /// 恢复控件自己的计时器并让推进类逻辑重新判定一次；只重启进度条，其余按需自启。
        /// Restores the control's own timers and lets the advance logic decide again. Only the progress bar is restarted, because the others start on
        /// demand from presentation or interaction state.
        /// </summary>
        private void ResumeBackgroundTimers()
        {
            if (IsBackgroundPruned || !IsLoaded)
            {
                return;
            }

            if (!_progressTimer.IsEnabled)
            {
                _progressTimer.Start();
            }

            ApplyMarqueeLayout(Math.Max(0, SongInfoStackPanel.Width));
            RefreshWebLyricsTimer();
        }

        /// <summary>是否因宿主传达的后台剪枝档位而暂停推进类计时器。/ Whether the advance timers are paused by the prune level the host published.</summary>
        private bool IsBackgroundPruned => _backgroundPruneLevel >= MemoryPruneLevel.DisplayOff;

        /// <summary>
        /// 是否连推进类计时器（跑马灯、Web 歌词）也一并停下：空闲档就停，显示器关闭与睡眠当然也停。
        /// Whether advance timers — marquee and web lyrics — stop as well: they stop at the idle level too, and of course while
        /// the display is dark or the system is suspending.
        ///
        /// 空闲档之所以也停，是因为它的前提就是"没有在播的媒体 + 用户已离开十分钟以上"：此时跑马灯只会为一个不在座位上的人每 16 毫秒推进一次，
        /// 而恢复的代价最多是一次评估周期（用户一有输入，档位就回到常规）。
        /// The idle level stops them because its premise is exactly "no playing media and a user gone for over ten minutes": the marquee would then advance
        /// once every 16 ms for somebody who is not at their desk, while the restore costs at most one evaluation period, since any input returns the level
        /// to normal.
        /// </summary>
        private bool IsAdvancePruned => _backgroundPruneLevel != MemoryPruneLevel.None;

        /// <summary>
        /// 设置竖向模式：任务栏在屏幕左侧或右侧时调整布局。
        /// Set vertical mode: adjust layout when taskbar is on screen left or right edge.
        /// </summary>
        public void SetVerticalMode(bool isVertical)
        {
            // 使用新的布局系统
            // Use new layout system
            var orientation = isVertical ? LayoutOrientation.Vertical : LayoutOrientation.Horizontal;
            ApplyLayout(_currentMode, orientation);

            // 兼容性：更新可见性（布局系统已处理尺寸）
            // Compatibility: update visibility (layout system handles sizing)
            SongInfoStackPanel.Visibility = !isVertical && _isConnected
                ? Visibility.Visible
                : Visibility.Collapsed;
            SongInfoStackPanel.IsHitTestVisible = !isVertical && _isConnected;
            SongArtistContainer.Visibility = !_isSmallTaskbar && !isVertical && !string.IsNullOrEmpty(_actualArtist)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        /// <summary>
        /// 设置小任务栏模式：任务栏高度较小时隐藏艺术家信息。
        /// Set small taskbar mode: hide artist info when taskbar height is small.
        /// </summary>
        public void SetSmallTaskbarMode(bool isSmallTaskbar)
        {
            _isSmallTaskbar = isSmallTaskbar;
            SongArtistContainer.Visibility = !isSmallTaskbar && !_isVertical && !string.IsNullOrEmpty(_actualArtist)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        /// <summary>
        /// 应用播放器文字和灵动岛背景设置。
        /// Applies player text and dynamic-island background settings.
        /// </summary>
        public void ApplyAppearanceSettings()
        {
            var appearance = SettingsManager.Current.Appearance.Normalize();
            var appTheme = ApplicationThemeManager.GetAppTheme();
            var isDark = appTheme == ApplicationTheme.Dark;
            if (appTheme == ApplicationTheme.Unknown)
            {
                WindowsThemeDetector.GetWindowsTheme(out var windowsAppTheme, out _);
                isDark = windowsAppTheme == WindowsThemeDetector.ThemeMode.Dark;
            }
            var presentation = PlayerForegroundPolicy.ResolvePresentation(
                appearance.PlayerForegroundMode,
                SystemParameters.HighContrast,
                isDark,
                _adaptiveForegroundDecision);

            Brush foreground;
            if (presentation.UsesSystemColors)
            {
                foreground = SystemColors.WindowTextBrush;
            }
            else
            {
                foreground = new SolidColorBrush(presentation.UsesLightText
                    ? Colors.White
                    : Color.FromRgb(0x1C, 0x1C, 0x1C));
            }

            SongTitle.Foreground = foreground;
            SongArtist.Foreground = foreground;
            TaskbarPerformanceText.Foreground = foreground;
            // 频谱与文字取同一支自动前景：两者铺在同一块任务栏表面上，分开判断只会在同一背景上给出两种颜色。
            // The spectrum takes the same automatic foreground as the text: both sit on the same taskbar surface, and judging
            // them separately would only produce two colours over one background.
            ApplySpectrumForeground(foreground);
            SongInfoStackPanel.Background = Brushes.Transparent;
            ApplyContrastShadow(presentation.NeedsContrastShadow, presentation.UsesLightText);
            SetWebLyricsAppearance(foreground, presentation.NeedsContrastShadow, presentation.UsesLightText);

            if (_currentMode == WindowMode.Taskbar)
            {
                MainBorder.Background = new SolidColorBrush(Colors.Transparent);
                TopBorder.BorderBrush = Brushes.Transparent;
                BackgroundImage.Visibility = Visibility.Collapsed;
                return;
            }

            if (_currentMode != WindowMode.DynamicIsland)
                return;

            MainBorder.Background = SettingsManager.Current.DynamicIslandBackgroundMode == DynamicIslandBackgroundMode.Transparent
                ? new SolidColorBrush(Color.FromArgb(1, 0, 0, 0))
                : SystemParameters.HighContrast
                    ? SystemColors.WindowBrush
                    : new SolidColorBrush(isDark
                        ? Color.FromArgb(0xFF, 0x20, 0x20, 0x20)
                        : Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF3));
        }

        private void ApplyContrastShadow(bool enabled, bool usesLightText)
        {
            Effect? effect = null;
            if (enabled)
            {
                // 阴影只负责对比度，不参与字形：模糊半径保持在 1 DIP，并且必须离开字形轮廓。
                // ShadowDepth 为 0 时模糊副本压在字形正中，会把每条笔画的边缘吃掉，用户看到的就是"文字发虚"；
                // 偏移 1 DIP 后阴影落在轮廓外侧，字形边缘保持干净。
                // The shadow exists for contrast, not for glyph shape: keep the blur radius at 1 DIP but move it off the
                // glyph outline. With ShadowDepth 0 the blurred copy sits centered under the glyphs and eats every stroke
                // edge, which reads as blurry text; a 1 DIP offset keeps the glyph edges clean.
                var shadow = new DropShadowEffect
                {
                    Color = usesLightText ? Colors.Black : Colors.White,
                    BlurRadius = 1,
                    ShadowDepth = 1,
                    Direction = 315,
                    Opacity = 0.85,
                    RenderingBias = RenderingBias.Quality
                };
                shadow.Freeze();
                effect = shadow;
            }

            SongTitle.Effect = effect;
            SongArtist.Effect = effect;
            TaskbarPerformanceText.Effect = effect;
        }


        /// <summary>
        /// 更新歌曲信息：根据快照更新 UI 的所有元素（标题、艺术家、封面、歌词、播放状态）。
        /// Update song info: updates all UI elements based on snapshot (title, artist, artwork, lyrics, playback state).
        ///
        /// 算法 Algorithm:
        /// 1. 断开状态：显示占位符图标，清空所有信息
        ///    Disconnected: show placeholder icon, clear all info
        /// 2. 连接状态：更新标题、艺术家、封面、歌词
        ///    Connected: update title, artist, artwork, lyrics
        /// 3. 封面存在时根据播放/暂停状态显示不同图标
        ///    Show different icon based on play/pause state when artwork exists
        /// 4. 歌词可用时用当前行替换标题显示
        ///    Replace title with current lyric line when lyrics are available
        /// </summary>
        public void UpdateSongInfo(MediaSnapshot snapshot)
        {
            _snapshot = snapshot;
            if (!snapshot.IsConnected)
            {
                // 无媒体播放 - 显示占位符保持媒体栏可见
                // No media playing - show the placeholder text so the media bar stays visible
                Dispatcher.Invoke(() =>
                {
                    _actualTitle = string.Empty;
                    _actualArtist = string.Empty;
                    _isConnected = false;
                    _canPlayPause = false;
                    _canSkipPrevious = false;
                    _canSkipNext = false;

                    SongTitle.Text = _actualTitle;
                    SongMetadataPanel.Visibility = Visibility.Visible;
                    SongLyricsPanel.Visibility = Visibility.Collapsed;
                    UpdateWebLyricsPresentation(allowTransition: false);
                    SongArtist.Text = _actualArtist;
                    SongInfoStackPanel.Visibility = Visibility.Collapsed;
                    SongInfoStackPanel.IsHitTestVisible = false;
                    UpdateSongInfoTooltip(null, null);
                    SongImagePlaceholder.Symbol = SymbolRegular.MusicNote220;
                    SongImagePlaceholder.Visibility = Visibility.Visible;
                    SongImage.ImageSource = null;
                    BackgroundImage.Source = null;
                    BackgroundImage.Visibility = Visibility.Collapsed;
                    SongImageBorder.Margin = new Thickness(0, 0, 0, -3); // align music note better when no cover
                    TaskbarPreviousButton.IsEnabled = false;
                    TaskbarPlayPauseButton.IsEnabled = false;
                    TaskbarNextButton.IsEnabled = false;
                    HideTaskbarHoverLayer(immediate: true);
                    UpdateTaskbarProgress();
                    ApplyTaskbarExperienceSettings();

                    // 任务栏无媒体时保持完全透明；灵动岛保留布局定义的稳定背景。
                    // Keep the disconnected taskbar transparent; preserve the dynamic-island layout background.
                    if (_currentMode == WindowMode.Taskbar)
                    {
                        MainBorder.Background = new SolidColorBrush(Colors.Transparent);
                        MainBorder.Background.Opacity = 0;
                        TopBorder.BorderBrush = Brushes.Transparent;
                    }

                    Visibility = Visibility.Visible;
                    RaiseDesiredSizeChanged(isForcedRefresh: true);
                });
                return;
            }

            _isPaused = !snapshot.IsPlaying;
            _isConnected = true;
            _canPlayPause = snapshot.CanPlayPause;
            _canSkipPrevious = snapshot.CanSkipPrevious;
            _canSkipNext = snapshot.CanSkipNext;

            Dispatcher.Invoke(() =>
            {
                string newTitle = !string.IsNullOrEmpty(snapshot.Title) ? snapshot.Title : "-";
                string newArtist = !string.IsNullOrWhiteSpace(snapshot.Artist)
                    ? snapshot.Artist
                    : !string.IsNullOrWhiteSpace(snapshot.SourceName) ? snapshot.SourceName : "-";

                // 标题或艺术家变化时触发入场动画
                // Trigger entrance animation when title or artist changes
                if (_actualTitle != newTitle || _actualArtist != newArtist)
                {
                    AnimateEntrance();

                    _actualTitle = newTitle;
                    _actualArtist = newArtist;

                    SongTitle.Text = _actualTitle;
                    SongArtist.Text = _actualArtist;
                }

                SongTitle.Text = _actualTitle;
                UpdateWebLyricsPresentation();

                // 完整歌曲信息并入整条媒体栏共用的那个提示：媒体文字区不再自持一个提示，
                // 否则悬停文字时会盖住滚轮提示，而两者恰恰是同一处需要的两条信息。
                // The full song info joins the single tooltip shared by the whole bar: the text area no longer carries one of its own,
                // because that would cover the wheel hint while both are exactly the two things needed in that one place.
                UpdateSongInfoTooltip(snapshot.Title, snapshot.Artist);

                // 根据主色调改变图标颜色（从封面提取）；没有封面主色时回退到应用统一强调色，再退回系统高亮色。
                // 旧实现回退到 MicaWPF 的强调色键，而该键在本项目中不解析，会得到一个空画刷。
                // Change icon color based on dominant color (extracted from artwork); without an artwork dominant color it
                // falls back to the application accent and then to the system highlight color. The previous fallback used a
                // MicaWPF accent key that does not resolve in this project and produced a null brush.
                SolidColorBrush brush = BitmapHelper.SavedDominantColors.Count > 0
                    ? BitmapHelper.SavedDominantColors.Last()
                    : Application.Current.TryFindResource("AfAccentBrush") as SolidColorBrush
                      ?? new SolidColorBrush(SystemColors.HighlightColor);
                SongImagePlaceholder.Foreground = brush;

                if (snapshot.Artwork is not null)
                {
                    if (_isPaused)
                    {
                        // show pause icon overlay
                        SongImagePlaceholder.Symbol = SymbolRegular.Pause24;
                        SongImagePlaceholder.Visibility = Visibility.Visible;
                        SongImage.Opacity = 0.4;
                    }
                    else
                    {
                        SongImagePlaceholder.Visibility = Visibility.Collapsed;
                        SongImage.Opacity = 1;
                    }

                    SongImage.ImageSource = snapshot.Artwork;
                    BackgroundImage.Source = snapshot.Artwork;
                    SongImageBorder.Margin = new Thickness(0, 0, 0, -2); // align image better when cover is present
                }
                else
                {
                    SongImagePlaceholder.Symbol = SymbolRegular.MusicNote220;
                    SongImagePlaceholder.Visibility = Visibility.Visible;
                    SongImage.ImageSource = null;
                    BackgroundImage.Source = null;
                }

                // 封面换了就重新按它的比例定封面框：非正方形封面（视频封面）因此不再被裁切。
                // A new artwork re-derives the box from its aspect, so a non-square cover such as a video's is no longer cropped.
                ApplyTaskbarArtworkAspect();

                // 文字内容一确定就重跑一次跑马灯，而且必须在**所有**文字写入之后：更长的标题该不该滚动只由这一段文字与当前可用宽度决定，
                // 而歌词呈现会把整行原文写进歌词两行、顶掉跑马灯的窗口——那时不同步恢复，屏幕上的歌词就会跳回行首。
                // The marquee is re-applied once the text is settled, and it has to run after **every** text write: whether a longer title has
                // to scroll depends only on this text and the width currently available, while the lyric presentation writes the whole line
                // into both lyric rows and replaces the marquee window, so without an immediate restore the lyrics snap back to the line's head.
                ApplyMarqueeLayout(Math.Max(0, SongInfoStackPanel.Width));
                SongArtistContainer.Visibility = !_isSmallTaskbar && !_isVertical && !string.IsNullOrEmpty(_actualArtist)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                SongInfoStackPanel.Visibility = _isVertical ? Visibility.Collapsed : Visibility.Visible;
                SongInfoStackPanel.IsHitTestVisible = !_isVertical;
                // 任务栏主体保持透明；灵动岛继续沿用布局引擎已有背景行为。
                // Keep the taskbar body transparent; the island retains its existing layout-engine background behavior.
                BackgroundImage.Visibility = Visibility.Collapsed;

                TaskbarPreviousButton.IsEnabled = _canSkipPrevious;
                TaskbarPlayPauseButton.IsEnabled = _canPlayPause;
                TaskbarNextButton.IsEnabled = _canSkipNext;
                TaskbarPlayPauseIcon.Symbol = _isPaused ? SymbolRegular.Play24 : SymbolRegular.Pause24;
                UpdateTaskbarProgress();
                ApplyTaskbarExperienceSettings();

                Visibility = Visibility.Visible;
                RaiseDesiredSizeChanged();
            });
        }

        /// <summary>根据当前可见文本发布自动尺寸请求。/ Raises an auto-size request for the visible text.</summary>
        /// <param name="isForcedRefresh">是否绕过内容指纹去重。/ Whether the content-fingerprint dedupe is bypassed.</param>
        private void RaiseDesiredSizeChanged(bool isForcedRefresh = false)
        {
            if (_layoutEngine?.CurrentOrientation is not { } orientation)
                return;

            var lyricsVisible = SongLyricsPanel.Visibility == Visibility.Visible;
            var visibleText = lyricsVisible ? _lyricsFrame.Current : SongTitle.Text;
            var secondaryText = lyricsVisible
                ? string.IsNullOrEmpty(_lyricsFrame.CurrentTranslation) ? _lyricsFrame.Next : _lyricsFrame.CurrentTranslation
                : string.Empty;
            var artist = !lyricsVisible && SongArtistContainer.Visibility == Visibility.Visible ? _actualArtist : string.Empty;
            // Spectrum tuning and metric selection never change their reserved widths. Keeping
            // those values (or play/pause) in the fingerprint causes redundant host size
            // animations and visibly nudges title/artist/lyrics while sliders are adjusted.
            // 频谱柱数是唯一的例外：它决定频谱组件宽度，因此必须参与指纹，否则改柱数后宿主不会重新发布尺寸。
            // The spectrum bar count is the one exception: it decides the spectrum width, so it belongs in the
            // fingerprint; leaving it out would stop the host from republishing the size after a bar-count change.
            var fingerprint = $"{orientation}|{visibleText}|{secondaryText}|{artist}|{SongTitle.FontSize:0.##}|{SongArtist.FontSize:0.##}|{SettingsManager.Current.LayoutLengthScalePercent:0.##}|{SettingsManager.Current.LayoutThicknessScalePercent:0.##}|{SettingsManager.Current.LyricsEnabled}|{SettingsManager.Current.TwoLineLyricsEnabled}|{SettingsManager.Current.LyricsSecondaryLine}|{string.Join(',', LyricsSecondaryLinePolicy.ResolveOrder(SettingsManager.Current.LyricsSecondaryLine))}|{SettingsManager.Current.TaskbarExperience}|{SpectrumSurfaceWidth:0.##}|{SettingsManager.Current.SpectrumComponent.ContentHeightDip:0.##}|{_snapshot.IsConnected}|{_snapshot.Duration > 0}";

            // 没有订阅者的请求不会被任何宿主消费，因此不能记入指纹；否则订阅后的首次请求会被去重丢弃，
            // 媒体栏在上一次媒体连接之前一直停留在预设长度。
            // A request raised without a subscriber is never consumed, so it must not be recorded: otherwise the first
            // request after the host subscribes is dropped by the dedupe and the bar keeps its preset length until the next
            // media connection.
            if (DesiredSizeChanged is null)
                return;

            if (!isForcedRefresh && fingerprint == _lastSizeFingerprint)
                return;

            _lastSizeFingerprint = fingerprint;
            var textWidth = Math.Max(
                Math.Max(
                    lyricsVisible ? MeasureWebLyricWidth(visibleText) : MeasureTextWidth(visibleText, SongTitle),
                    lyricsVisible ? MeasureWebLyricWidth(secondaryText) : 0),
                MeasureTextWidth(artist, SongArtist));
            var preset = LayoutPresets.GetLayout(_currentMode, orientation);
            var request = LayoutSizeCalculator.Calculate(
                preset,
                SettingsManager.Current.LayoutLengthScalePercent / 100.0,
                SettingsManager.Current.LayoutThicknessScalePercent / 100.0,
                textWidth,
                double.PositiveInfinity,
                fingerprint,
                isForcedRefresh);
            if (_currentMode == WindowMode.Taskbar && orientation == LayoutOrientation.Horizontal)
            {
                var experience = SettingsManager.Current.TaskbarExperience.Normalize();
                var spectrumVisible = experience.SpectrumVisible && TaskbarExperiencePolicy.ShouldShowSpectrum(_snapshot);
                var spectrumWidth = SpectrumSurfaceWidth;
                var progressVisible = _snapshot.Duration > 0;
                var contentWidth = TaskbarExperiencePolicy.CalculateWidth(
                    textWidth,
                    GetTaskbarArtworkRight(),
                    spectrumWidth,
                    TaskbarTrailingMargin,
                    _snapshot.IsConnected,
                    spectrumVisible,
                    experience.HoverControls.PlayPauseVisible || experience.HoverControls.PreviousNextVisible,
                    experience.HoverLayerEnabled,
                    progressVisible,
                    experience.Density,
                    double.PositiveInfinity,
                    experience.ComponentSpacingDip,
                    performanceVisible: experience.PerformanceVisible,
                    performanceWidth: TaskbarPerformanceWidth,
                    hoverControls: experience.HoverControls);
                _minimumPrimaryLength = TaskbarExperiencePolicy.CalculateWidth(
                    0,
                    GetTaskbarArtworkRight(),
                    spectrumWidth,
                    TaskbarTrailingMargin,
                    _snapshot.IsConnected,
                    spectrumVisible,
                    experience.HoverControls.PlayPauseVisible || experience.HoverControls.PreviousNextVisible,
                    experience.HoverLayerEnabled,
                    progressVisible,
                    experience.Density,
                    double.PositiveInfinity,
                    experience.ComponentSpacingDip,
                    performanceVisible: experience.PerformanceVisible,
                    performanceWidth: TaskbarPerformanceWidth,
                    hoverControls: experience.HoverControls);
                request = request with
                {
                    Width = _snapshot.IsConnected
                        ? TaskbarExperiencePolicy.ResolvePrimaryLength(
                            contentWidth,
                            _minimumPrimaryLength,
                            double.PositiveInfinity,
                            experience.LengthMode,
                            experience.FixedLengthDip)
                        : contentWidth
                };
            }
            DesiredSizeChanged?.Invoke(this, new MediaBarSizeRequestEventArgs(request));
        }

        private void SongImageBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || ShouldSuppressSurfaceClick())
                return;

            if (!_isConnected)
            {
                if (_currentMode == WindowMode.Taskbar && !_isVertical)
                {
                    QuickLaunchMenuRequested?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                }
                return;
            }

            var action = _currentMode == WindowMode.Taskbar
                ? PlayerClickBindingPolicy.Resolve(SettingsManager.Current.Interaction, artwork: true)
                : PlayerClickAction.TogglePlayPause;
            ExecutePlayerClickAction(action);
            e.Handled = true;
        }

        /// <summary>
        /// 执行静置层的点击绑定。三个动作各自只发一个请求，因此封面与文字可以绑到不同结果而不会互相触发。
        /// Runs a rest-layer click binding. Each action raises exactly one request, so artwork and text can be bound to different
        /// results without triggering each other.
        /// </summary>
        private void ExecutePlayerClickAction(PlayerClickAction action)
        {
            switch (action)
            {
                case PlayerClickAction.TogglePlayPause:
                    if (!_canPlayPause)
                        return;
                    TogglePlayPauseRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case PlayerClickAction.OpenFullPanel:
                    RequestOpenFullPanel();
                    break;
                case PlayerClickAction.Disabled:
                    // 不绑定：点击该区域什么都不做（命中仍然被吃掉，因为这一块本来就是媒体动作区）。
                    // Not bound: clicking that region does nothing, while the hit stays consumed because the region is a media action area.
                    break;
                default:
                    ActivateSourceRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }

        /// <summary>请求宿主打开完整层；细杠、快捷键入口与点击绑定都走这一条路径。 / Requests the host to open the full layer; the thin bar, the click bindings, and any other entry all go through this one path.</summary>
        public void RequestOpenFullPanel() => OpenFullPanelRequested?.Invoke(this, EventArgs.Empty);

        private void InteractionSurface_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Preview events tunnel through this parent before the button handlers.
            // Leave both audio buttons in control so their wheel input never becomes a media gesture.
            if (TaskbarDeviceButton.IsMouseOver || TaskbarVolumeButton.IsMouseOver)
                return;

            if (!_isConnected)
            {
                if (_currentMode == WindowMode.Taskbar && !_isVertical && SongImageBorder.IsMouseOver && _quickLaunchEntries.Count > 0)
                {
                    QuickLaunchWheelRequested?.Invoke(this, new PlayerSurfaceWheelEventArgs(e.Delta, false, false, false));
                    e.Handled = true;
                }
                return;
            }

            if (_currentMode == WindowMode.Taskbar)
            {
                var leftDown = Mouse.LeftButton == MouseButtonState.Pressed;
                var rightDown = Mouse.RightButton == MouseButtonState.Pressed;
                var shiftDown = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                var modifier = SettingsManager.Current.Interaction.Normalize().Modifier;
                if ((modifier == InteractionModifier.LeftMouseButton && leftDown) ||
                    (modifier == InteractionModifier.RightMouseButton && rightDown))
                    _suppressSurfaceClickUntilUtc = DateTime.UtcNow.AddMilliseconds(350);
                WheelRequested?.Invoke(this, new PlayerSurfaceWheelEventArgs(e.Delta, shiftDown, leftDown, rightDown));
                e.Handled = true;
            }
            else if (e.Delta > 0 && _canSkipPrevious)
            {
                SkipPreviousRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
            else if (e.Delta < 0 && _canSkipNext)
            {
                SkipNextRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
        }

        private void TaskbarPreviousButton_Click(object sender, RoutedEventArgs e) =>
            SkipPreviousRequested?.Invoke(this, EventArgs.Empty);

        private void TaskbarPlayPauseButton_Click(object sender, RoutedEventArgs e) =>
            TogglePlayPauseRequested?.Invoke(this, EventArgs.Empty);

        private void TaskbarNextButton_Click(object sender, RoutedEventArgs e) =>
            SkipNextRequested?.Invoke(this, EventArgs.Empty);

        private void TaskbarDeviceButton_Click(object sender, RoutedEventArgs e) =>
            OutputDeviceMenuRequested?.Invoke(this, EventArgs.Empty);

        private void TaskbarVolumeButton_Click(object sender, RoutedEventArgs e) =>
            VolumeMenuRequested?.Invoke(this, EventArgs.Empty);

        private void TaskbarDeviceButton_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            OutputDeviceWheelRequested?.Invoke(this, new PlayerSurfaceWheelEventArgs(e.Delta, false, false, false));
            e.Handled = true;
        }

        private void TaskbarDeviceButton_MouseEnter(object sender, MouseEventArgs e) =>
            OutputDeviceInfoRequested?.Invoke(this, EventArgs.Empty);

        private void TaskbarVolumeButton_MouseEnter(object sender, MouseEventArgs e) =>
            VolumeInfoRequested?.Invoke(this, EventArgs.Empty);

        private void TaskbarVolumeButton_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            VolumeWheelRequested?.Invoke(this, new PlayerSurfaceWheelEventArgs(e.Delta, false, false, false));
            e.Handled = true;
        }

        private void TaskbarFullPanelHandle_Click(object sender, RoutedEventArgs e) =>
            OpenFullPanelRequested?.Invoke(this, EventArgs.Empty);

        private void UpdateTaskbarProgress()
        {
            var position = TaskbarExperiencePolicy.GetPosition(_snapshot, DateTimeOffset.UtcNow);
            var hasDuration = _snapshot.Duration > 0;
            TaskbarRestProgress.Maximum = Math.Max(1, _snapshot.Duration);
            TaskbarRestProgress.Value = position;
            TaskbarHoverProgress.Maximum = Math.Max(1, _snapshot.Duration);
            TaskbarHoverProgress.Value = position;
            if (_currentMode == WindowMode.Taskbar && !_isVertical)
            {
                var experience = SettingsManager.Current.TaskbarExperience.Normalize();
                TaskbarRestProgress.Visibility = hasDuration && experience.RestProgressVisible
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                TaskbarHoverProgress.Visibility = hasDuration && experience.HoverControls.ProgressVisible
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                if (child is T typed)
                    yield return typed;
                foreach (var descendant in FindVisualChildren<T>(child))
                    yield return descendant;
            }
        }

        private void SongTitleContainer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isConnected || e.ChangedButton != MouseButton.Left || ShouldSuppressSurfaceClick())
                return;

            var action = _currentMode == WindowMode.Taskbar
                ? PlayerClickBindingPolicy.Resolve(SettingsManager.Current.Interaction, artwork: false)
                : PlayerClickAction.ActivateSource;
            ExecutePlayerClickAction(action);
            e.Handled = true;
        }

        /// <summary>
        /// 本次点击是否应当被吞掉：组合滚轮结束后的合成点击，或宿主设定的一次性抑制窗口。
        /// 查询一律执行，因为抑制标记是一次性的——不取走它，之后真正的点击会被误吞。
        /// Whether this click has to be swallowed: the synthesized one after a chord wheel, or a one-shot window set by the host.
        /// The query always runs, because the suppression flag is one-shot: leaving it in place would eat a later, real click.
        /// </summary>
        private bool ShouldSuppressSurfaceClick()
        {
            var chordWheelClick = SuppressedClickSource?.Invoke() ?? false;
            return chordWheelClick || DateTime.UtcNow < _suppressSurfaceClickUntilUtc;
        }
    }
}
