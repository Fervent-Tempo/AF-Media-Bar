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
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Layout;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Components;

/// <summary>
/// 任务栏媒体控件的动效辅助实现。/ Motion helpers for the taskbar media control.
/// </summary>
public partial class TaskBarMediaControl
{
    private MotionProfile CurrentMotion => MotionPolicy.ResolveCurrent();

    private static PowerEase CreateEaseOut() => new() { Power = 3, EasingMode = EasingMode.EaseOut };

    private static PowerEase CreateEaseInOut() => new() { Power = 3, EasingMode = EasingMode.EaseInOut };

    // 文字保留原文，溢出时仅移动两份绘制；暂停保留时钟位置，卸载才解除时钟。
    internal void ApplyMarqueeLayout(double availableWidth)
    {
        var enabled = IsLoaded && IsBarPresentation && !_isVertical &&
                      (_isConnected || _restTransitionKeepsOutgoingText) && CurrentMotion.UseContinuousMotion;
        var paused = IsAdvancePruned || !IsVisible || SongMetadataPanel.Visibility != Visibility.Visible ||
                     HoverRevealHost.Visibility == Visibility.Visible || _restTransitionActive || _restTransitionPreparing;
        foreach (var state in _marqueeTexts)
        {
            var rowVisible = !ReferenceEquals(state.Element, SongArtist) || SongArtistContainer.Visibility == Visibility.Visible;
            state.Configure(enabled && rowVisible, availableWidth, paused, ResolveConfiguredAlignment(state.Element));
        }
    }

    private void StopMarqueeAnimations()
    {
        foreach (var state in _marqueeTexts)
        {
            state.Stop();
            state.Element.TextAlignment = ResolveConfiguredAlignment(state.Element);
        }
    }

    private void ApplyConfiguredTextAlignment(TextBlock element, TextAlignment alignment)
    {
        if (_marqueeTexts.Any(state => ReferenceEquals(state.Element, element) && state.Advancing)) return;
        element.TextAlignment = alignment;
    }

    private TextAlignment ResolveConfiguredAlignment(TextBlock element) =>
        PresentationSettings.TaskbarExperience.Normalize().MediaTextAlignment switch
        {
            TaskbarMediaTextAlignment.Center => TextAlignment.Center,
            TaskbarMediaTextAlignment.Right => TextAlignment.Right,
            _ => TextAlignment.Left
        };

    /// <summary>Only measurement inputs invalidate text metrics; available width does not change glyph advances.</summary>
    internal readonly record struct MarqueeFontKey(
        FontFamily Family, FontStyle Style, FontWeight Weight, FontStretch Stretch,
        double Size, System.Globalization.CultureInfo Culture, double PixelsPerDip,
        TextFormattingMode FormattingMode, FlowDirection Direction)
    {
        // WPF font structs do not all implement IEquatable<T>; default record comparisons box them on every cache hit.
        public bool Equals(MarqueeFontKey other) =>
            Equals(Family, other.Family) && Style == other.Style && Weight == other.Weight && Stretch == other.Stretch &&
            Size == other.Size && Equals(Culture, other.Culture) && PixelsPerDip == other.PixelsPerDip &&
            FormattingMode == other.FormattingMode && Direction == other.Direction;

        public override int GetHashCode() => HashCode.Combine(
            Family, Style.GetHashCode(), Weight.GetHashCode(), Stretch.GetHashCode(), Size, Culture, PixelsPerDip,
            HashCode.Combine(FormattingMode, Direction));

        internal static MarqueeFontKey Capture(TextBlock element) => new(
            element.FontFamily, element.FontStyle, element.FontWeight, element.FontStretch,
            element.FontSize, element.Language.GetSpecificCulture(),
            VisualTreeHelper.GetDpi(element).PixelsPerDip, TextOptions.GetTextFormattingMode(element), element.FlowDirection);
    }

    private static double MeasureTextWidth(string text, TextBlock source, bool metadata = false) =>
        (metadata ? MeasureMetadataText(text, MarqueeFontKey.Capture(source)).Width : MeasureTextWidthExact(text, source)) + 4;

    // 歌词与其他展示模式保留原有测量条件；横向媒体文字单独匹配实际 WPF 绘制。
    private static double MeasureTextWidthExact(string text, TextBlock source)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(source.FontFamily, source.FontStyle, source.FontWeight, source.FontStretch),
            source.FontSize, Brushes.Transparent, VisualTreeHelper.GetDpi(source).PixelsPerDip)
        { Trimming = TextTrimming.None }.WidthIncludingTrailingWhitespace;
    }

    internal static (double Width, double LeadingInset) MeasureMetadataText(string text, MarqueeFontKey font)
    {
        if (string.IsNullOrEmpty(text))
            return (0, 0);

        var formatted = new FormattedText(
            text,
            font.Culture,
            font.Direction,
            new Typeface(font.Family, font.Style, font.Weight, font.Stretch),
            font.Size,
            Brushes.Transparent,
            null,
            font.FormattingMode,
            font.PixelsPerDip)
        {
            Trimming = TextTrimming.None
        };
        // Display 模式会逐字形对齐像素；按实际渲染条件测量，另外保留斜体等越出排版边界的墨迹。
        var leading = Math.Max(0, -formatted.OverhangLeading);
        var trailing = Math.Max(0, -formatted.OverhangTrailing);
        return (formatted.WidthIncludingTrailingWhitespace + leading + trailing, leading);
    }

    /// <summary>
    /// 入场动画：标题/艺术家变化时触发淡入和左滑效果。
    /// Entrance animation: fade-in and left-slide effect when title/artist changes.
    /// </summary>
    private void AnimateEntrance()
    {
        try
        {
            if (_restTransitionActive)
                return;
            var motion = CurrentMotion;
            if (!motion.UseTransitions || _isHostVisibilitySuspended)
            {
                SongInfoStackPanel.BeginAnimation(OpacityProperty, null);
                SongInfoStackPanel.Opacity = _isTaskbarHoverVisible ? TaskbarCoveredOpacity : 1;
                // 只归位入场位移支（不动放大推挤支）。
                // Only settle the entrance branch; leave the zoom-push branch untouched.
                if (_songInfoEntranceTransform is not null)
                {
                    _songInfoEntranceTransform.BeginAnimation(TranslateTransform.XProperty, null);
                    _songInfoEntranceTransform.X = 0;
                }
                return;
            }

            var duration = motion.StandardDuration;
            var opacityAnimation = new DoubleAnimation
            {
                From = 0.0,
                To = _isTaskbarHoverVisible ? TaskbarCoveredOpacity : 1.0,
                Duration = duration,
                EasingFunction = CreateEaseOut()
            };
            var translateAnimation = new DoubleAnimation
            {
                From = -6,
                To = 0,
                Duration = duration,
                EasingFunction = CreateEaseOut()
            };

            SongInfoStackPanel.BeginAnimation(OpacityProperty, opacityAnimation);
            // 入场位移走专用支（与放大推挤支同处一个 TransformGroup），换歌时不再整体换掉 RenderTransform，
            // 推挤位移因此得以保留（BUG 2 修复）。
            // The entrance slide uses its dedicated branch (sharing a TransformGroup with the zoom-push branch), so a track
            // change no longer replaces the whole RenderTransform and the push survives (BUG 2 fix).
            var entrance = EnsureSongInfoEntranceTransform();
            entrance.BeginAnimation(TranslateTransform.XProperty, translateAnimation);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    /// <summary>
    /// 取得（或建立）文字区入场位移支，并保证它与放大推挤支同处一个 TransformGroup：两者若共用同一个 RenderTransform
    /// 实例，换歌时的入场动画会整体换掉它、抹掉推挤（BUG 2 根因）。
    /// Gets (or builds) the text region's entrance branch and ensures it shares a TransformGroup with the zoom-push branch: had
    /// the two shared one RenderTransform instance, the track-change entrance would replace it outright and wipe the push
    /// (root of BUG 2).
    /// </summary>
    private TranslateTransform EnsureSongInfoEntranceTransform()
    {
        _songInfoEntranceTransform ??= new TranslateTransform();
        if (SongInfoStackPanel.RenderTransform is TransformGroup group)
        {
            if (!group.Children.Contains(_songInfoEntranceTransform))
            {
                if (_artworkHover._songInfoPushTransform is not null && !group.Children.Contains(_artworkHover._songInfoPushTransform))
                    group.Children.Add(_artworkHover._songInfoPushTransform);
                group.Children.Add(_songInfoEntranceTransform);
            }
        }
        else
        {
            var newGroup = new TransformGroup();
            if (_artworkHover._songInfoPushTransform is not null)
                newGroup.Children.Add(_artworkHover._songInfoPushTransform);
            newGroup.Children.Add(_songInfoEntranceTransform);
            SongInfoStackPanel.RenderTransform = newGroup;
        }
        return _songInfoEntranceTransform;
    }

    private bool _isPlacementMotionActive;

    /// <summary>位置恢复期间收起悬停呈现；不暂停媒体、歌词或业务服务。</summary>
    public void ApplyPlacementMotionSuspension(bool suspended)
    {
        if (_isPlacementMotionActive == suspended) return;
        _isPlacementMotionActive = suspended;
        if (suspended)
        {
            _artworkHover.AnimateArtworkHover(false, immediate: true);
            HideTaskbarHoverLayer(immediate: true);
        }
        else if (CanUseTaskbarComponentHover() && PresentationSettings.TaskbarExperience.HoverLayerEnabled &&
            (SongInfoStackPanel.IsMouseOver || HoverRevealHost.IsMouseOver))
        {
            _hoverOpenTimer.Stop();
            _hoverOpenTimer.Start();
        }
    }

    private bool CanUseTaskbarComponentHover() =>
        !_isPlacementMotionActive && !_isHostVisibilitySuspended && !_restTransitionActive &&
        _backgroundPruneLevel < MemoryPruneLevel.DisplayOff &&
        _isConnected && IsBarPresentation;

    private bool CanUseTaskbarRestHover(Border surface)
    {
        if (_isPlacementMotionActive || !IsBarPresentation || _restTransitionActive)
            return false;

        if (_isConnected)
            return true;

        if (ReferenceEquals(surface, SongImageHoverOverlay))
            return SongImageBorder.Visibility == Visibility.Visible;

        return surface.Visibility == Visibility.Visible &&
            (ReferenceEquals(surface, TaskbarSpectrumHoverSurface) ||
             ReferenceEquals(surface, TaskbarPerformanceHoverSurface) ||
             ReferenceEquals(surface, TaskbarOutputDeviceHoverSurface) ||
             ReferenceEquals(surface, TaskbarVolumeHoverSurface));
    }


    /// <summary>直接模糊并淡化原文字区域，悬停按钮作为独立兄弟元素保持清晰。</summary>
    private void AnimateSongInfoCovered(bool isCovered, bool immediate = false)
    {
        if (isCovered && !CanUseTaskbarComponentHover())
            return;

        var motion = CurrentMotion;
        BlurEffect? blur = null;
        var needsBlur = motion.UseDecorativeEffects &&
                        (isCovered || SongInfoStackPanel.Effect is BlurEffect);
        if (needsBlur)
        {
            if (SongInfoStackPanel.Effect is not BlurEffect currentBlur || currentBlur.IsFrozen)
            {
                currentBlur = new BlurEffect
                {
                    Radius = 0,
                    KernelType = KernelType.Gaussian,
                    RenderingBias = RenderingBias.Performance
                };
                SongInfoStackPanel.Effect = currentBlur;
            }

            blur = currentBlur;
        }
        else if (SongInfoStackPanel.Effect is BlurEffect existingBlur)
        {
            // Effect 节点只要存在，文字就会一直被渲染到中间表面并失去字形保真度，
            // 因此不透明状态必须真正清空它，而不是留下一个半径为 0 的模糊。
            // While the effect node exists the text keeps rendering through an intermediate surface and loses glyph
            // fidelity, so the un-covered state must clear it instead of leaving a zero-radius blur behind.
            existingBlur.BeginAnimation(BlurEffect.RadiusProperty, null);
            SongInfoStackPanel.Effect = null;
        }

        var targetRadius = motion.UseDecorativeEffects && isCovered ? TaskbarCoveredBlurRadius : 0;
        var targetOpacity = isCovered ? TaskbarCoveredOpacity : 1;
        if (immediate || !motion.UseTransitions)
        {
            blur?.BeginAnimation(BlurEffect.RadiusProperty, null);
            SongInfoStackPanel.BeginAnimation(OpacityProperty, null);
            if (targetRadius > 0 && blur is not null)
                blur.Radius = targetRadius;
            else
                SongInfoStackPanel.Effect = null;
            SongInfoStackPanel.Opacity = targetOpacity;
            return;
        }

        var duration = isCovered ? motion.StandardDuration : motion.FastDuration;
        var easingMode = isCovered ? EasingMode.EaseOut : EasingMode.EaseInOut;
        if (blur is not null)
        {
            var radiusAnimation = new DoubleAnimation
            {
                To = targetRadius,
                Duration = duration,
                EasingFunction = easingMode == EasingMode.EaseOut ? CreateEaseOut() : CreateEaseInOut()
            };
            if (targetRadius <= 0)
            {
                // 恢复动画结束后立即移除 Effect 节点，让文字回到直接合成的清晰状态。
                // Remove the effect node when the un-cover animation ends so the text composites directly again.
                radiusAnimation.Completed += (_, _) =>
                {
                    // 快速重入时新的覆盖状态可能已经换上另一个模糊，只在仍由本次恢复持有该节点时才清空。
                    // A quick re-entry can already have installed another blur, so clear the node only while this
                    // un-cover still owns it.
                    if (!ReferenceEquals(SongInfoStackPanel.Effect, blur))
                        return;

                    blur.BeginAnimation(BlurEffect.RadiusProperty, null);
                    SongInfoStackPanel.Effect = null;
                };
            }

            blur.BeginAnimation(BlurEffect.RadiusProperty, radiusAnimation, HandoffBehavior.SnapshotAndReplace);
        }
        SongInfoStackPanel.BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            To = targetOpacity,
            Duration = duration,
            EasingFunction = easingMode == EasingMode.EaseOut ? CreateEaseOut() : CreateEaseInOut()
        }, HandoffBehavior.SnapshotAndReplace);
    }


    /// <summary>复用原 TaskBarMediaControl 的 hover 色彩和节奏，但将效果限制在单个组件。</summary>
    private void AnimateComponentHover(Border surface, bool isHovered, bool immediate = false, TimeSpan? colorDuration = null)
    {
        if (isHovered && !CanUseTaskbarRestHover(surface))
            return;

        var backgroundColor = isHovered
            ? _taskbarHoverPalette.Surface
            : Colors.Transparent;
        var backgroundOpacity = isHovered ? _taskbarHoverPalette.SurfaceOpacity : 0;
        var borderColor = isHovered ? _taskbarHoverPalette.Border : Colors.Transparent;
        var borderOpacity = isHovered ? _taskbarHoverPalette.BorderOpacity : 0;
        var easingMode = isHovered ? EasingMode.EaseOut : EasingMode.EaseInOut;

        if (surface.Background is not SolidColorBrush background || background.IsFrozen)
        {
            background = new SolidColorBrush(Colors.Transparent);
            surface.Background = background;
        }
        if (surface.BorderBrush is not SolidColorBrush border || border.IsFrozen)
        {
            border = new SolidColorBrush(Colors.Transparent);
            surface.BorderBrush = border;
        }

        var motion = CurrentMotion;
        var duration = colorDuration ?? (immediate || SystemParameters.HighContrast ? TimeSpan.Zero : motion.StandardDuration);
        TransitionColor(surface.Name + ".Background", new SolidColorBrush(backgroundColor), duration, background, immediate);
        TransitionColor(surface.Name + ".Border", new SolidColorBrush(borderColor), duration, border, immediate);
        if (immediate || !motion.UseTransitions)
        {
            background.BeginAnimation(SolidColorBrush.OpacityProperty, null);
            border.BeginAnimation(SolidColorBrush.OpacityProperty, null);
            background.Opacity = backgroundOpacity;
            border.Opacity = borderOpacity;
            return;
        }

        background.BeginAnimation(SolidColorBrush.OpacityProperty, new DoubleAnimation
        {
            To = backgroundOpacity,
            Duration = motion.StandardDuration,
            EasingFunction = easingMode == EasingMode.EaseOut ? CreateEaseOut() : CreateEaseInOut()
        });
        border.BeginAnimation(SolidColorBrush.OpacityProperty, new DoubleAnimation
        {
            To = borderOpacity,
            Duration = motion.StandardDuration,
            EasingFunction = easingMode == EasingMode.EaseOut ? CreateEaseOut() : CreateEaseInOut()
        });
    }

    /// <summary>文字区入场位移支的专用 TranslateTransform：由 <see cref="AnimateEntrance"/> 使用，与推挤支分离。
    /// / The text region's dedicated entrance-transform branch, used by <see cref="AnimateEntrance"/> and kept apart from the push branch.</summary>
    private TranslateTransform? _songInfoEntranceTransform;

    internal static Point ResolveArtworkTooltipPosition(Size tooltip, Rect artwork, Rect workArea, bool vertical, bool onLeft, double gap)
    {
        var above = new Point(artwork.Left + (artwork.Width - tooltip.Width) / 2, artwork.Top - tooltip.Height - gap);
        var below = new Point(above.X, artwork.Bottom + gap);
        var left = new Point(artwork.Left - tooltip.Width - gap, artwork.Top + (artwork.Height - tooltip.Height) / 2);
        var right = new Point(artwork.Right + gap, left.Y);
        var candidates = vertical ? (onLeft ? new[] { right, left, above, below } : new[] { left, right, above, below })
            : new[] { above, below, right, left };
        var best = candidates[0];
        var leastOverlap = double.PositiveInfinity;
        foreach (var candidate in candidates)
        {
            var point = new Point(Math.Clamp(candidate.X, workArea.Left, Math.Max(workArea.Left, workArea.Right - tooltip.Width)),
                Math.Clamp(candidate.Y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - tooltip.Height)));
            var intersection = Rect.Intersect(new Rect(point, tooltip), artwork);
            var overlap = intersection.IsEmpty ? 0 : intersection.Width * intersection.Height;
            if (overlap < leastOverlap) { leastOverlap = overlap; best = point; }
            if (overlap == 0) break;
        }
        return best;
    }

    /// <summary>
    /// 依据<b>当前真实指针位置</b>重同步原地放大的悬停状态，并修复可能的滞留状态。宿主在每次左键抬起后与
    /// 陈旧捕获看门狗清理后调用。修复层：按下（哪怕只是没拖动的尝试）期间 WPF 会把鼠标捕获交给按下的元素、
    /// 弹层或拖动逻辑，捕获存续时命中测试被重定向；三条滞留路径都会让封面"失聪"——①封面滞留在已关闭的 Popup
    /// 宿主里（不在主树，命中永远够不到），②Popup 开着但 <c>IsMouseOver</c> 被捕获谎报，③捕获挂在已卸载的
    /// 弹层树上成幽灵（tooltip 关闭竞态），把整个命中测试锁进一棵死树。①由本方法强制收回，③由宿主看门狗释放，
    /// 随后本方法延迟一拍（Background）用 <see cref="IsPointerOverArtwork"/>（屏幕几何，不受捕获影响的物理位置）
    /// 重新长开或收拢。
    /// Re-syncs the in-place zoom's hover state from the <b>actual pointer position</b> and repairs any stranded state. The host
    /// calls it after every left release and after the stale-capture watchdog's cleanups. Repair layer: during a press — even a
    /// drag attempt that never moves — WPF hands the mouse capture to the pressed element, a popup, or the drag logic,
    /// redirecting hit-testing while it lasts; three stranding paths can deafen the cover — ① the artwork stranded inside a
    /// closed Popup host (outside the main tree, forever out of reach), ② the Popup open while <c>IsMouseOver</c> lies under
    /// the capture, and ③ a capture stranded as a ghost on an unloaded popup tree (the tooltip-close race) locking all
    /// hit-testing inside a dead tree. ① is force-reclaimed here, ③ is released by the host's watchdog, and afterwards this
    /// method grows or collapses one tick later (Background) off <see cref="IsPointerOverArtwork"/> (screen geometry — the
    /// capture-immune physical position).
    /// </summary>
    public void ResyncArtworkHoverFromPointer()
    {
        // 修复层：Popup 已关但封面还困在宿主里——此时元素不在主树，鼠标事件永远送不到，只能在这里强制送回。
        // Repair layer: the Popup is closed yet the artwork still sits in the host — the element is outside the main tree and
        // no mouse event can ever reach it; only a forced reclaim here can return it.
        if (_artworkHover._artworkHoverZoomHost is { } host && ReferenceEquals(SongImageBorder.Parent, host)
            && _artworkHover._artworkHoverZoomPopup?.IsOpen != true)
            _artworkHover.ReclaimArtworkZoom();

        if (_artworkHover._artworkHoverZoomPopup?.IsOpen != true)
            return;

        var generation = _artworkHover._artworkZoomGeneration;
        Dispatcher.BeginInvoke(() =>
        {
            if (generation == _artworkHover._artworkZoomGeneration && IsLoaded && CanUseTaskbarComponentHover() &&
                _artworkHover._artworkHoverZoomPopup?.IsOpen == true)
                _artworkHover.AnimateArtworkHover(_artworkHover.IsPointerOverArtwork());
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    // 待定拖动捕获 Popup 子树，按下和抬起仍落在真实封面；确认拖动后才交还主宿主。
    internal bool CaptureArtworkPointer(DependencyObject? source) => IsArtworkZoomActive &&
        _artworkHover._artworkHoverZoomHost is { } host && source is Visual visual &&
        (ReferenceEquals(host, visual) || host.IsAncestorOf(visual)) && Mouse.Capture(host, CaptureMode.SubTree);

    internal void ReleaseArtworkPointerCapture()
    {
        if (_artworkHover._artworkHoverZoomHost is { } host && ReferenceEquals(Mouse.Captured, host))
            Mouse.Capture(null);
    }

    /// <summary>宿主确认拖动后同步收回封面并清除推挤，避免拖动与收回动画交叠。</summary>
    public void CollapseArtworkZoomForDrag()
    {
        if (!IsArtworkZoomActive)
            return;

        // 复用 Unloaded 那条"不等一帧"的收回：缩放归位 + 立即拆 Popup（见 HideArtworkHoverZoom）。
        // Reuse the "cannot wait a frame" reclaim the Unloaded path uses: reset the scale and tear the Popup down at once
        // (see HideArtworkHoverZoom).
        _artworkHover.HideArtworkHoverZoom(immediate: true);
    }

    private void RefreshTaskbarHoverAppearance(bool animateColor = false)
    {
        // 封面的悬停态用 IsPointerOverArtwork（屏幕几何，捕获免疫）判定，理由同 RefreshArtworkHoverPresentation：
        // 唯有封面会被换树进 Popup，其余组件住在主树里，IsMouseOver 对它们始终诚实。
        // The artwork's hover state comes from IsPointerOverArtwork (screen geometry, capture-immune), same reasoning as in
        // RefreshArtworkHoverPresentation: only the artwork ever re-parents into its Popup, while the other widgets live in the
        // main tree where IsMouseOver stays honest.
        var duration = ColorTransitionBrush.ResolveDuration(animateColor);
        AnimateComponentHover(SongImageHoverOverlay, _artworkHover.IsPointerOverArtwork(), immediate: !animateColor, colorDuration: duration);
        _artworkHover.RefreshArtworkHoverPresentation();
        AnimateComponentHover(SongInfoHoverOverlay, IsTextSurfaceHovered, immediate: !animateColor, colorDuration: duration);
        AnimateComponentHover(TaskbarSpectrumHoverSurface, TaskbarSpectrumHoverSurface.IsMouseOver, immediate: !animateColor, colorDuration: duration);
        AnimateComponentHover(TaskbarPerformanceHoverSurface, TaskbarPerformanceHoverSurface.IsMouseOver, immediate: !animateColor, colorDuration: duration);
        AnimateComponentHover(TaskbarInputDeviceHoverSurface, TaskbarInputDeviceHoverSurface.IsMouseOver, immediate: !animateColor, colorDuration: duration);
        AnimateComponentHover(TaskbarOutputDeviceHoverSurface, TaskbarOutputDeviceHoverSurface.IsMouseOver, immediate: !animateColor, colorDuration: duration);
        AnimateComponentHover(TaskbarVolumeHoverSurface, TaskbarVolumeHoverSurface.IsMouseOver, immediate: !animateColor, colorDuration: duration);
        AnimateComponentHover(TaskbarBatterySurface, TaskbarBatterySurface.IsMouseOver, immediate: !animateColor, colorDuration: duration);
    }

    private void SongImageBorder_MouseEnter(object sender, MouseEventArgs e)
    {
        if (e.StylusDevice is null) QuickLaunchHoverChanged?.Invoke(true);
        if (!_isConnected && _quickLaunchTarget is { } target) SetQuickLaunchPreview(target);
        AnimateComponentHover(SongImageHoverOverlay, true);
        // 进事件也以原格几何为准：指针可以从侧面落进"放大后的轮廓、但已出原格"的那一圈，此时不该长开——
        // 长开会立刻被悬停区轮询收回，两者拉成永不停的收/放振荡。1× 静止态下指针刚进入元素，判定必然为真，
        // 常规悬停不受影响。
        // The enter event keys off the original-slot geometry too: the pointer can drop in from the side into the ring
        // "inside the enlarged outline but outside the original slot", where a grow would be collapsed again by the very next
        // hover-zone tick — the two would drag each other into an endless grow/collapse oscillation. At rest (1×) the pointer
        // has just entered the element, so the verdict is true and ordinary hovering is unaffected.
        _artworkHover.AnimateArtworkHover(_artworkHover.IsPointerOverArtwork());
    }

    private void SongImageBorder_MouseLeave(object sender, MouseEventArgs e)
    {
        QuickLaunchHoverChanged?.Invoke(false);
        if (_notePressed) CancelQuickLaunchInteraction();
        _quickLaunchTooltip.IsOpen = false;
        AnimateComponentHover(SongImageHoverOverlay, false);
        if (_artworkHover._artworkHoverZoomPopup?.IsOpen == true)
        {
            // 原地放大期间不立即收起：换树本身会丢一次命中（见 VerifyArtworkZoomLeave），延迟判定后由它收口。
            // Do not collapse at once during the in-place zoom: the re-parent itself drops the hit (see VerifyArtworkZoomLeave),
            // which settles the decision after the delay.
            _artworkHover.VerifyArtworkZoomLeave();
        }
        else
        {
            _artworkHover.AnimateArtworkHover(false);
        }

        // 快速启动提示是手动打开的（滚轮预览需要它在指针不动时也出现），因此离开音符时要显式收起，
        // 否则它会留在屏幕上直到下一次交互。封面的滚轮提示同理：组合键按住期间它会一直被重申，
        // 指针离开封面后没有新的鼠标事件能让它自己关掉。
        // The quick-launch tooltip is opened by hand, because a wheel preview has to appear while the pointer is stationary, so it is closed
        // explicitly when the pointer leaves; otherwise it would stay on screen until the next interaction. The artwork's wheel tooltip is
        // the same: the chord keeps re-asserting it, and once the pointer leaves the artwork no mouse event would close it.
        _quickLaunchTooltip.IsOpen = false;
        _artworkWheelTooltip.IsOpen = false;
    }

    // 静置层的四个小组件（频谱、性能、输出设备、音量）共用这两个处理器，因此高亮必须落在事件源上：
    // 写死频谱表面会让悬停设备按钮时亮起的是频谱。
    // The rest layer's four widgets (spectrum, performance, output device, volume) share these two handlers, so the highlight has to land
    // on the event source: naming the spectrum surface outright would light up the spectrum while the device button is hovered.
    private void TaskbarSpectrumHoverSurface_MouseEnter(object sender, MouseEventArgs e) =>
        AnimateComponentHover((Border)sender, true);

    private void TaskbarSpectrumHoverSurface_MouseLeave(object sender, MouseEventArgs e) =>
        AnimateComponentHover((Border)sender, false);

    private void TaskbarPerformanceHoverSurface_MouseEnter(object sender, MouseEventArgs e) =>
        AnimateComponentHover(TaskbarPerformanceHoverSurface, true);

    private void TaskbarPerformanceHoverSurface_MouseLeave(object sender, MouseEventArgs e) =>
        AnimateComponentHover(TaskbarPerformanceHoverSurface, false);

    private void SongInfoStackPanel_MouseEnter(object sender, MouseEventArgs e)
    {
        if (!CanUseTaskbarComponentHover())
            return;

        // 控件遮住文字后暂停滚动，收起后从原位继续。
        AnimateComponentHover(SongInfoHoverOverlay, true);
        _hoverCloseTimer.Stop();
        _hoverOpenTimer.Stop();
        if (PresentationSettings.TaskbarExperience.HoverLayerEnabled)
            _hoverOpenTimer.Start();
    }

    private void SongInfoStackPanel_MouseLeave(object sender, MouseEventArgs e)
    {
        _hoverOpenTimer.Stop();
        ApplyMarqueeLayout(Math.Max(0, SongInfoStackPanel.Width));
        if (!_isTaskbarHoverVisible)
        {
            AnimateComponentHover(SongInfoHoverOverlay, false);
            return;
        }
        _hoverCloseTimer.Stop();
        _hoverCloseTimer.Start();
    }

    private void TaskbarHoverLayer_MouseEnter(object sender, MouseEventArgs e)
    {
        _hoverCloseTimer.Stop();
        AnimateComponentHover(SongInfoHoverOverlay, true);
    }

    private void TaskbarHoverLayer_MouseLeave(object sender, MouseEventArgs e)
    {
        _hoverCloseTimer.Stop();
        _hoverCloseTimer.Start();
    }



    private int _hoverRevealGeneration;
    private int _hoverHideFallbackGeneration;
    private long _hoverHideFallbackDeadline;

    private void ShowTaskbarHoverLayer()
    {
        // 宿主正在跟随任务栏动画时不开悬停层：展开会给整块文字区装上 BlurEffect 并播一段裁剪动画，
        // 这两样都与 Shell 的任务栏动画抢同一条合成管线，正是"触边收起时卡顿"里最贵的那一笔。
        // The hover layer is not opened while the host is following the taskbar animation: revealing it installs a BlurEffect on the whole text area and
        // plays a clip animation, and both compete for the same compositing pipeline as the Shell's taskbar animation — the most expensive part of the
        // jank seen when the taskbar hides right after a hover.
        if (!CanUseTaskbarComponentHover() || _isTaskbarHoverVisible)
            return;

        if (!_isConnected || !IsBarPresentation ||
            !PresentationSettings.TaskbarExperience.HoverLayerEnabled ||
            (!SongInfoStackPanel.IsMouseOver && !HoverRevealHost.IsMouseOver && !TaskbarHoverLayer.IsMouseOver))
            return;

        // 展开只需要"跑马灯按当前文字区宽度重新判定一次"：媒体栏几何每次快照都会重算（悬停层宽度、文字宽度、各处 Margin 都在那里写入），
        // 因此这里 MUST NOT 再跑一遍完整的体验设置——那会连带重建频谱、重排整条几何并走一次尺寸指纹，正好落在指针离开任务栏、
        // Shell 准备开始收起动画的那一刻，而这一刻宿主的冻结请求还排在 UI 线程的队列里。
        // The reveal only needs the marquee re-evaluated against the current text width: the bar's geometry is recomputed on every snapshot (the hover
        // layer's width, the text width, and every margin are written there), so this MUST NOT run a full experience pass — that would rebuild the
        // spectrum, re-lay out the whole bar, and run the size fingerprint exactly at the moment the pointer leaves the taskbar and the Shell is about to
        // start its hide animation, when the host's freeze request is still queued behind that work on the UI thread.
        ApplyMarqueeLayout(Math.Max(0, SongInfoStackPanel.Width));
        _isTaskbarHoverVisible = true;
        _hoverHideFallbackTimer.Stop();
        AnimateComponentHover(SongInfoHoverOverlay, true);
        AnimateSongInfoCovered(true);
        HoverRevealHost.Visibility = Visibility.Visible;
        HoverRevealHost.IsHitTestVisible = true;
        var targetWidth = Math.Max(0, SongInfoStackPanel.Width);
        HoverRevealHost.Width = targetWidth;
        ++_hoverRevealGeneration;
        var currentWidth = Math.Clamp(HoverRevealClip.Rect.Width, 0, targetWidth);
        HoverRevealClip.BeginAnimation(RectangleGeometry.RectProperty, null);
        HoverRevealClip.Rect = TaskbarRevealRect(currentWidth);
        if (!CurrentMotion.UseTransitions)
        {
            HoverRevealClip.Rect = TaskbarRevealRect(targetWidth);
            return;
        }

        var reveal = new RectAnimation
        {
            From = TaskbarRevealRect(currentWidth),
            To = TaskbarRevealRect(targetWidth),
            Duration = CurrentMotion.PanelDuration,
            EasingFunction = CreateEaseOut()
        };
        HoverRevealClip.BeginAnimation(RectangleGeometry.RectProperty, reveal, HandoffBehavior.SnapshotAndReplace);
    }

    // Keep the reveal fraction when the text area changes size during an open or close transition.
    private void RetargetHoverRevealWidth(double targetWidth)
    {
        var generation = ++_hoverRevealGeneration;
        var oldWidth = Math.Max(0, HoverRevealHost.Width);
        var fraction = oldWidth > 0
            ? Math.Clamp(HoverRevealClip.Rect.Width / oldWidth, 0, 1)
            : (_isTaskbarHoverVisible ? 1 : 0);
        HoverRevealClip.BeginAnimation(RectangleGeometry.RectProperty, null);
        HoverRevealHost.Width = targetWidth;
        var currentWidth = targetWidth * fraction;
        HoverRevealClip.Rect = TaskbarRevealRect(currentWidth);
        var destinationWidth = _isTaskbarHoverVisible ? targetWidth : 0;
        if (!CurrentMotion.UseTransitions || Math.Abs(destinationWidth - currentWidth) < 0.5)
        {
            HoverRevealClip.Rect = TaskbarRevealRect(destinationWidth);
            if (!_isTaskbarHoverVisible)
                FinishTaskbarHoverLayerHide();
            return;
        }

        var duration = _isTaskbarHoverVisible ? CurrentMotion.PanelDuration : CurrentMotion.ExitDuration;
        var remaining = _isTaskbarHoverVisible ? 1 - fraction : fraction;
        var animation = new RectAnimation
        {
            From = HoverRevealClip.Rect,
            To = TaskbarRevealRect(destinationWidth),
            Duration = TimeSpan.FromMilliseconds(Math.Max(1, duration.TotalMilliseconds * remaining)),
            EasingFunction = _isTaskbarHoverVisible ? CreateEaseOut() : CreateEaseInOut()
        };
        if (!_isTaskbarHoverVisible)
        {
            animation.Completed += (_, _) => CompleteTaskbarHoverLayerHide(generation);
            ArmHoverHideFallback(animation.Duration.TimeSpan, generation);
        }
        HoverRevealClip.BeginAnimation(RectangleGeometry.RectProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void HideTaskbarHoverLayer(bool immediate = false)
    {
        if (!immediate && !_isTaskbarHoverVisible && HoverRevealHost.Visibility == Visibility.Visible && CurrentMotion.UseTransitions)
            return;
        var generation = ++_hoverRevealGeneration;
        _hoverOpenTimer.Stop();
        _hoverCloseTimer.Stop();
        _hoverHideFallbackTimer.Stop();
        var keepRegularHover = CanUseTaskbarComponentHover() &&
                               SongInfoStackPanel.IsMouseOver;
        if (immediate || HoverRevealHost.Visibility != Visibility.Visible)
        {
            FinishTaskbarHoverLayerHide(immediate);
            AnimateSongInfoCovered(false, immediate: true);
            AnimateComponentHover(SongInfoHoverOverlay, keepRegularHover, immediate);
            return;
        }

        AnimateSongInfoCovered(false);
        // 逻辑状态立刻落地：悬停层从这一刻起就是"已关闭"，不依赖动画回调。
        // The logical state lands right away: the layer counts as closed from this moment on, without depending on an animation callback.
        _isTaskbarHoverVisible = false;

        if (!CurrentMotion.UseTransitions)
        {
            FinishTaskbarHoverLayerHide();
            return;
        }

        var hide = new RectAnimation
        {
            From = HoverRevealClip.Rect,
            To = TaskbarRevealRect(0),
            Duration = CurrentMotion.ExitDuration,
            EasingFunction = CreateEaseInOut()
        };
        hide.Completed += (_, _) => CompleteTaskbarHoverLayerHide(generation);
        HoverRevealClip.BeginAnimation(RectangleGeometry.RectProperty, hide, HandoffBehavior.SnapshotAndReplace);

        // 兜底：动画回调可能永远不来——渲染时钟停走（任务栏自动隐藏、窗口被遮挡）或这次动画被下一次动画顶掉时，
        // 收起动作会卡在半途、悬停层留在屏幕上。计时器只依赖 Dispatcher，因此一定会把状态收干净。
        // Fallback: the animation callback may never arrive — when the render clock stops (auto-hidden taskbar, occluded window) or the
        // animation is replaced by the next one, the collapse freezes halfway and the hover layer stays on screen. The timer only depends
        // on the dispatcher, so the state is always cleaned up.
        ArmHoverHideFallback(CurrentMotion.ExitDuration, generation);
    }

    private void ArmHoverHideFallback(TimeSpan duration, int generation)
    {
        var timeout = duration + HoverHideFallbackMargin;
        _hoverHideFallbackGeneration = generation;
        _hoverHideFallbackDeadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        _hoverHideFallbackTimer.Stop();
        _hoverHideFallbackTimer.Interval = timeout;
        _hoverHideFallbackTimer.Start();
    }

    private void CompleteTaskbarHoverLayerHide(int generation)
    {
        if (generation == _hoverRevealGeneration && !_isTaskbarHoverVisible)
            FinishTaskbarHoverLayerHide();
    }

    /// <summary>
    /// 把悬停层收干净：裁剪归零、宽度归零、隐藏并取消命中测试。可以重复调用（动画回调与兜底计时器都会走到这里）。
    /// Finishes hiding the hover layer: zeroes the clip and the width, hides it, and drops hit testing. It is safe to call repeatedly,
    /// since both the animation callback and the fallback timer land here.
    /// </summary>
    private void FinishTaskbarHoverLayerHide(bool immediate = false)
    {
        ++_hoverRevealGeneration;
        _hoverHideFallbackTimer.Stop();
        HoverRevealClip.BeginAnimation(RectangleGeometry.RectProperty, null);
        HoverRevealHost.Width = 0;
        HoverRevealClip.Rect = TaskbarRevealRect(0);
        HoverRevealHost.Visibility = Visibility.Collapsed;
        HoverRevealHost.IsHitTestVisible = false;
        ApplyMarqueeLayout(Math.Max(0, SongInfoStackPanel.Width));
        _isTaskbarHoverVisible = false;
        if (!SongInfoStackPanel.IsMouseOver)
        {
            AnimateComponentHover(SongInfoHoverOverlay, false, immediate);
        }
    }

    /// <summary>
    /// 立即结束所有任务栏指针反馈，不留任何 Blur、裁剪、颜色动画或手动打开的提示窗继续跑。
    /// Immediately settles every taskbar pointer visual, leaving no blur, clip, color animation, or manually opened tooltip running.
    /// </summary>
    private void SettleTaskbarPointerVisuals()
    {
        _hoverOpenTimer.Stop();
        _hoverCloseTimer.Stop();
        _hoverHideFallbackTimer.Stop();
        _wheelTooltipTimer.Stop();
        CloseWheelTooltips();
        _quickLaunchTooltip.IsOpen = false;
        HideTaskbarHoverLayer(immediate: true);
        AnimateSongInfoCovered(false, immediate: true);
        AnimateComponentHover(SongImageHoverOverlay, false, immediate: true);
        AnimateComponentHover(SongInfoHoverOverlay, false, immediate: true);
        AnimateComponentHover(TaskbarSpectrumHoverSurface, false, immediate: true);
        AnimateComponentHover(TaskbarPerformanceHoverSurface, false, immediate: true);
        AnimateComponentHover(TaskbarOutputDeviceHoverSurface, false, immediate: true);
        AnimateComponentHover(TaskbarVolumeHoverSurface, false, immediate: true);
    }

    /// <summary>
    /// 指针离开整条媒体栏：悬停层如果还开着（逻辑上已关闭、正在播收起动画，或收起被卡住）就直接收起。
    /// 这条路径让"移出程序"不依赖文字区/悬停层各自的通知是否齐全。
    /// The pointer left the whole media bar: when the hover layer is still up — logically closed, mid exit animation, or stuck — it is
    /// collapsed. This path keeps "left the app" from depending on the notifications of the text region and the hover host being complete.
    /// </summary>
    private void InteractionSurface_MouseLeave(object sender, MouseEventArgs e)
    {
        // 这个通知发生在 Shell 因指针离开任务栏而开始自动收起之前，是唯一个能在动画前清掉文字区 Blur 与裁剪动画的时机。
        // 等任务栏矩形开始变化后再清理已经太晚，WPF 与 Shell 会同时提交合成帧，这正是“交互后离开才偶发留角”的条件。
        // This notification arrives before the Shell starts auto-hiding after the pointer leaves the taskbar, making it the only point where the text
        // blur and clip animation can be cleared ahead of that motion. Waiting for the taskbar rectangle to change is too late: WPF and the Shell then
        // submit composition frames together, which is precisely why the sliver occurs only after interacting with and leaving the bar.
        SettleTaskbarPointerVisuals();
    }

}
