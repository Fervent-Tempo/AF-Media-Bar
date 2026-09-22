using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Views.Windows;

/// <summary>
/// 胶囊岛窗口的输入半部分：媒体动作区的点击绑定（单击/双击/中键）、岛内滚轮手势、组合滚轮合成点击的抑制，以及滚轮结果提示。
/// Input half of the capsule-island window: click bindings on the media-action areas (single click, double click, middle button),
/// the in-island wheel gesture, suppression of the click a chord wheel synthesizes, and the wheel-result tooltip.
///
/// 三条不变量：点击与滚轮都**不抢焦点**（岛是 <c>WS_EX_NOACTIVATE</c>，全程没有 <c>Activate()</c>）；媒体动作区的点击把命中吃掉
/// 但**不**切换固定态（固定态下指针离开不再收起）；合成点击的抑制标记是**一次性**的，整个岛只有窗口级左键抬起这一处消费它
/// （右键菜单那条路径消费的是同一次按压的另一种合成结果，两者互斥），因此不会二次消费成 false。
/// Three invariants: neither clicks nor the wheel **take focus** (the island is <c>WS_EX_NOACTIVATE</c> and nothing calls
/// <c>Activate()</c>); a click on a media-action area consumes the hit but deliberately **does not** toggle the pin (a pinned island
/// survives the pointer leaving); and the one-shot suppression flag is consumed by exactly one place on the island — the window-level
/// left-button release (the context-menu path consumes the other kind of synthesis of the same press, and the two are mutually
/// exclusive), so it can never be consumed twice and read false.
/// </summary>
public partial class CapsuleIslandWindow
{
    /// <summary>本次按压的起点区域（按下时记录、抬起时读一次并清零，见 <see cref="CapsuleIslandPressSurface"/>）。/ This press's starting area, recorded on the press and taken once on the release (see <see cref="CapsuleIslandPressSurface"/>).</summary>
    private CapsuleIslandPressSurface _pressSurface;

    /// <summary>本次抬起是否是组合滚轮合成的那次点击；由窗口级左键抬起**一次性**取走，之后只读。/ Whether this release is the click synthesized by a chord wheel; taken **once** at the window-level left-button release and only read afterwards.</summary>
    private bool _suppressClickThisPress;

    /// <summary>气泡里当前显示的结果（用于在切歌真正落定后改写细节）；气泡没写过内容时为 null。/ The result the bubble currently shows, used to rewrite the detail once a skip really lands; null while nothing has been written.</summary>
    private WheelTooltipResult? _shownWheelResult;

    private GlobalInteractionRouter? _interactionRouter;
    private NativeMouseInputMonitor? _mouseInputMonitor;
    private ToolTip? _wheelTooltip;
    private bool _wheelDetailSubscriptionAttached;

    /// <summary>
    /// 接上岛内输入：滚轮路由、合成点击抑制、滚轮结果提示与中键绑定。
    /// Wires the island's input: the wheel route, chord-wheel click suppression, the wheel-result tooltip, and the middle-button
    /// binding.
    /// </summary>
    /// <param name="interactionRouter">与任务栏共用同一个滚轮执行器（容器单例）。/ The same wheel executor the taskbar uses (a container singleton).</param>
    /// <param name="mouseInputMonitor">与托盘共用同一个全局鼠标监听（容器单例），滚轮修饰键状态与合成点击标记都来自它。/ The same global mouse monitor the tray uses (a container singleton), the source of both the wheel modifier state and the synthesized-click flag.</param>
    private void InitializeIslandInput(
        GlobalInteractionRouter interactionRouter,
        NativeMouseInputMonitor mouseInputMonitor)
    {
        _interactionRouter = interactionRouter;
        _mouseInputMonitor = mouseInputMonitor;

        // 滚轮结果只用一个 ToolTip 实例：改内容不会重开气泡，用户才看得到"刚发生了什么"（重新赋 ToolTip 属性会先关掉它）。
        // 它随鼠标定位，并且**不**挂到任何元素的 ToolTip 属性上——挂上去会让鼠标划过岛时自动弹出一个空气泡。
        // One ToolTip instance carries the wheel result: changing its content does not reopen the bubble, so the user can see what
        // just happened (reassigning the ToolTip property would close it first). It follows the mouse and is deliberately **not**
        // assigned to any element's ToolTip property, which would pop an empty bubble whenever the pointer crossed the island.
        _wheelTooltip = new ToolTip
        {
            Placement = PlacementMode.Mouse,
            PlacementTarget = RootSurface,
        };
        // 指针离开岛或岛关闭时收起气泡：气泡跟着鼠标走，留着就会一直悬在桌面上。
        // The bubble is closed when the pointer leaves the island or the island closes: it follows the mouse and would otherwise
        // hang over the desktop forever.
        MouseLeave += (_, _) => _wheelTooltip.IsOpen = false;
        Closed += (_, _) => _wheelTooltip.IsOpen = false;

        // 组合滚轮（按住鼠标键滚动）结束时的松键会合成一次右键菜单：抑制标记在这里取走。
        // 标记的一次性由"左右键松开各自只合成一种结果"保证：按住左键滚动走左键抬起那条路径，按住右键滚动走这里，两者互斥。
        // Releasing the button after a chord wheel (a wheel scrolled while a mouse button is held) synthesizes a context menu: the
        // suppression flag is taken here. It stays one-shot because each button's release synthesizes only one kind of result: a
        // left-held chord wheel goes through the left-button release, a right-held one through this handler, and the two never
        // overlap.
        AddHandler(
            ContextMenuService.ContextMenuOpeningEvent,
            new ContextMenuEventHandler(Island_ContextMenuOpening),
            handledEventsToo: true);

        // 中键与左键走同一套点击绑定；左键的按下/抬起由窗口 XAML 接线，中键在这里单独补一条（它不参与拖拽与固定态）。
        // The middle button follows the same click bindings as the left one; the left button's press/release is wired in the
        // window's XAML, and the middle button gets its own handler here (it takes no part in dragging or pinning).
        AddHandler(
            PreviewMouseUpEvent,
            new MouseButtonEventHandler(Island_PreviewMouseUp),
            handledEventsToo: true);

        // 起点区域的两个复位点（抬起时的"读一次并清零"在 xaml.cs 的抬起处理器里）：
        // 捕获丢失（拖到别的元素抢走捕获、弹出模态框）与窗口关闭。少了它们，残留的"起点在媒体动作区"会被下一次抬起读到，
        // 而那次抬起可能根本没有按下记录（按下落在按钮上被早退、抬起落在岛外），于是凭空执行一次切歌/暂停/激活来源。
        // The starting area's two reset points (the "take once and clear" lives in xaml.cs's release handler):
        // capture loss (a drag that hands capture to another element, a modal dialog) and window close. Without them a leftover "press
        // started on a media-action area" would be read by a later release that has no press of its own — the press landed on a button
        // and exited early, or the release landed outside the island — running a skip/pause/activate-source out of nowhere.
        LostMouseCapture += (_, _) => _pressSurface.Reset();
        Closed += (_, _) => _pressSurface.Reset();

        // 气泡里"细节随媒体变化"的结果（切歌）要在新会话到达后改写一次：挂到岛上已有的 500ms 进度计时器上（第二个订阅者），
        // 不新起计时器、也不改 AdvanceProgress 本体。
        // A bubble result whose detail follows the media (a skip) is rewritten once the new session arrives: it rides the island's existing
        // 500 ms progress timer as a second subscriber, so no timer is added and AdvanceProgress itself is left alone.
        //
        // 订阅放在 Loaded 里而不是这里：_progressTimer 在构造函数里**晚于** InitializeIslandInput 才创建。
        // The subscription happens in Loaded rather than here: the constructor creates _progressTimer **after** InitializeIslandInput.
        Loaded += (_, _) =>
        {
            if (_wheelDetailSubscriptionAttached)
                return;

            _wheelDetailSubscriptionAttached = true;
            _progressTimer.Tick += (_, _) => RefreshWheelResultDetail();
        };
    }

    /// <summary>
    /// 记录本次按压的起点区域，供抬起时判定媒体动作与固定态。
    /// Records where this press started, which the release uses to decide the media action and the pin.
    /// </summary>
    /// <param name="source">按下时的原始命中元素。/ The original hit element of the press.</param>
    private void CapturePressSurface(DependencyObject? source) =>
        _pressSurface.Capture(ResolveMediaActionSurface(source));

    /// <summary>
    /// 视觉树祖先链上最近的媒体动作区：回答它是不是封面区（所有媒体动作区都带 <c>Tag="MediaAction"</c>，两个封面槽另以元素名区分），
    /// 整条链上没有媒体动作区时返回 null。
    /// The nearest media-action area on the visual-tree ancestor chain, answering whether it is a cover area (every media-action area
    /// carries <c>Tag="MediaAction"</c> while the two cover slots are told apart by name), or null when the chain holds none.
    /// </summary>
    private bool? ResolveMediaActionSurface(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { Tag: "MediaAction" } element)
                return ReferenceEquals(element, CardArtworkSlot) || ReferenceEquals(element, CapsuleArtworkSlot);

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    /// <summary>
    /// 执行一次媒体动作区的点击绑定：动作由 <c>Interaction</c> 设置里的封面/文字槽位解析（与任务栏同一份策略），
    /// <c>Disabled</c> 什么都不做但命中仍然被吃掉。
    /// Runs one media-action click binding: the action is resolved from the artwork/text slot in the <c>Interaction</c> settings (the
    /// same policy the taskbar uses), and <c>Disabled</c> does nothing while the hit stays consumed.
    /// </summary>
    /// <param name="artwork">是否落在封面区。/ Whether the click landed on a cover area.</param>
    private void ExecuteIslandMediaAction(bool artwork)
    {
        switch (PlayerClickBindingPolicy.Resolve(SettingsManager.Current.Interaction, artwork))
        {
            case PlayerClickAction.TogglePlayPause:
                // 不能暂停/播放时（断连、直播源）不发请求：与任务栏静置层同一道守卫。
                // No request while play/pause is unavailable (disconnected, a live source): the same guard the taskbar rest layer uses.
                if (_snapshot.CanPlayPause)
                    Execute(_viewModel.TogglePlayPauseCommand);
                break;

            case PlayerClickAction.OpenFullPanel:
                // 与右键菜单里的"打开完整层"同一条路径：宿主订阅该事件并给出锚点与目标显示器。
                // The same path as "open full panel" in the context menu: the host subscribes to this event and supplies the anchor and
                // target display.
                OpenFullPanelRequested?.Invoke(this, EventArgs.Empty);
                break;

            case PlayerClickAction.Disabled:
                // 不绑定：什么都不做，但这次点击已经算在媒体动作区上，不会再退回"切换固定态"。
                // Not bound: nothing happens, but the click still counts as a media-action hit and never falls back to pinning.
                break;

            default:
                Execute(_viewModel.ActivateMediaSourceCommand);
                break;
        }
    }

    /// <summary>
    /// 中键的媒体动作绑定：与左键共用同一份点击策略，只是不参与拖拽与固定态判定。
    /// The middle button's media-action binding: the same click policy the left button uses, without taking part in dragging or the
    /// pin decision.
    /// </summary>
    private void Island_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || _isClosing)
            return;

        // 中键的合成抑制不在讨论范围内：全局监听只为左右键写入抑制标记，中键不会产生它。
        // Synthesized-click suppression is out of scope here: the global monitor only writes the flag for the left and right buttons,
        // so a middle press can never produce one.
        if (ResolveMediaActionSurface(e.OriginalSource as DependencyObject) is not { } isArtwork)
            return;

        ExecuteIslandMediaAction(isArtwork);
        e.Handled = true;
    }

    /// <summary>
    /// 岛内滚轮：整块岛都是媒体滚轮面（胶囊态与卡片态一样），动作走与任务栏同一个 <see cref="GlobalInteractionRouter"/>。
    /// The in-island wheel: the whole island is a media wheel surface — capsule and card alike — and the action goes through the same
    /// <see cref="GlobalInteractionRouter"/> the taskbar uses.
    ///
    /// <c>PreviewMouseWheel</c> 是隧道事件，挂在根表面上因此**先于**子控件的处理器；三个自带滚轮语义的控件（输出设备、音量、进度条）
    /// 必须放行，否则"在音量按钮上滚动调音量"会变成切歌。事件在这里被标为已处理，滚轮因此不会继续冒泡到别处。
    /// <c>PreviewMouseWheel</c> is a tunneling event, so attaching it to the root surface runs it **before** the child controls'
    /// handlers; the three controls that carry wheel semantics of their own (output device, volume, seek slider) have to be released,
    /// otherwise "scroll over the volume button to change the volume" would skip a track. The event is marked handled here, so the
    /// wheel does not bubble on.
    /// </summary>
    private async void RootSurface_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_isClosing || _interactionRouter is null)
            return;

        if (!CapsuleIslandWheelPolicy.ShouldHandleMediaWheel(
                CardOutputDevice.IsMouseOver,
                CardVolume.IsMouseOver,
                SeekSlider.IsMouseOver))
        {
            return;
        }

        e.Handled = true;

        // 修饰键状态一律读全局监听：岛永不激活，WPF 的键盘状态在它身上没有可靠来源；三个取值与托盘读的是同一处。
        // The modifier state always comes from the global monitor: the island never activates, so WPF's keyboard state has no reliable
        // source here, and these three values are the ones the tray reads as well.
        var monitor = _mouseInputMonitor;
        var result = await _interactionRouter.ExecuteWheelAsync(
            e.Delta,
            monitor?.IsShiftDown ?? Keyboard.Modifiers.HasFlag(ModifierKeys.Shift),
            monitor?.IsLeftButtonDown ?? Mouse.LeftButton == MouseButtonState.Pressed,
            monitor?.IsRightButtonDown ?? Mouse.RightButton == MouseButtonState.Pressed);

        // 动作执行完把结果写给同一支提示："滚一下就看到刚才发生了什么"，用户不需要去别处确认。
        // The result is written into the one tooltip once the action completes: "scroll once and see what just happened", with no second
        // place for the user to check.
        if (_isClosing || _wheelTooltip is not { } tooltip || result is not { } wheelResult)
            return;

        _shownWheelResult = wheelResult;
        tooltip.Content = WheelTooltipPolicy.BuildResult(wheelResult.ActionName, wheelResult.Detail);
        tooltip.IsOpen = true;
    }

    /// <summary>
    /// 气泡打开期间改写"细节随媒体变化"的结果（切歌）。
    ///
    /// 切歌是异步的：滚动那一刻读到的是**切歌前**的曲名，结果因此带 <c>DetailFollowsMedia</c> 标记，
    /// 由进度计时器的每 500ms 轮询在新会话到达后改写成真正的曲名——与任务栏
    /// <c>TaskBarMediaControl.RefreshWheelResultDetail</c> 同一套做法。
    /// Rewrites a result whose detail follows the media (a skip) while the bubble is open.
    ///
    /// A skip is asynchronous: the title read at the moment of scrolling is the one from **before** the skip, so the result carries the
    /// <c>DetailFollowsMedia</c> flag and this 500 ms poll of the progress timer rewrites it with the real title once the new session
    /// arrives — the same approach <c>TaskBarMediaControl.RefreshWheelResultDetail</c> takes.
    ///
    /// 与 <c>AdvanceProgress</c> 不打架：两者是同一支 <see cref="DispatcherTimer"/> 的两个**独立订阅者**，
    /// 一个只写进度条控件、一个只写气泡 <c>Content</c>，谁也不读对方的状态，调用顺序因此无关紧要；
    /// 气泡没打开或结果不跟随媒体时本方法立刻返回，不产生任何工作量。
    /// It does not fight <c>AdvanceProgress</c>: the two are independent subscribers of the same <see cref="DispatcherTimer"/>, one writes
    /// the seek widgets and the other the bubble's <c>Content</c>, neither reads the other's state, so the invocation order does not matter;
    /// with the bubble closed or a result that does not follow the media this returns at once and costs nothing.
    /// </summary>
    private void RefreshWheelResultDetail()
    {
        if (_wheelTooltip is not { IsOpen: true } tooltip)
            return;

        var title = string.IsNullOrWhiteSpace(_snapshot.Title) ? null : _snapshot.Title;
        if (!CapsuleIslandWheelPolicy.ShouldRefreshWheelResultDetail(_shownWheelResult, title))
            return;

        var shown = _shownWheelResult!.Value;
        _shownWheelResult = shown with { Detail = title };
        tooltip.Content = WheelTooltipPolicy.BuildResult(shown.ActionName, title);
    }

    /// <summary>
    /// 右键菜单打开前的抑制：组合滚轮（按住右键滚动）结束时的松键会合成一次右键菜单，这一次必须被吞掉。
    /// Suppression before the context menu opens: releasing the right button after a chord wheel (a wheel scrolled while the right button
    /// is held) synthesizes a context menu, and that one has to be swallowed.
    /// </summary>
    private void Island_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (_mouseInputMonitor?.ConsumeSuppressedClick() != true)
            return;

        e.Handled = true;
        CloseIslandMenu();
    }
}
