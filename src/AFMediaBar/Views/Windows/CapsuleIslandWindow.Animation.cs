using System.Windows;
using System.Windows.Media;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Layout;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Views.Windows;

/// <summary>
/// 胶囊岛窗口的形态插值部分：胶囊 ↔ 卡片的宽/高/圆角/位置同步动画（随显示器刷新率逐帧、三次缓出）。
/// Shape-morphing half of the capsule-island window: synchronized width/height/corner-radius/position
/// interpolation between capsule and card (one step per displayed frame, cubic ease-out).
/// </summary>
public partial class CapsuleIslandWindow
{
    /// <summary>形态插值是否仍在推进。/ Whether the shape interpolation is still running.</summary>
    private bool _isShapeAnimating;

    /// <summary>上一帧的合成器呈现时间；<see cref="TimeSpan.MinValue"/> 表示尚未取到第一帧。/ Last compositor presentation time; MinValue means no frame has been taken yet.</summary>
    private TimeSpan _lastFrameTime = TimeSpan.MinValue;

    /// <summary>合成帧回调是否已挂上。/ Whether the compositor frame callback is attached.</summary>
    private bool _isFrameLoopAttached;

    /// <summary>本帧是否正在推进；用于挡掉同步重入的帧回调。/ Whether a frame is being advanced right now; blocks synchronously re-entered frame callbacks.</summary>
    private bool _isAdvancingFrame;

    /// <summary>
    /// 歌词时间轴是否仍需要逐帧推进：由 <c>AdvanceLyricHighlightFrame</c> 的返回值写入，摘除条件只读它。
    /// 用同一个值决定"继续还是摘除"，因此不会出现"还要跟随但回调已被摘掉"的错配。
    /// Whether the lyric timeline still needs a frame per step: written from <c>AdvanceLyricHighlightFrame</c>'s return value and read by
    /// the detach condition. One value decides both keeping and detaching the callback, so "still following but already detached" cannot
    /// happen.
    /// </summary>
    private bool _isLyricHighlightFrameNeeded;

    private bool _isExpanded;
    private bool _isPinned;
    private DynamicIslandEdge? _dockedEdge;
    private double _shapeProgress;

    /// <summary>展开那一刻的岛屿中心的屏上坐标：整个形变过程中窗口都围着这个焦点生长，岛因此"中心不动、四面伸展"。/ The island's centre on screen at the moment the expansion started: the whole morph grows around this focus point, which is what makes the island "stay centred while every side stretches".</summary>
    private Point _shapeFocus;

    private double _shapeStartWidth;
    private double _shapeStartHeight;

    /// <summary>展开为卡片（悬停/点击/拖拽）。/ Expands into the card (hover/click/drag).</summary>
    private void Expand(bool animated)
    {
        _isExpanded = true;
        Visibility = Visibility.Visible;
        BeginShapeAnimation(animated);
    }

    /// <summary>收拢为胶囊（指针离开）。/ Collapses into the capsule (pointer leaves).</summary>
    private void Collapse(bool animated)
    {
        _isExpanded = false;
        // 收起即解除固定：固定只描述"这一次展开要不要留住"，收起之后没有可留住的形态。
        // Collapsing releases the pin: the pin only describes whether this expansion is held open, and once collapsed there
        // is no open form to hold.
        _isPinned = false;
        BeginShapeAnimation(animated);

        // 收起后波形又需要逐帧推进了（展开时被有意摘掉，见 NeedsCapsuleSpectrumFrame）。动画那条路径在 BeginShapeAnimation
        // 里已经挂过帧回调，但**无过渡**那一档（系统关掉客户区动画）直接落位、不挂回调，于是波形要等到下一份快照才恢复；
        // EnsureFrameLoop 自己是幂等的，这里补一句把恢复变成确定行为。
        // Collapsing makes the waveform need a frame again (expanding deliberately detaches it, see NeedsCapsuleSpectrumFrame). The animated path
        // already attached the callback inside BeginShapeAnimation, but the **no-transition** tier (client-area animation switched off) lands the
        // shape without attaching anything, which would leave the waveform until the next snapshot. EnsureFrameLoop guards itself, so this one line
        // makes the recovery deterministic.
        if (NeedsCapsuleSpectrumFrame)
            EnsureFrameLoop();
    }

    /// <summary>
    /// 以当前可视尺寸为起点，向目标形态启动尺寸/圆角/位置/内容缩放的同步插值。
    /// Starts the synchronized interpolation of size, radius, position, and content scale from the current visual size toward the target shape.
    ///
    /// 起点尺寸就是**当前形态的尺寸**（收拢时是胶囊、展开时是卡片），不再额外记一份锚点：位置由
    /// <see cref="CapsuleIslandMorphPolicy.ResolveAnchor"/> 每帧从焦点重算，读的是窗口**当前**的 Left/Top，
    /// 因此别处（恢复位置、夹回工作区、DPI 重定位）改过位置也不会留下过期锚点。
    /// The start size is simply the **current form's** size (the capsule while collapsed, the card while expanded) and no second anchor is kept:
    /// the position is recomputed from the focus point every frame by <see cref="CapsuleIslandMorphPolicy.ResolveAnchor"/>, which reads the
    /// window's **current** Left/Top — so a position changed elsewhere (restoring, clamping, a DPI restore) can never leave a stale anchor behind.
    /// </summary>
    private void BeginShapeAnimation(bool animated)
    {
        var motion = MotionPolicy.ResolveCurrent();
        if (!animated || !motion.UseTransitions || !IsLoaded)
        {
            _isShapeAnimating = false;
            ApplyShapeTarget();
            return;
        }

        // 起点尺寸兜底取**当前形态的目标尺寸**，不是胶囊尺寸：收拢途中若 Width 读到 NaN，兜底成胶囊尺寸会让
        // 这一段的起点比实际小一截（尺寸突然跳一下），而当前形态的目标尺寸才是它的合理近似。
        // The fallback start size is the **current form's** target size, not the capsule's: if Width reads NaN mid-collapse,
        // falling back to the capsule size would make this run start from a much smaller size (a visible jump), while the
        // current form's target is the reasonable approximation of it.
        var fallback = ResolveShapeSize(_isExpanded);
        _shapeStartWidth = double.IsNaN(Width) || Width <= 0 ? fallback.Width : Width;
        _shapeStartHeight = double.IsNaN(Height) || Height <= 0 ? fallback.Height : Height;
        _shapeProgress = 0;

        // 焦点 = 展开前岛屿的中心：窗口尺寸在下面每帧都围着它长，因此"岛不动、四面伸展"。
        // The focus is the island's centre before the expansion: every frame below grows the window around it, which is what makes the island
        // stay put while each side stretches.
        _shapeFocus = new Point(Left + _shapeStartWidth / 2, Top + _shapeStartHeight / 2);

        _isShapeAnimating = true;
        EnsureFrameLoop();
    }

    /// <summary>
    /// 挂上合成帧回调：每个显示帧一次，与显示器刷新对齐。
    ///
    /// 原先用 16ms 的 DispatcherTimer 驱动，默认 Background 优先级排在渲染与输入之后，实际帧间隔常是它的两三倍，
    /// 而且每次调用都把"经过 16ms"写死传给插值器，于是形变既慢又顿。改由合成器驱动、按真实帧间隔推进后，
    /// 刷新率只决定步进有多细，不决定形变快慢。
    /// Attaches the compositor frame callback: once per displayed frame, aligned with the monitor refresh.
    ///
    /// The old 16 ms DispatcherTimer ran at the Background dispatcher priority, which sits behind rendering and input, so
    /// its real interval was often two or three times the nominal one — and every call handed the interpolator a hard-coded
    /// "16 ms elapsed", which made the morph both slow and choppy. Driven by the compositor with the real frame interval,
    /// the refresh rate decides how fine the steps are, never how long the morph takes.
    /// </summary>
    private void EnsureFrameLoop()
    {
        if (_isFrameLoopAttached || _isClosing)
            return;

        _lastFrameTime = TimeSpan.MinValue;
        CompositionTarget.Rendering += OnCompositorFrame;
        _isFrameLoopAttached = true;
    }

    private void StopFrameLoop()
    {
        if (!_isFrameLoopAttached)
            return;

        CompositionTarget.Rendering -= OnCompositorFrame;
        _isFrameLoopAttached = false;
        _lastFrameTime = TimeSpan.MinValue;
        _isLyricHighlightFrameNeeded = false;
    }

    /// <summary>没有形态动画、也没有需要滚动的跑马灯、歌词时间轴与波形都不必推进时摘掉帧回调，避免空转一直占着渲染管线。
    /// Detaches the frame callback once neither the shape, the marquee, nor the lyric timeline nor the waveform needs it, so nothing keeps the
    /// render pipeline busy.</summary>
    private void StopFrameLoopWhenIdle()
    {
        if (!_isShapeAnimating &&
            !(_marqueeActive && !_isExpanded) &&
            !IsLyricHighlightFrameNeeded &&
            !NeedsCapsuleSpectrumFrame)
        {
            StopFrameLoop();
        }
    }

    /// <summary>
    /// 歌词时间轴当前是否仍需要逐帧推进：已挂上帧回调、窗口未在关闭、且调用方判定仍要继续（见
    /// <see cref="AdvanceLyricHighlightFrame"/> 的返回值）。
    /// Whether the lyric timeline still needs a frame per step: the callback is attached, the window is not closing, and the caller says
    /// it must go on (see the return value of <see cref="AdvanceLyricHighlightFrame"/>).
    /// </summary>
    private bool IsLyricHighlightFrameNeeded =>
        _isFrameLoopAttached && !_isClosing && _isLyricHighlightFrameNeeded;

    /// <summary>
    /// 波形是否仍需逐帧推进（编号 107，接缝 S1）。
    ///
    /// 判定的输入必须是**状态字段**（快照里是否在播放），不能是"上一帧推进的结论"：<c>ApplySnapshot</c> 在 <c>IsPlaying</c> 翻转时
    /// 挂上帧回调，而同一帧收尾的摘除判定紧接着就会执行——若那时读的是"上一帧有没有推进"，刚挂上的回调会被立刻摘掉，
    /// 波形停在最后一帧、擦亮永不推进（第二波在歌词与跑马灯上踩过同一个坑）。
    ///
    /// **展开时不推进**：卡片态下胶囊整块不可见，逐帧重画它没有意义，更不该每 50ms 调一次 <c>GetSpectrum</c>（WASAPI 回环采集有真实
    /// 开销）。收起时由 <c>Collapse</c> 重新挂上（见那里的注释）。
    /// Whether the waveform still needs a frame per step (item 107, seam S1).
    ///
    /// The input has to be a **state field** (is playback running in the snapshot, is the island expanded), never "last frame's conclusion": ApplySnapshot attaches the
    /// callback the moment <c>IsPlaying</c> flips, and the detach check at the end of that very frame runs right after — reading "did the previous
    /// frame advance?" there would take the fresh callback straight back off, leaving the waveform frozen on its last frame and the reveal never
    /// advancing (the same trap wave two walked into with the lyrics and the marquee).
    ///
    /// **Nothing advances while expanded**: the whole capsule is invisible in the card state, so repainting it per frame is pointless and calling
    /// <c>GetSpectrum</c> every fifty milliseconds is worse (a WASAPI loopback capture has real cost). Collapsing re-attaches it — see the note there.
    /// </summary>
    private bool NeedsCapsuleSpectrumFrame =>
        !_isClosing && !_isExpanded && _snapshot.IsConnected && _snapshot.IsPlaying;

    /// <summary>
    /// 一帧：推进形态与跑马灯，然后在没有后续需求时摘掉回调。
    /// One frame: advances the morph and the marquee, then detaches the callback when nothing needs it.
    ///
    /// 必须挡重入：给 <c>Window.Width/Height</c> 赋值会让 WPF 同步发 <c>WM_SIZE</c>，合成器**同步**再回调一次本方法。
    /// 重入的那一帧会拿旧状态再推进一步（甚至把已经落定的目标尺寸又改掉），展开时进度翻倍、收拢时高度被写成中间值，
    /// 卡片就卡成又矮又半透明的方块。一帧只推进一次，其余的重入直接丢弃。
    /// Re-entrancy has to be blocked: assigning <c>Window.Width/Height</c> makes WPF post <c>WM_SIZE</c> synchronously, and
    /// the compositor calls this method again **synchronously**. That re-entered frame advances a second step from stale
    /// state — and can even overwrite a size that had just settled — so an expansion runs at double speed and a collapse
    /// writes an intermediate height, leaving the card wedged as a short, half-transparent block. One step per frame; any
    /// re-entrant call is dropped.
    /// </summary>
    private void OnCompositorFrame(object? sender, EventArgs e)
    {
        if (_isClosing)
        {
            StopFrameLoop();
            return;
        }

        if (_isAdvancingFrame)
            return;

        _isAdvancingFrame = true;
        try
        {
            var now = (e as RenderingEventArgs)?.RenderingTime ?? TimeSpan.Zero;
            var elapsed = _lastFrameTime == TimeSpan.MinValue || now <= _lastFrameTime
                ? MarqueeTiming.FrameInterval.TotalMilliseconds
                : (now - _lastFrameTime).TotalMilliseconds;
            _lastFrameTime = now;

            if (_isShapeAnimating)
                AdvanceShapeAnimation(elapsed);

            // 歌名不再逐帧推进（用户裁定：显示不全无所谓，优先显示歌词），因此这里不再有 AdvanceMarquee。
            // The title no longer advances per frame (the user's ruling: a partially shown title is fine, lyrics come first), so there is no
            // AdvanceMarquee call here any more.
            // 返回值必须记下来：它同时决定"本帧继续推进"与"是否还留着帧回调"，两者用同一个值才不会错配。
            // The return value has to be kept: one value decides both "keep advancing" and "keep the callback", and only then can the two
            // never disagree.
            _isLyricHighlightFrameNeeded = AdvanceLyricHighlightFrame();

            // 波形与采样节流也挂在同一条帧源上（红线 5：不外起 DispatcherTimer）；它自己按 RefreshRateHz 数时间。
            // The waveform and its sampling throttle hang off the same frame source (red line 5: no extra DispatcherTimer); it counts the time
            // itself from RefreshRateHz.
            AdvanceCapsuleSpectrum(elapsed);
        }
        finally
        {
            _isAdvancingFrame = false;
        }

        StopFrameLoopWhenIdle();
    }

    /// <summary>
    /// 直接落到目标形态（无过渡或尚未加载时）。
    /// Jumps straight to the target shape (no transition or not loaded yet).
    /// </summary>
    private void ApplyShapeTarget()
    {
        var target = ResolveShapeSize(_isExpanded);
        var capsule = CurrentCapsuleSize;
        var card = CurrentCardSize;

        // 落位时"卡片占比"就是目标形态本身：展开落位是 1（纯卡片），收拢落位是 0（纯胶囊）。
        // **这里曾写死成 1**，于是收拢落位时用**卡片**的宽度去算位置，窗口明明已经缩回胶囊大小，却停在卡片左缘
        // （实测 1074 而不是 1164）——用户看到的"伸展后回不到紧凑形态"就是这个错位。
        // Landing onto the target means the share of the card *is* the target form: one for a landing on the card, zero for one on the capsule.
        // **This used to be hard-coded to one**, so a collapse computed its position from the **card's** width and parked the window at the card's
        // left edge even though its size had already shrunk back (measured: 1074 instead of 1164) — the misplacement the user read as "after
        // stretching it cannot return to the compact form".
        var cardShare = _isExpanded ? 1.0 : 0.0;

        // 落位时焦点取**当前窗口的中心**：直接落位可能发生在别处刚改过位置之后（恢复保存位置、夹回工作区、DPI 重定位），
        // 用此刻真实的 Left/Top 才算得出"这个位置上是哪一点不动"。
        // The landing takes the **current window's centre** as its focus: a direct landing can happen right after a position change made elsewhere
        // (restoring, clamping, a DPI restore), and only the real Left/Top at this instant can say which point stays put.
        if (double.IsFinite(Left) && double.IsFinite(Top))
            _shapeFocus = new Point(Left + Width / 2, Top + Height / 2);

        Width = target.Width;
        Height = target.Height;
        RootSurface.CornerRadius = new CornerRadius(
            CapsuleIslandMorphPolicy.ResolveCornerRadius(cardShare, capsule, card, ResolveCardRadius()));
        KeepShapeAnchor(cardShare);
        WriteCardContentScale(cardShare);

        // 直接落位就是"动画已经走完"：行进度取 1，方向由 _isExpanded 决定，因此收拢态落位时卡片是 0、胶囊是 1。
        // Landing straight onto the target means the animation is already over: the row progress is one and the direction comes from _isExpanded, so a
        // collapse lands with the card at zero and the capsule at one.
        ApplyViewOpacities(1);
        ApplyArtworkVisibility();
        ApplyCapsuleViewSize();
        ClampToWorkArea();
    }

    /// <summary>缩放后的胶囊态尺寸。/ The scaled collapsed capsule size.</summary>
    private Size CurrentCapsuleSize => CapsuleIslandMetrics.ResolveCapsuleSize(_islandScale);

    /// <summary>缩放后的卡片态尺寸。/ The scaled expanded card size.</summary>
    private Size CurrentCardSize => CapsuleIslandMetrics.ResolveCardSize(_islandScale);

    /// <summary>当前形态的目标尺寸（已乘缩放系数）。/ The current form's target size, already scaled.</summary>
    /// <param name="expanded">是否为卡片态。/ Whether the card form is the target.</param>
    private Size ResolveShapeSize(bool expanded) => expanded ? CurrentCardSize : CurrentCapsuleSize;

    /// <summary>设置里的卡片圆角（按缩放系数换算后），只在形变终点使用。/ The card's configured corner radius, scaled, used at the morph's end point only.</summary>
    private double ResolveCardRadius() => ResolveCornerRadius(
        SettingsManager.Current.DynamicIslandSurface.CornerRadiusDip,
        CapsuleIslandMetrics.CardCornerRadius,
        CurrentCardSize.Width,
        CurrentCardSize.Height);

    /// <summary>
    /// 把 <c>CapsuleView</c>、其中的 32×32 封面槽、左内缩列与波形列改成缩放后的胶囊尺寸：它显式钉着尺寸来抵消窗口拉伸，
    /// 缩放后不同步就会被窗口拉变形（窗口已是 232×52 而视图还是 180×40，封面槽与标题的比例全错）。
    /// Rewrites <c>CapsuleView</c>, its 32×32 artwork slot, the leading inset column, and the waveform column to the scaled capsule size: the view
    /// is explicitly pinned to cancel the window's stretch, so without this it gets deformed instead (the window at 232×52 against a 180×40 view
    /// throws off every proportion between the slot and the title).
    /// </summary>
    private void ApplyCapsuleViewSize()
    {
        var size = CurrentCapsuleSize;
        var slot = 32 * _islandScale;

        CapsuleView.Width = size.Width;
        CapsuleView.Height = size.Height;
        // 左内缩是"列宽"（固定 DIP），缩放只能由这里写；它不出现在 XAML 的缩放路径里，所以必须和槽位一起改写。
        // The leading inset is a column width (a fixed DIP), so scaling it is code's job here; it has no other scaling path and
        // therefore has to be rewritten together with the slot.
        CapsuleLeadInset.Width = new GridLength(CapsuleIslandMetrics.CapsuleLeadInsetDip * _islandScale);
        CapsuleArtworkSlot.Width = slot;
        CapsuleArtworkSlot.Height = slot;
        CapsuleArtworkSlot.CornerRadius = new CornerRadius(slot / 2);

        // 波形列（编号 107，裁定 10）：列宽与元素尺寸都是固定 DIP，缩放只能写在这里——XAML 里写的 42×22 只在 scale=1 时对。
        // 尺寸每次**从设置解算**（段数决定宽度、内容区高度决定高度，且**与样式无关**，因此切样式时文字槽不会跳），
        // 内容层再用同一个系数做 RenderTransform；几何本身仍是内容区的局部坐标，缩放不重算任何一点（接缝 S5/S6）。
        // The waveform column (item 107, ruling 10): both the column width and the element size are fixed DIP, so their scaling can only be written
        // here — the 42×22 in XAML is only correct at scale one. The size is **solved from the settings** every time (the band count decides the width
        // and the content height the height, and it is **style-independent**, so switching styles never moves the text slot); the content layer then
        // applies the same factor as a RenderTransform, so the geometry stays in the content area's local coordinates and no point is ever recomputed
        // for a scale (seams S5/S6).
        var spectrum = SettingsManager.Current.SpectrumComponent;
        var content = CapsuleSpectrumPolicy.ResolveContentSizeDip(spectrum);
        var host = CapsuleSpectrumPolicy.ResolveHostSizeDip(spectrum, _islandScale);
        CapsuleSpectrumHost.Width = host.Width;
        CapsuleSpectrumHost.Height = host.Height;
        CapsuleSpectrumContent.Width = content.Width;
        CapsuleSpectrumContent.Height = content.Height;
        CapsuleSpectrumScale.ScaleX = _islandScale;
        CapsuleSpectrumScale.ScaleY = _islandScale;
    }

    /// <summary>
    /// 按是否有封面切换封面图与占位音符图标：占位态下无封面，图标必须出现，否则胶囊里只剩一行文字。
    /// Switches between artwork and the placeholder note icon: the placeholder state has no artwork, so the icon has to
    /// appear or the capsule would be left with a bare line of text.
    /// </summary>
    private void ApplyArtworkVisibility()
    {
        var artworkVisibility = _isPlaceholder ? Visibility.Collapsed : Visibility.Visible;
        var iconVisibility = _isPlaceholder ? Visibility.Visible : Visibility.Collapsed;
        CapsuleArtwork.Visibility = artworkVisibility;
        CardArtwork.Visibility = artworkVisibility;
        CapsulePlaceholderIcon.Visibility = iconVisibility;
        CardPlaceholderIcon.Visibility = iconVisibility;
    }

    /// <summary>
    /// 推进一帧形态动画：宽/高/圆角共享同一进度（三次缓出），贴边侧保持不动，自由态围绕中心。
    /// Advances one morphing frame: width/height/radius share one eased progress; the docked side stays put
    /// while the free form grows around its center.
    /// </summary>
    /// <param name="elapsedMilliseconds">本帧真实经过的毫秒数（来自合成器）。/ Real elapsed milliseconds of this frame, from the compositor.</param>
    private void AdvanceShapeAnimation(double elapsedMilliseconds)
    {
        if (_isClosing || _isPressed)
        {
            _isShapeAnimating = false;
            ApplyShapeTarget();
            return;
        }

        var card = CurrentCardSize;
        var capsule = CurrentCapsuleSize;
        var duration = MotionPolicy.ResolveCurrent().PositionDuration.TotalMilliseconds;

        // 尺寸、圆角、位置与内容缩放**全部由同一条缓动曲线上的一个进度算出**，因此窗口边界与里面的内容永远同步；
        // 早先位置钉死在贴边侧、圆角又与尺寸同用一条被夹紧的曲线，三者会在动画中途互相错拍，那正是"生硬"的来源。
        // Size, radius, position, and content scale are **all resolved from one progress on one eased curve**, so the frame and its content can never
        // fall out of step; the position used to be pinned on the docked side and the radius shared one clamped curve with the size, which let the
        // three drift apart mid-animation — the source of the "stiff" look.
        if (double.IsFinite(duration) && duration > 0)
            _shapeProgress = Math.Clamp(_shapeProgress + Math.Max(0, elapsedMilliseconds) / duration, 0, 1);

        var eased = CapsuleIslandMorphPolicy.EaseInOut(_shapeProgress);
        var cardShare = CapsuleIslandMorphPolicy.CardShare(_isExpanded, eased);
        var isCompleted = _shapeProgress >= 1;

        // 尺寸、圆角、位置与内容缩放全部由 `cardShare`（卡片占比）驱动：收拢时它是补数，
        // 因此窗口从卡片**缩回**胶囊，而不是像曾经那样"永远从胶囊长到卡片"（收拢第一帧就被写成胶囊宽，然后再也回不去）。
        // Size, radius, position, and content scale are all driven by `cardShare` (the share of the card): on a collapse it is the complement, so the
        // window shrinks from the card back into the capsule instead of the old "always grow capsule → card", which wrote the capsule's width on the
        // collapse's first frame and could never come back.
        Width = capsule.Width + (card.Width - capsule.Width) * cardShare;
        Height = capsule.Height + (card.Height - capsule.Height) * cardShare;
        RootSurface.CornerRadius = new CornerRadius(
            CapsuleIslandMorphPolicy.ResolveCornerRadius(cardShare, capsule, card, ResolveCardRadius()));
        KeepShapeAnchor(cardShare);
        WriteCardContentScale(cardShare);
        ApplyViewOpacities(_shapeProgress);

        if (isCompleted)
        {
            _isShapeAnimating = false;
            Width = ResolveShapeSize(_isExpanded).Width;
            Height = ResolveShapeSize(_isExpanded).Height;
            RootSurface.CornerRadius = new CornerRadius(
                CapsuleIslandMorphPolicy.ResolveCornerRadius(_isExpanded ? 1 : 0, capsule, card, ResolveCardRadius()));
            KeepShapeAnchor(_isExpanded ? 1 : 0);
            WriteCardContentScale(_isExpanded ? 1 : 0);

            // 这里必须把**两个视图**按最终形态落定，不能写死 "卡片不透明"：
            // 曾经这里写的是 ApplyViewOpacities(1)，收拢时会把上一行刚按进度写好的"卡片 0 / 胶囊 1"再覆盖成"卡片 1 / 胶囊 0"——
            // 窗口已经缩回胶囊尺寸，卡片内容却留在原位盖住胶囊，收起的岛里显示的是**卡片字号**的标题。
            // 现在行进度固定为 1，方向由 _isExpanded 决定，收拢与展开因此各自落在正确的终点上。
            // Both views have to be settled from the final form here, never a hard-coded "card is opaque": this used to read
            // ApplyViewOpacities(1), and on a collapse that overwrote the "card 0 / capsule 1" the previous line had just written with
            // "card 1 / capsule 0" — the window had already shrunk back to the capsule's size while the card content stayed on top of it, so the
            // collapsed island showed the **card's** title. The row progress is fixed at one and the direction comes from _isExpanded, so an
            // expansion and a collapse each land on their own correct end point.
            ApplyViewOpacities(1);
            ClampToWorkArea();
            if (!_isExpanded)
            {
                SettingsManager.Current.DynamicIslandLeft = Left;
                SettingsManager.Current.DynamicIslandTop = Top;
            }
        }
    }

    /// <summary>
    /// 形态尺寸与位置的一次求解：中心停在开始时的焦点上（四面伸展），贴到屏幕某条边时那一面改为钉住（不伸展）。
    ///
    /// 四个贴边分支原先都把左/上角钉死，于是顶边停靠的卡片只向右长，窗口里居中的胶囊内容被推着一起右移——
    /// 对着胶囊点下去，歌名和封面会横着滑出去半个身位。现在贴边只决定"哪一面不动"，另一轴一律围绕焦点居中生长。
    /// One resolution of the morph's size and position: the centre rests on the focus point captured at the start (every side stretches), and a side
    /// docked to a screen edge is pinned instead (that side does not stretch).
    ///
    /// All four branches used to pin the left/top corner, so a top-docked card grew to the right only and the capsule content centred inside the
    /// window got pushed sideways with it — hovering the capsule slid the title and thumbnail half a body to the side. Docking now only decides
    /// **which side stays put**; the other axis always grows centred on the focus point.
    /// </summary>
    /// <param name="cardShare">卡片占比（0 = 胶囊，1 = 卡片）。/ The share of the card (zero is the capsule, one the card).</param>
    private void KeepShapeAnchor(double cardShare)
    {
        var anchor = CapsuleIslandMorphPolicy.ResolveAnchor(
            cardShare,
            CurrentCapsuleSize,
            CurrentCardSize,
            _shapeFocus.X,
            _shapeFocus.Y,
            _dockedEdge,
            GetCurrentWorkArea());

        Left = anchor.Left;
        Top = anchor.Top;
    }

    /// <summary>
    /// 把卡片内容的缩放写进渲染变换：内容因此随进度从小长到 1（铺满窗口）。
    ///
    /// 用 <c>RenderTransform</c> 而不是 <c>LayoutTransform</c>：形变每帧都在改尺寸，布局变换会让整棵树每帧重新排版并逐帧重排文字，
    /// 那既贵又会让文字在动画里不停跳动；渲染变换只影响绘制，动画结束后缩放恰好是 1，因此不会留下任何长期缩放的位图。
    /// 缩放挂在卡片自己身上、以它自己的左上角为原点：窗口坐标里的原点固定，内容因此是从窗口左上角稳定地长出来的，不会左右漂。
    /// The card content's scale is written into a render transform, so the content grows from small to one (filling the window) with the progress.
    ///
    /// A render transform rather than a layout transform: the morph resizes every frame, and a layout transform would re-run layout for the whole tree
    /// and re-flow the text on each of those frames — expensive, and it makes the text jitter throughout the animation; a render transform only
    /// affects drawing, and since the final scale is exactly one it leaves no permanently scaled bitmap behind. The scale hangs off the card itself
    /// with its own top-left as the origin, so the origin is fixed in window coordinates and the content grows steadily from that corner instead of
    /// drifting sideways.
    /// </summary>
    /// <param name="cardShare">卡片占比（0 = 胶囊，1 = 卡片）。/ The share of the card (zero is the capsule, one the card).</param>
    private void WriteCardContentScale(double cardShare)
    {
        var scale = CapsuleIslandMorphPolicy.ResolveContentScale(cardShare, CurrentCapsuleSize, CurrentCardSize);
        if (CardView.RenderTransform is not ScaleTransform transform)
        {
            transform = new ScaleTransform(1, 1);
            CardView.RenderTransform = transform;
        }

        transform.ScaleX = scale;
        transform.ScaleY = scale;
    }

    /// <summary>
    /// 按**行进度**切换两个视图的不透明度与命中（胶囊先渐隐、卡片后渐显，两边从不半透明地重叠）。
    /// 收拢是展开的逆过程，方向由 <paramref name="isExpanded"/> 交给策略判断，调用方只报"这一帧走到哪了"。
    /// Cross-fades the two views' opacity and hit testing from the **row progress** (the capsule fades out first and the card fades in after, never
    /// half transparent on top of each other). A collapse is the expansion reversed: the direction comes from the policy through
    /// <paramref name="isExpanded"/> and the caller only reports how far this frame has come.
    /// </summary>
    /// <param name="rowProgress">未缓动的行进度（0 = 动画起点，1 = 目标形态）。/ The un-eased row progress (zero is the animation's start, one the target form).</param>
    private void ApplyViewOpacities(double rowProgress)
    {
        var (cardOpacity, capsuleOpacity) = CapsuleIslandMorphPolicy.ResolveViewOpacities(_isExpanded, rowProgress);
        CardView.Opacity = cardOpacity;
        CardView.IsHitTestVisible = _isExpanded;
        CapsuleView.Opacity = capsuleOpacity;
        CapsuleView.IsHitTestVisible = !_isExpanded;
    }

    /// <summary>停止形态动画并摘掉帧回调。/ Stops the shape animation and detaches the frame callback.</summary>
    private void StopShapeAnimation()
    {
        _isShapeAnimating = false;
        StopFrameLoop();
    }
}


