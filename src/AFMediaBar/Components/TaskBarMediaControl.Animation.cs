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
        var enabled = IsLoaded && _currentMode == WindowMode.Taskbar && !_isVertical &&
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

    private static TextAlignment ResolveConfiguredAlignment(TextBlock element) =>
        SettingsManager.Current.TaskbarExperience.Normalize().MediaTextAlignment switch
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
            var motion = CurrentMotion;
            if (!motion.UseTransitions)
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
                if (_songInfoPushTransform is not null && !group.Children.Contains(_songInfoPushTransform))
                    group.Children.Add(_songInfoPushTransform);
                group.Children.Add(_songInfoEntranceTransform);
            }
        }
        else
        {
            var newGroup = new TransformGroup();
            if (_songInfoPushTransform is not null)
                newGroup.Children.Add(_songInfoPushTransform);
            newGroup.Children.Add(_songInfoEntranceTransform);
            SongInfoStackPanel.RenderTransform = newGroup;
        }
        return _songInfoEntranceTransform;
    }

    private bool CanUseTaskbarComponentHover() =>
        _isConnected && _currentMode == WindowMode.Taskbar && !_isVertical;

    private bool CanUseTaskbarRestHover(Border surface)
    {
        if (_currentMode != WindowMode.Taskbar || _isVertical || _restTransitionActive)
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

    private bool CanShowDirectFullPanelHandle()
    {
        var experience = SettingsManager.Current.TaskbarExperience;
        return CanUseTaskbarComponentHover() &&
               experience.FullPanelEntryVisible &&
               !experience.HoverLayerEnabled &&
               experience.FullLayerEnabled;
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

    private void AnimateDirectFullPanelHandle(bool isVisible, bool immediate = false)
    {
        var targetOpacity = isVisible && CanShowDirectFullPanelHandle() ? 1 : 0;
        var motion = CurrentMotion;
        if (immediate || !motion.UseTransitions)
        {
            TaskbarDirectFullPanelHandle.BeginAnimation(OpacityProperty, null);
            TaskbarDirectFullPanelHandle.Opacity = targetOpacity;
            return;
        }

        TaskbarDirectFullPanelHandle.BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            To = targetOpacity,
            Duration = motion.FastDuration,
            EasingFunction = new CubicEase
            {
                EasingMode = targetOpacity > 0 ? EasingMode.EaseOut : EasingMode.EaseInOut
            }
        }, HandoffBehavior.SnapshotAndReplace);
    }

    /// <summary>复用原 TaskBarMediaControl 的 hover 色彩和节奏，但将效果限制在单个组件。</summary>
    private void AnimateComponentHover(Border surface, bool isHovered, bool immediate = false)
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
        if (immediate || !motion.UseTransitions)
        {
            background.BeginAnimation(SolidColorBrush.ColorProperty, null);
            background.BeginAnimation(SolidColorBrush.OpacityProperty, null);
            border.BeginAnimation(SolidColorBrush.ColorProperty, null);
            border.BeginAnimation(SolidColorBrush.OpacityProperty, null);
            background.Color = backgroundColor;
            background.Opacity = backgroundOpacity;
            border.Color = borderColor;
            border.Opacity = borderOpacity;
            return;
        }

        var duration = motion.StandardDuration;
        background.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation
        {
            To = backgroundColor,
            Duration = duration,
            EasingFunction = easingMode == EasingMode.EaseOut ? CreateEaseOut() : CreateEaseInOut()
        });
        background.BeginAnimation(SolidColorBrush.OpacityProperty, new DoubleAnimation
        {
            To = backgroundOpacity,
            Duration = duration,
            EasingFunction = easingMode == EasingMode.EaseOut ? CreateEaseOut() : CreateEaseInOut()
        });
        border.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation
        {
            To = borderColor,
            Duration = duration,
            EasingFunction = easingMode == EasingMode.EaseOut ? CreateEaseOut() : CreateEaseInOut()
        });
        border.BeginAnimation(SolidColorBrush.OpacityProperty, new DoubleAnimation
        {
            To = borderOpacity,
            Duration = duration,
            EasingFunction = easingMode == EasingMode.EaseOut ? CreateEaseOut() : CreateEaseInOut()
        });
    }

    /// <summary>封面悬停放大倍率：横向任务栏里封面 40px、画布 44px，1.1 倍放大后恰好贴满画布高度而不被裁切。
    /// The artwork hover zoom factor: a 40 px cover in a 44 px canvas grows to exactly the canvas height at 1.1, so nothing gets clipped.</summary>
    private const double ArtworkHoverScaleFactor = 1.1;

    /// <summary>大图预览的高度（DIP）；宽度由共享封面比例策略确定。</summary>
    private const double ArtworkHoverPreviewHeightDip = 120;

    /// <summary>封面悬停提示是否处于激活状态（紧凑提示或大图预览任一）；快照更新据此在播放状态翻转时刷新符号（见 AnimateArtworkHover）。
    /// Whether the artwork hover hint is active (either the compact hint or the large preview); snapshot updates key off it to refresh the
    /// glyph when the playback state flips (see AnimateArtworkHover).</summary>
    private bool _artworkHoverHintActive;

    /// <summary>大图预览里的封面图像；源随快照刷新（见 RefreshArtworkHoverPreviewContent）。/ The cover image inside the large preview; its source follows snapshots (see RefreshArtworkHoverPreviewContent).</summary>
    private Image? _artworkHoverPreviewImage;

    /// <summary>大图预览里的半透明遮罩与播放/暂停符号宿主。/ The scrim plus play/pause glyph host inside the large preview.</summary>
    private Border? _artworkHoverPreviewScrim;

    /// <summary>大图预览符号。/ The glyph inside the large preview.</summary>
    private Wpf.Ui.Controls.SymbolIcon? _artworkHoverPreviewIcon;

    /// <summary>大图预览的缩放变换：原点在预览底部中央，进入动画从任务栏一侧向上生长。/ The preview's scale transform: origin at the bottom centre so the entrance grows away from the taskbar.</summary>
    private ScaleTransform? _artworkHoverPreviewScale;

    /// <summary>原地放大的倍率：封面以底部中心为锚放大到 2 倍，只向上生长、底部位置不动；放大量的一半由右侧内容
    /// 右移避让（见 BuildArtworkZoomPushTransforms），另一半从封面左侧的空隙里长出来。
    /// The in-place zoom factor: the artwork grows to 2× from its bottom-centre anchor, upward only, keeping its bottom edge put;
    /// half the growth is yielded to by the right-hand content sliding right (see BuildArtworkZoomPushTransforms) and the other
    /// half grows into the gap left of the cover.</summary>
    private const double ArtworkHoverZoomScaleFactor = 2.0;

    /// <summary>
    /// 原地放大悬停区的边缘容差（DIP）。悬停区以<b>封面在栏内的原尺寸那一格</b>为准（不是放大后的轮廓），四周再放开
    /// 这么一点：吸收屏幕几何判定与逐级像素舍入之间的亚像素抖动，也吸收落位微滑期间位移层带出的一两像素。
    /// Edge tolerance (DIP) of the in-place zoom's hover zone, which keys off the artwork's <b>original in-bar slot</b> (not the
    /// enlarged outline): it absorbs the sub-pixel jitter between the screen-geometry verdict and per-hop pixel rounding, and the
    /// pixel or two the settle micro-slide's shift layer adds to the measurement.
    /// </summary>
    private const double ArtworkHoverEdgeToleranceDip = 3.0;

    /// <summary>
    /// 开窗即校准能接受的差量上限（物理像素）。量到的差量超过它，说明 Popup 窗口此刻还没定位、读回的是窗口的
    /// 默认位置——此时放弃本次校准：宁可退回"第一次收回时再学"的老路，也不能把这种垃圾值喂进校准 E，
    /// 那会当场把封面挪飞。
    /// The largest gap the on-show calibration accepts (physical pixels). A bigger one means the Popup's window is not
    /// positioned yet and the reading is the window's default spot — the attempt is abandoned: better to fall back to
    /// learning at the first reclaim than to feed such garbage into the calibration, which would fling the cover off.
    /// </summary>
    private const double ArtworkZoomShowCalibrationMaxGapPx = 12.0;

    /// <summary>原地放大的宿主：封面这一格的真实元素（SongImageBorder 本体，不是克隆）悬停期间住在这里。/ The in-place zoom host: the artwork slot's real element (the SongImageBorder itself, not a clone) lives here while hovered.</summary>
    private System.Windows.Controls.Grid? _artworkHoverZoomHost;

    /// <summary>
    /// 替身封面：封面本体住进 Popup 期间，主树原格位置留着的一份同貌占位（同一把 <c>SongImage</c> 画刷，不参与命中）。
    /// Popup 的窗口增删发生在窗口管理器层，而主树表面更新要晚一拍——没有它，两个渲染目标交替处会露出一帧空档
    /// （"封面空/黑一帧"）；有了它，主窗口每一帧都自带封面，交替与关窗时机再无关系。送回主树时移除。
    /// The stand-in artwork: while the real cover lives in the Popup, a look-alike placeholder holds the original slot in the
    /// main tree (the same <c>SongImage</c> brush, never hit-testable). Adding or removing the Popup's window happens at the
    /// window-manager level while the main tree's surface updates a frame behind — without it the hand-off exposes a one-frame
    /// gap ("the cover goes blank/black for a frame"); with it every main-window frame carries a cover and the hand-off no longer
    /// depends on when the Popup closes. Removed on the return to the main tree.
    /// </summary>
    private System.Windows.Controls.Border? _artworkZoomSubstitute;

    /// <summary>封面对齐方式的存档：换进 Popup 宿主前存下，收回时原样恢复；主画布的定位走 Canvas 附加属性，不受换树影响。/ Saved artwork alignments, stashed before moving into the Popup host and restored on return; the main canvas positions via Canvas attached properties, unaffected by the re-parent.</summary>
    private HorizontalAlignment _artworkZoomRestoreHorizontalAlignment;
    private VerticalAlignment _artworkZoomRestoreVerticalAlignment;

    /// <summary>收拢进行中：置位后由收拢动画的 Completed 回调把元素收回主树；中途指针回来会先清掉它再重新长开。
    /// Collapse in progress: once set, the collapse animation's Completed callback reclaims the element into the main tree; a
    /// pointer returning mid-collapse clears it first and grows the cover back instead.</summary>
    private bool _artworkZoomCollapsing;

    /// <summary>
    /// 封面在宿主内的渲染偏移校准（物理像素）：收拢终态实测位置与目标位置之差的滚动估计，回填进宿主内位移。
    /// 只吸收「同一摆放点下封面渲染位置的系统性偏差」（如宿主内逐级舍入的 ~3px），绝不参与 Popup 的屏幕摆放——
    /// 二者彻底分离，往摆放里反馈会双重补偿（曾致 comp 每轮翻倍的指数发散）。反馈斜率为 1，不可能放大；
    /// 环境变化时差量反向出现，E 随之自动回落。学习时机有两处：开窗即刻（<see cref="CalibrateArtworkZoomOnShow"/>，
    /// 主路径，让本会话第一次放大就落在正确位置——原先只在第一次收回才学到，第一次放大带着 ~3px 偏差长出来，
    /// 由落位微滑补回，看起来就是"第一次动画往上挪 3~4px"）；收回落位测量（<see cref="CompleteArtworkZoomReclaim"/>，
    /// 兜底，开窗两次都量不准则由它接手）。
    /// Calibration of the cover's rendered in-host offset (physical pixels): a running estimate of the gap between the
    /// collapsed cover's measured spot and its target, folded into the in-host shift. It only absorbs the systematic bias of
    /// where the cover renders for a given host spot (e.g. ~3 px of per-hop rounding inside the host) and never feeds the
    /// Popup's screen placement — the two are strictly separated, and feedback into the placement double-compensates (it
    /// once doubled comp every round into an exponential runaway). The feedback has slope 1 and cannot amplify; when the
    /// environment changes the gap re-appears sign-flipped and E settles back by itself. It is learned in two places:
    /// the moment the Popup opens (<see cref="CalibrateArtworkZoomOnShow"/>, the primary path, so the session's first zoom
    /// lands correctly — it used to be learned only at the first reclaim, leaving the first zoom to grow out with the ~3 px
    /// bias and get patched by the settle slide, read as "the first animation nudges up 3-4 px"), and the reclaim's settle
    /// measurement (<see cref="CompleteArtworkZoomReclaim"/>, the fallback when both on-show attempts failed).
    /// </summary>
    private double _artworkZoomCoverOffsetCalibrationX;
    private double _artworkZoomCoverOffsetCalibrationY;

    /// <summary>落位微移动画的代号：每次放大开始与每次微滑启动都递增；Completed 只在代号仍匹配时才清零位移层。
    /// 实测上一轮微滑的 Completed 会在下一轮放大开始后仍然触发，把刚写入的宿主内位移清掉——没有守卫，位移会
    /// 静默丢失。/ A generation token for the settle micro-slide: bumped on every zoom start and slide start; a Completed
    /// callback zeroes the shift layer only while its token still matches. The previous slide's Completed was observed to
    /// fire even after the next zoom start, silently wiping the fresh in-host shift — hence the guard.</summary>
    private int _artworkZoomSettleGeneration;

    /// <summary>开窗校准的代号：每次激活递增。晚到一拍的那次补量只在代号仍匹配（期间没有新的激活）时才动手——
    /// 否则它会拿上一轮的基准点去校准这一轮的封面。
    /// A token for the on-show calibration, bumped on every activation. The retry one tick later acts only while its token
    /// still matches (no new activation in between) — otherwise it would calibrate this round's cover off the last
    /// round's baseline.</summary>
    private int _artworkZoomShowCalibrationGeneration;

    /// <summary>Custom 摆放回调要用的摆放点（物理像素，相对 PlacementTarget = MainCanvas 原点）。实测确认回调收发
    /// 均为物理像素（popupSize=120 = 80 DIP × 1.5），传 DIP 会被放大 1.5 倍；Custom 模式下 HorizontalOffset/
    /// VerticalOffset 失效，位置只能由回调给出。/ The placement point the Custom placement callback returns (physical
    /// pixels, relative to the PlacementTarget = MainCanvas origin). The callback exchanges device pixels (popupSize=120 =
    /// 80 DIP x 1.5), so passing DIP magnifies them 1.5x; HorizontalOffset/VerticalOffset are dead in Custom mode, so the
    /// position is stashed here for the callback to hand out.</summary>
    private double _artworkZoomPendingPlacementX;
    private double _artworkZoomPendingPlacementY;

    /// <summary>
    /// 送回主树时的落位时长：主树与 Popup 是两条像素舍入管道（主树逐级 UseLayoutRounding、Popup 是独立 HWND 各自
    /// 舍入），送回那一帧的位置天然差 1~2 物理像素。差值不做事后校正——封面先画在 Popup 终态的位置
    /// （<see cref="SongImageSettleTranslate"/>），再用这段时长的微移动画滑回原位，切换全程视觉连续。
    /// Settle duration for the return to the main tree: the main tree and the Popup are two pixel-rounding pipelines (the main
    /// tree aligns every layout hop via UseLayoutRounding, the Popup is an independent HWND rounding on its own), so the return
    /// frame naturally differs by 1-2 physical pixels. The gap is not patched afterwards — the cover is first drawn at the
    /// Popup's final spot (via <see cref="SongImageSettleTranslate"/>) and a micro-slide of this duration eases it onto the
    /// original spot, keeping the switch visually continuous throughout.</summary>
    private static readonly TimeSpan ArtworkZoomSettleDuration = TimeSpan.FromMilliseconds(80);

    /// <summary>
    /// 原地放大收拢的兜底计时器：Completed 可能因为渲染时钟停走（任务栏自动隐藏、窗口被遮挡）或动画被顶掉而永不到达
    /// ——悬停层的 <c>_hoverHideFallbackTimer</c> 治的就是同一种病。回调缺席时封面会滞留在开着的 Popup 里：
    /// IsArtworkZoomActive 恒真、封面又不在主树里，鼠标事件一件都送不到。计时器只依赖 Dispatcher，到点后若收拢仍未完成就强制收回。
    /// The fallback timer for the zoom's collapse: the Completed callback may never arrive when the render clock stops (an
    /// auto-hidden taskbar, an occluded window) or the animation gets replaced — the same disease the hover layer's
    /// <c>_hoverHideFallbackTimer</c> treats. Without the callback the artwork strands inside the open Popup: IsArtworkZoomActive
    /// stuck true with the cover outside the main tree, so not a single mouse event reaches it. The timer depends only on the
    /// dispatcher: on expiry, a collapse that never finished is force-reclaimed.
    /// </summary>
    private readonly System.Windows.Threading.DispatcherTimer _artworkZoomReclaimFallbackTimer;

    /// <summary>
    /// 原地放大期间的悬停区轮询（约 30 ms 一拍）。判定是屏幕几何而非元素的鼠标进出事件：放大后的轮廓把原格整个包住，
    /// 指针停在轮廓边缘时元素不发任何事件，事件驱动永远收不回去。只在放大存续期间运行，Popup 一关（收回完成）自动停表。
    /// The hover-zone poll while the in-place zoom lasts (~30 ms per tick). The verdict is screen geometry rather than the
    /// element's enter/leave events: the enlarged outline encloses the original slot whole, so a pointer at its edge raises no
    /// event and an event-driven machine could never collapse it. It runs only while the zoom lasts — closing the Popup (the
    /// reclaim) stops it.
    /// </summary>
    private readonly System.Windows.Threading.DispatcherTimer _artworkZoomHoverSyncTimer;

    /// <summary>本次放大的推挤量（DIP）：封面宽度增长的一半，右侧内容右移同样的距离以维持间距。/ This activation's push amount (DIP): half the artwork's width growth, matched by the right-hand content to keep the gap.</summary>
    private double _artworkZoomPushOffset;

    /// <summary>推挤变换清单：每次激活重建，收回时清零并丢弃。/ The push transforms, rebuilt per activation and zeroed then dropped on reclaim.</summary>
    private List<TranslateTransform>? _artworkZoomPushTransforms;

    /// <summary>文字区推挤支的专用 TranslateTransform：与入场位移支同处一个 TransformGroup，二者因此互不覆盖。
    /// 放大中切歌时入场动画只改自己的支，推挤位移得以保留（修复 BUG 2）。/ The text region's dedicated push-transform
    /// branch: it shares one TransformGroup with the entrance-transform branch, so the two never overwrite each other. A track
    /// change mid-zoom touches only the entrance branch, leaving the push intact (fixes BUG 2).</summary>
    private TranslateTransform? _songInfoPushTransform;

    /// <summary>文字区入场位移支的专用 TranslateTransform：由 <see cref="AnimateEntrance"/> 使用，与推挤支分离。
    /// / The text region's dedicated entrance-transform branch, used by <see cref="AnimateEntrance"/> and kept apart from the push branch.</summary>
    private TranslateTransform? _songInfoEntranceTransform;

    /// <summary>最近一次激活原地放大的时间戳（Stopwatch ticks），用于区分换树副作用导致的误发离开与真实离开。
    /// / Timestamp of the most recent in-place zoom activation (Stopwatch ticks), used to tell a re-parent's spurious leave from a real one.</summary>
    private long _artworkZoomShowStamp;

    /// <summary>换树（封面移入 Popup）后这段时间内到达的 MouseLeave 视为重挂窗口的副作用，直接忽略——真实离开发生得更晚。
    /// / A MouseLeave that arrives within this window after the re-parent is treated as the re-parent's artifact and ignored; a real leave comes later.</summary>
    private const double ArtworkZoomReparentGuardMs = 80.0;

    /// <summary>
    /// 构建封面大图预览的 Popup：主窗口只有 44 DIP 高且裁切内容，放大的封面必须由独立 Popup 才能画到任务栏上方。
    /// Popup 透明、不可命中，锚在封面上方；显隐由 <see cref="AnimateArtworkHover"/> 管理，内容随快照刷新。
    /// Builds the Popup for the large artwork preview: the host window is 44 DIP tall and clips its content, so the enlarged cover
    /// needs its own Popup to render above the taskbar. The Popup is transparent and non-hit-testable, anchored above the artwork;
    /// <see cref="AnimateArtworkHover"/> manages its visibility while the content follows snapshots.
    /// </summary>
    private System.Windows.Controls.Primitives.Popup BuildArtworkHoverPreviewPopup()
    {
        _artworkHoverPreviewImage = new Image { Stretch = Stretch.Uniform };
        _artworkHoverPreviewIcon = new Wpf.Ui.Controls.SymbolIcon
        {
            Symbol = Wpf.Ui.Controls.SymbolRegular.Pause24,
            Filled = true,
            FontSize = 44,
            Foreground = Brushes.White
        };
        _artworkHoverPreviewScrim = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)),
            Child = _artworkHoverPreviewIcon,
            IsHitTestVisible = false
        };
        var root = new Border
        {
            Width = ArtworkHoverPreviewHeightDip,
            Height = ArtworkHoverPreviewHeightDip,
            CornerRadius = new CornerRadius(10),
            ClipToBounds = true,
            Background = Brushes.Black,
            RenderTransformOrigin = new Point(0.5, 1),
            Effect = new DropShadowEffect
            {
                BlurRadius = 16,
                ShadowDepth = 3,
                Opacity = 0.4,
                RenderingBias = RenderingBias.Performance
            },
            Child = new Grid { Children = { _artworkHoverPreviewImage, _artworkHoverPreviewScrim } }
        };
        root.RenderTransform = _artworkHoverPreviewScale = new ScaleTransform(1, 1);
        return new System.Windows.Controls.Primitives.Popup
        {
            AllowsTransparency = true,
            IsHitTestVisible = false,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Top,
            PlacementTarget = SongImageBorder,
            StaysOpen = true,
            Child = root
        };
    }

    /// <summary>
    /// 构建原地放大的 Popup：它不画任何自己的内容，只是悬停期间收留封面这一格的<b>真实元素</b>——
    /// 任务栏宿主是任务栏的子窗口且被 SetWindowRgn 裁到媒体栏矩形，主树里的元素无论怎么放大都出不了任务栏；
    /// 把 SongImageBorder 本体移进这个透明 Popup、底部中心对齐原位，视觉上就是"封面自己长大了"，
    /// 点击与滚轮仍落在真实元素上。宿主可命中（与不可命中的大图预览相反），显隐由 <see cref="AnimateArtworkHover"/> 管理。
    /// Builds the Popup for the in-place zoom: it draws nothing of its own and merely hosts the artwork slot's <b>real element</b> while
    /// hovered — the taskbar host is a child of the taskbar clipped to the bar rect by SetWindowRgn, so no element in the main tree can
    /// ever grow beyond the taskbar. Moving SongImageBorder itself into this transparent Popup, bottom-centre aligned with its original
    /// spot, reads as "the cover itself grew"; clicks and the wheel still land on the real element. The host IS hit-testable (unlike the
    /// large preview) and <see cref="AnimateArtworkHover"/> manages its visibility.
    /// </summary>
    private System.Windows.Controls.Primitives.Popup BuildArtworkHoverZoomPopup()
    {
        _artworkHoverZoomHost = new System.Windows.Controls.Grid();
        // 主树（MediaControl）开着 UseLayoutRounding，逐级把布局对齐到物理像素；Popup 是独立 HWND，不继承这个设置，
        // 默认亚像素渲染——与主树两条舍入路径在非整数 DPI 下能差出 1~2 物理像素。宿主显式开启，让内部布局与
        // 主树同规则，剩余的差量再由送回时的落位微移动画吸收（见 CompleteArtworkZoomReclaim）。
        // The main tree (MediaControl) runs with UseLayoutRounding, aligning every layout hop to physical pixels; the Popup is an
        // independent HWND that does not inherit the setting and renders sub-pixel by default — two rounding pipelines that drift
        // 1-2 physical pixels apart at a fractional DPI. Enabling it on the host aligns the inner layout with the main tree's rule,
        // and whatever difference is left is absorbed by the settle micro-slide on the return (see CompleteArtworkZoomReclaim).
        _artworkHoverZoomHost.UseLayoutRounding = true;
        // 全局滚轮手势只挂在主交互面上；封面住进 Popup 期间把滚轮转发回主面，音量/选曲手势与滚轮提示不丢。
        // The global wheel gesture lives only on the main interaction surface; while the artwork stays in the Popup the wheel is
        // forwarded back so the volume/track gestures and the wheel tooltips survive.
        _artworkHoverZoomHost.PreviewMouseWheel += (_, e) => InteractionSurface_PreviewMouseWheel(InteractionSurface, e);
        return new System.Windows.Controls.Primitives.Popup
        {
            AllowsTransparency = true,
            // Custom 摆放：位置由回调给出（见 ArtworkZoomPlacementCallback），HorizontalOffset/VerticalOffset 在此模式
            // 下失效，点位经 _artworkZoomPendingPlacement 传递。WPF 对 Custom 摆放同样做屏幕夹持，故 Show 端先行
            // 预夹持并把差额转成宿主内位移（见 ShowArtworkHoverZoom）。
            // Custom placement: the callback supplies the spot (see ArtworkZoomPlacementCallback); HorizontalOffset and
            // VerticalOffset are ignored in this mode, so the position travels via _artworkZoomPendingPlacement. WPF clamps
            // Custom placements too, so Show pre-clamps itself and turns the cut-off amount into the in-host shift (see
            // ShowArtworkHoverZoom).
            Placement = System.Windows.Controls.Primitives.PlacementMode.Custom,
            CustomPopupPlacementCallback = ArtworkZoomPlacementCallback,
            // 锚定 MainCanvas 而不是控件整体：主树渲染封面走「MainCanvas + Canvas 坐标」这条视觉链，Popup 的摆放
            // 也必须锚在同一条链上，摆放换算才与主树渲染一致。
            // Anchor to the MainCanvas rather than the whole control: the main tree renders the cover along the
            // "MainCanvas + Canvas coordinates" visual chain, and the Popup's placement must ride that same chain so its
            // conversion matches the main tree's rendering.
            PlacementTarget = MainCanvas,
            StaysOpen = true,
            Child = _artworkHoverZoomHost
        };
    }

    /// <summary>
    /// Custom 摆放回调：把 <see cref="_artworkZoomPendingPlacementX/Y"/>（相对 MainCanvas 原点的物理像素点）原样交给
    /// WPF。只返回单一候选点——多候选时 WPF 会挑"可见面积最大"的一个，等于变相夹持；单候选即所点即所得。
    /// The Custom placement callback: hands <see cref="_artworkZoomPendingPlacementX/Y"/> (a physical-pixel point relative
    /// to the MainCanvas origin) to WPF verbatim. A single candidate is returned on purpose — with multiple candidates WPF
    /// picks the "most visible" one, which is clamping in disguise; one candidate means exactly what we ask for.
    /// </summary>
    private System.Windows.Controls.Primitives.CustomPopupPlacement[] ArtworkZoomPlacementCallback(
        Size popupSize, Size targetSize, Point offset)
    {
        return new[]
        {
            new System.Windows.Controls.Primitives.CustomPopupPlacement(
                new Point(_artworkZoomPendingPlacementX, _artworkZoomPendingPlacementY),
                System.Windows.Controls.Primitives.PopupPrimaryAxis.None)
        };
    }

    /// <summary>
    /// 激活原地放大：把封面元素移进 Popup 宿主，宿主大小即放大后的尺寸，封面以"底部+水平居中"落在宿主里、
    /// 缩放原点在底部中心——于是只往上长、底部位置不动。摆放以封面当前屏幕位置为基准（见方法内注释），
    /// Custom 摆放 + 预夹持 + 宿主内位移保证收拢终态与主树原位重合；开窗后即刻校准渲染偏移
    /// （<see cref="CalibrateArtworkZoomOnShow"/>），本会话第一次放大也从正确位置起步。已激活时是空操作：
    /// 内容就是真实元素，快照刷新（换歌、暂停翻转）自动生效。
    /// Activates the in-place zoom: the artwork element moves into the Popup host sized to its enlarged bounds, sitting bottom-centre
    /// with its scale origin at the bottom centre — so it grows only upward with its bottom edge unmoved. The placement
    /// baselines on the cover's current screen spot (see comments in the method body); Custom placement plus pre-clamping
    /// and the in-host shift make the collapsed final spot coincide with the main-tree one, and the on-show calibration
    /// (<see cref="CalibrateArtworkZoomOnShow"/>) lets even the session's first zoom start on the correct spot. A no-op while
    /// already active: the content is the real element, so snapshot refreshes (track change, pause flip) apply by themselves.
    /// </summary>
    private void ShowArtworkHoverZoom()
    {
        if (_artworkHoverZoomPopup is not { } popup || _artworkHoverZoomHost is not { } host)
            return;
        // 记下激活时刻：换树（封面移入 Popup）那一瞬 WPF 会先丢一次命中而误发 MouseLeave，以"show 之后多久"判断其
        // 是否为重挂窗口的副作用（见 VerifyArtworkZoomLeave），避免把它当成真实离开提前收回。
        // Stamp the activation moment: the re-parent (cover moved into the Popup) drops the hit for a moment and spuriously
        // raises MouseLeave; gating on how long since the show tells that artifact from a real leave (see VerifyArtworkZoomLeave).
        _artworkZoomShowStamp = Stopwatch.GetTimestamp();
        // 指针回来了（可能正处在收拢动画里）：清掉收拢标记，随后的动画把倍率重新拉起来，元素仍在宿主中无需换树。
        // 收拢兜底计时器一并停掉，免得它把正在重新长开的放大强行收回。
        // The pointer returned (possibly mid-collapse): clear the collapse flag, the following animation pulls the factor back
        // up, and the element is still in the host so no re-parent is needed. The collapse fallback timer stops too, so it never
        // force-reclaims a zoom busy regrowing.
        _artworkZoomCollapsing = false;
        _artworkZoomReclaimFallbackTimer.Stop();
        // Popup 开着即放大已在（收回在同一轮里就把 Popup 关掉，不开着不承载封面的窗口，见 CompleteArtworkZoomReclaim）。
        // An open Popup means the zoom is already up: the reclaim closes it within the same turn, so no Popup ever stays open
        // without hosting the cover (see CompleteArtworkZoomReclaim).
        if (popup.IsOpen)
            return;

        // 换树前先收掉打开中的封面 tooltip：tooltip 弹层内部为"点外部关闭"持有鼠标捕获，而换树会把它的触发
        // 元素（封面）移出主树——弹层关闭与换树交错时，那份捕获可能被遗留在一棵已卸载的树上成幽灵，把此后
        // 所有命中测试锁进死树，封面悬停彻底失聪。先收再换，竞态从源头消失。
        // Close an open artwork tooltip before the re-parent: the tooltip popup internally holds a mouse capture for its
        // "dismiss on outside click", and the re-parent moves its trigger element (the artwork) out of the main tree — when
        // the tooltip's closure interleaves with that, the capture can be stranded on an unloaded tree as a ghost, locking all
        // subsequent hit-testing inside a dead tree and killing the artwork's hover outright. Closing first removes the race
        // at its source.
        if (SongImageBorder.ToolTip is System.Windows.Controls.ToolTip { IsOpen: true } openTooltip)
            openTooltip.IsOpen = false;

        var width = SongImageBorder.ActualWidth;
        var height = SongImageBorder.ActualHeight;
        if (width <= 0 || height <= 0 || double.IsNaN(width) || double.IsNaN(height))
            return;

        var scaledWidth = width * ArtworkHoverZoomScaleFactor;
        var scaledHeight = height * ArtworkHoverZoomScaleFactor;
        host.Width = scaledWidth;
        host.Height = scaledHeight;
        _artworkZoomRestoreHorizontalAlignment = SongImageBorder.HorizontalAlignment;
        _artworkZoomRestoreVerticalAlignment = SongImageBorder.VerticalAlignment;
        SongImageBorder.HorizontalAlignment = HorizontalAlignment.Center;
        SongImageBorder.VerticalAlignment = VerticalAlignment.Bottom;
        // 底部中心为原点：缩放把封面往上、往左右对称地推，底边留在原地。
        // Bottom-centre origin: the scale pushes the cover upward and symmetrically sideways while the bottom edge stays put.
        SongImageBorder.RenderTransformOrigin = new Point(0.5, 1);

        // 生长必须从恰好 1 倍开始：上一次紧凑悬停可能留下 1.1 的残留，直接换树会让封面跳一下再长。
        // Growth must start from exactly 1×: a previous compact hover can leave 1.1 behind, and re-parenting with that residue
        // makes the cover jump before it grows.
        SongImageScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        SongImageScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        SongImageScale.ScaleX = 1;
        SongImageScale.ScaleY = 1;
        // 位移层一并清零（代号递增，上一轮微滑的 Completed 不得再清零本轮新位移）：清零必须先于 homeScreen 的
        // 采样——PointToScreen 会把渲染变换算进去，带着尾巴测出的基准是脏数据。
        // The shift layer zeroes too (generation bumped, so the previous slide's Completed cannot zero this round's fresh
        // shift), strictly before homeScreen is sampled — PointToScreen folds render transforms in, and a baseline taken
        // with a tail still on is dirty data.
        _artworkZoomSettleGeneration++;
        SongImageSettleTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        SongImageSettleTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        SongImageSettleTranslate.X = 0;
        SongImageSettleTranslate.Y = 0;

        // 封面当前的主树屏幕位置：宿主摆放与宿主内位移的共同基准。
        // The cover's current main-tree screen spot: the shared baseline for the host placement and the in-host shift.
        var homeScreen = SongImageBorder.PointToScreen(new Point(0, 0));

        // 推挤量 = 宽度增长的一半：右侧内容右移同样的距离，封面与它们的间距维持不变。
        // The push equals half the width growth: the right-hand content shifts by the same distance, keeping its gap to the cover.
        _artworkZoomPushOffset = width * (ArtworkHoverZoomScaleFactor - 1) / 2;
        BuildArtworkZoomPushTransforms();

        // 替身先就位、再抽走真封面：主画布这一格里永远有封面（见 _artworkZoomSubstitute）——送进 Popup 的那一帧
        // 与之后主窗口的每一帧都带着封面，送回的交替处同理，完全不必赌窗口增删与表面更新谁先到。
        // The stand-in takes its place before the real cover is pulled out: the main canvas slot always carries a cover (see
        // _artworkZoomSubstitute) — the hand-off frame and every main-window frame after it carry one, and the return's
        // hand-off works the same way, with no bet on whether the window add/remove or the surface update lands first.
        ShowArtworkZoomSubstitute();
        MainCanvas.Children.Remove(SongImageBorder);
        host.Children.Add(SongImageBorder);

        // 摆放点全程物理像素：实测确认 Custom 回调收发的都是物理像素（popupSize=120 = 80 DIP × 1.5），喂 DIP 会被
        // 放大 1.5 倍。摆放与宿主内位移是两条严格分离的补偿：摆放不做任何跨轮反馈（往里掺反馈曾致指数发散），
        // 位移的校准值（_artworkZoomCoverOffsetCalibrationX/Y，学习时机见字段说明）只吸收封面渲染偏移。
        // The placement point is physical pixels end to end: the Custom callback exchanges device pixels (popupSize=120 =
        // 80 DIP x 1.5), and feeding DIP magnifies them 1.5x. Placement and the in-host shift are two strictly separated
        // compensations: the placement takes no cross-round feedback (feedback there once caused an exponential runaway),
        // while the shift's calibration (_artworkZoomCoverOffsetCalibrationX/Y, learned where the field doc says) only
        // absorbs the cover's rendered offset.
        var dpiScale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var canvasOrigin = MainCanvas.PointToScreen(new Point(0, 0));
        var marginPx = (scaledWidth - width) / 2 * dpiScale;
        var topPx = (scaledHeight - height) * dpiScale;
        var screenLeft = SystemParameters.VirtualScreenLeft * dpiScale;
        var screenTop = SystemParameters.VirtualScreenTop * dpiScale;
        var screenRight = screenLeft + SystemParameters.VirtualScreenWidth * dpiScale;
        var screenBottom = screenTop + SystemParameters.VirtualScreenHeight * dpiScale;
        // 理想 host 左上 = 封面基准 − 宿主内边距，再按屏幕边界预夹持（WPF 对 Custom 摆放同样夹持，自己先夹才知道
        // 夹了多少）。宿主内位移直接以「收拢终态 = 封面基准」为目标：位移 = 基准 − 夹持后host − 内边距 − 校准 E，
        // 被屏幕裁掉的只有看不见的空边距，放大态封面依旧渲染在主树原位上。
        // Ideal host top-left = baseline minus paddings, pre-clamped to the screen bounds (WPF clamps Custom placements
        // too, so clamping first tells us how much will be cut). The in-host shift targets "collapsed final spot = baseline"
        // directly: shift = baseline − clamped host − paddings − calibration E; the screen crops only the invisible empty
        // margin, so the enlarged cover still renders on the main-tree spot.
        var idealHostX = homeScreen.X - marginPx;
        var idealHostY = homeScreen.Y - topPx;
        var clampedX = Math.Max(screenLeft, Math.Min(idealHostX, screenRight - scaledWidth * dpiScale));
        var clampedY = Math.Max(screenTop, Math.Min(idealHostY, screenBottom - scaledHeight * dpiScale));
        var shiftPxX = homeScreen.X - clampedX - marginPx - _artworkZoomCoverOffsetCalibrationX;
        var shiftPxY = homeScreen.Y - clampedY - topPx - _artworkZoomCoverOffsetCalibrationY;
        SongImageSettleTranslate.X = shiftPxX / dpiScale;
        SongImageSettleTranslate.Y = shiftPxY / dpiScale;
        _artworkZoomPendingPlacementX = clampedX - canvasOrigin.X;
        _artworkZoomPendingPlacementY = clampedY - canvasOrigin.Y;

        popup.IsOpen = true;
        // 开窗即刻校准封面渲染偏移：本会话的第一次放大若等到第一次收回才学，就会带着 3~4px 的未校准偏差长出来。
        // Calibrate the rendered offset the moment the Popup opens: a session's first zoom that waits for the first reclaim
        // would grow out carrying 3-4 px of un-calibrated bias.
        CalibrateArtworkZoomOnShow(homeScreen, dpiScale, host, popup);
        // 推挤出去的内容要穿过两层裁切才能完整可见：WPF 层 MainBorder 的 ClipToBounds 在此解除（收回时恢复），
        // HWND 层的 SetWindowRgn 区域由宿主经 ArtworkZoomExtensionChanged 右扩。只处理其中一层，组件仍会被吃。
        // The pushed content must clear two clipping layers to stay fully visible: the WPF layer — MainBorder's
        // ClipToBounds — is released here (and restored on reclaim), while the HWND layer's SetWindowRgn region is widened
        // by the host through ArtworkZoomExtensionChanged. Handling only one of them still eats the widgets.
        MainBorder.ClipToBounds = false;
        ArtworkZoomExtensionChanged?.Invoke(this, _artworkZoomPushOffset);
        // 悬停区轮询从这一刻起接管收/放判定（见 _artworkZoomHoverSyncTimer）：放大后的元素轮廓不再能被当作
        // 悬停区——它把原格整个包住，指针停在轮廓边缘时元素不发任何事件，只能靠几何轮询。
        // The hover-zone poll takes over the collapse/grow verdict from here (see _artworkZoomHoverSyncTimer): the enlarged
        // outline can no longer serve as the hover zone — it encloses the original slot whole, and a pointer at its edge
        // raises no event at all, which only a geometry poll can catch.
        _artworkZoomHoverSyncTimer.Start();
    }

    /// <summary>
    /// 收回原地放大。过渡可用时不立即拆掉：先在 Popup 里把封面平滑收拢回 1 倍、右侧内容同步回位（动画由紧随其后的
    /// <see cref="AnimateArtworkZoom"/> 播放并在 Completed 后调用 <see cref="ReclaimArtworkZoom"/>），收拢期间指针回来
    /// 还会重新长开；immediate（或过渡被关闭）时立即收回。Unloaded 等不可等待的路径必须传 immediate。
    /// Reclaims the in-place zoom. With transitions it does not tear down at once: the cover first shrinks smoothly back to 1×
    /// inside the Popup while the pushed content slides back (the animation is played by the <see cref="AnimateArtworkZoom"/> call
    /// right after, whose Completed invokes <see cref="ReclaimArtworkZoom"/>), and a pointer returning mid-collapse grows it back;
    /// immediate (or transitions off) reclaims at once. Paths that cannot wait — Unloaded among them — must pass immediate.
    /// </summary>
    private void HideArtworkHoverZoom(bool immediate = false)
    {
        if (_artworkHoverZoomPopup is not { } popup || _artworkHoverZoomHost is not { } host)
            return;
        if (popup.IsOpen)
        {
            if (immediate || !CurrentMotion.UseTransitions)
            {
                SongImageScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                SongImageScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                SongImageScale.ScaleX = 1;
                SongImageScale.ScaleY = 1;
                ReclaimArtworkZoom(immediate: true);
            }
            else
            {
                // 只做标记：收拢动画与收回由紧随其后的 AnimateArtworkZoom 接管。
                // Mark only: the collapse animation and the reclaim are owned by the AnimateArtworkZoom call right after.
                _artworkZoomCollapsing = true;
                // 兜底起表：Completed 若因渲染时钟停走/动画被顶掉而永不到达，到点强制收回（见字段说明）。
                // Start the fallback clock: if Completed never arrives (render clock stopped or the animation replaced), the
                // expiry force-reclaims (see the field's doc).
                _artworkZoomReclaimFallbackTimer.Interval = CurrentMotion.PositionDuration + HoverHideFallbackMargin;
                _artworkZoomReclaimFallbackTimer.Start();
            }
            return;
        }

        ResetArtworkZoomPush();
    }

    /// <summary>
    /// 原地放大的<b>统一收回入口</b>：关 Popup、把封面送回主画布、恢复对齐与缩放原点、推挤清零。送回时封面以
    /// <see cref="SongImageSettleTranslate"/> 从 Popup 终态的位置起步，用一个微移动画（<see cref="ArtworkZoomSettleDuration"/>）
    /// 平滑滑回主树原位——主树与 Popup 是两条像素舍入管道（主树逐级 UseLayoutRounding、Popup 是独立 HWND 各自舍入），
    /// 送回那一帧的位置天然差 1~2 物理像素，任何"切回前把 Popup 挪正"的事后校正都被窗口重定位的异步时序吃掉；
    /// 与其校不准，不如让切换帧从 Popup 终态位置连续起步、肉眼无感地滑回去。
    /// immediate（Unloaded、模式切换等不等一帧的场景）跳过落位直接拆——那几个场景 2px 跳变无人看见。
    /// 是否送回以「元素当前还在不在宿主里」为准、<b>不以 Popup 是否还开着为准</b>：Popup 可能已被外部路径悄悄
    /// 关闭，若据此提前返回，封面会滞留在已关闭的 Popup 里——命中测试永远够不到，鼠标事件从此一件都送不到
    /// （悬停彻底失聪，正是"拖动尝试后封面认不出鼠标"的病灶之一）。
    /// The in-place zoom's <b>single reclaim entry</b>: closes the Popup, returns the cover to the main canvas, restores the
    /// alignments and the scale origin, zeroes the push. On the return the cover starts at the Popup's final spot via
    /// <see cref="SongImageSettleTranslate"/> and a micro-slide (<see cref="ArtworkZoomSettleDuration"/>) eases it onto the
    /// main-tree spot: the main tree and the Popup are two pixel-rounding pipelines (the main tree aligns every layout hop via
    /// UseLayoutRounding, the Popup is an independent HWND rounding on its own), so the return frame naturally differs by 1-2
    /// physical pixels, and any "nudge the Popup straight before switching back" afterthought was eaten by the window
    /// reposition's async timing. Rather than chasing an alignment that never lands, the switch frame starts continuously at the
    /// Popup's final spot and slides home imperceptibly. immediate (Unloaded, mode switches — paths that cannot wait a frame)
    /// tears down at once without the settle; nobody sees a 2 px jump in those. Whether to return the element keys off "is it
    /// still in the host", <b>not "is the Popup still open"</b>: the Popup can be closed behind our back, and bailing out on a
    /// closed Popup would strand the artwork inside it — unreachable by hit-testing, never again delivered a single mouse event
    /// (hover dead for good, one root of "the cover stops recognizing the mouse after a drag attempt").
    /// </summary>
    private void ReclaimArtworkZoom(bool immediate = false)
    {
        if (_artworkHoverZoomPopup is not { } popup || _artworkHoverZoomHost is not { } host)
            return;
        _artworkZoomCollapsing = false;
        // 收回既已落地，兜底计时器与悬停区轮询都失去使命；重复收回是幂等的，重复 Stop 亦然。
        // With the reclaim landed both the fallback timer and the hover-zone poll have served their purpose; a repeated
        // reclaim is idempotent and so is Stop.
        _artworkZoomReclaimFallbackTimer.Stop();
        _artworkZoomHoverSyncTimer.Stop();
        ResetArtworkZoomPush();

        // 落位起点 = 封面此刻在 Popup 里的物理位置（收拢已停、布局稳定，测量可信）；immediate 不需要。
        // The settle origin is the cover's physical spot inside the Popup right now (the collapse has stopped and the layout is
        // stable, so the measurement is trustworthy); immediate needs none.
        Point? settleFrom = null;
        if (!immediate && popup.IsOpen && PresentationSource.FromVisual(SongImageBorder) is not null)
        {
            settleFrom = SongImageBorder.PointToScreen(new Point(0, 0));
        }

        CompleteArtworkZoomReclaim(popup, host, settleFrom);
        if (immediate)
            return;

        // 收回后的悬停复核：收拢期间指针可能移回了原尺寸那一格——放大/收拢都不改变判定用的原格矩形，但指针
        // 在收拢末段才回到格内的那一次可能晚于轮询的最后一拍；收回换树后 WPF 也不会在没有鼠标移动的情况下补发
        // 进入事件，封面于是停在收拢态直到指针再动（"收拢到底却不重新长开"）。延迟一拍复核：指针确实在原格
        // （含边缘容差）内就重新长开；这一拍里若新一轮放大已经把封面接回宿主，则让位——它自管。
        // Post-reclaim hover re-check: mid-collapse the pointer can move back into the original slot — growth and collapse
        // both leave the verdict's rectangle (the original slot) untouched, but a pointer returning only in the collapse's
        // final stretch can slip past the poll's last tick; and after the return WPF delivers no enter event without a mouse
        // move, leaving the cover collapsed until the pointer twitches ("collapsed all the way but never grew back"). One
        // deferred tick re-checks: a pointer genuinely inside the slot (edge tolerance included) grows the cover again; if a
        // fresh zoom has already carried the cover back into the host, this yields — that zoom runs its own machine.
        Dispatcher.BeginInvoke(() =>
        {
            // 收回后复核是否要重新长开：优先用真实可靠的 IsMouseOver（收回后封面回到主树、无捕获）。
            // 不再依赖几何判定——它在陈旧捕获或跨屏 DPI 下会误报"仍在"，把刚收回的放大又顶回来，正是放大卡死在
            // 完全放大帧的另一条路径（收回→几何误报→重新长开→再收回…循环）。
            // After the reclaim, re-check whether to grow again using the truthful IsMouseOver (the cover is back in the main
            // tree with no capture). We no longer lean on the geometry verdict, which misreports "still over" under a stale
            // capture or cross-screen DPI and bounces the just-reclaimed zoom back up — another path to the stuck zoom
            // (reclaim → geometry misfire → regrow → reclaim…).
            if (!ReferenceEquals(SongImageBorder.Parent, _artworkHoverZoomHost) && SongImageBorder.IsMouseOver)
                AnimateArtworkHover(true);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// 收回的落地段：关 Popup、把封面元素送回主画布、撤掉替身、恢复对齐与缩放原点，并把两层裁切与区域扩展恢复到
    /// 收回态。送回后同步跑一次布局（此时测量才可信），把「Popup 终态位置 − 主树落位」的管道差交给
    /// <see cref="SongImageSettleTranslate"/> 用一个微移动画吸收：封面先画在 Popup 刚才的位置，再滑回原位——
    /// 切换帧与滑动全程视觉连续，没有跳变。半物理像素以内的差肉眼不可辨，直接跳过。
    /// The reclaim's landing phase: closes the Popup, returns the artwork element to the main canvas, dismisses the stand-in,
    /// restores the alignments and the scale origin, and returns both clipping layers and the region extension to their
    /// reclaimed state. After the return one synchronous layout pass runs (only then is a measurement trustworthy), and the
    /// pipeline gap between "the Popup's final spot" and "the main-tree spot" is absorbed by
    /// <see cref="SongImageSettleTranslate"/> with a micro-slide: the cover is drawn at the Popup's last position first and
    /// eases home — the switch and the slide stay visually continuous, no jump. Gaps under half a physical pixel are
    /// imperceptible and skipped outright.
    /// </summary>
    private void CompleteArtworkZoomReclaim(
        System.Windows.Controls.Primitives.Popup popup,
        System.Windows.Controls.Grid host,
        Point? settleFrom)
    {
        if (popup.IsOpen)
            popup.IsOpen = false;

        if (!ReferenceEquals(SongImageBorder.Parent, host))
        {
            // 元素已不在宿主（从未离开或已送回）：把两层裁切与区域扩展恢复到收回态即可，重复收回是幂等的。
            // The element is not in the host (never left or already returned): restoring the clipping layers and the region
            // extension to their reclaimed state completes it — a repeated reclaim is idempotent.
            DismissArtworkZoomSubstitute();
            MainBorder.ClipToBounds = true;
            ArtworkZoomExtensionChanged?.Invoke(this, 0);
            return;
        }

        host.Children.Remove(SongImageBorder);
        SongImageBorder.HorizontalAlignment = _artworkZoomRestoreHorizontalAlignment;
        SongImageBorder.VerticalAlignment = _artworkZoomRestoreVerticalAlignment;
        SongImageBorder.RenderTransformOrigin = new Point(0.5, 0.5);
        if (!MainCanvas.Children.Contains(SongImageBorder))
            MainCanvas.Children.Add(SongImageBorder);
        // 真封面已经就位（替身本次的桥接使命已由主窗口"已合成过"的那几帧完成），撤掉替身。
        // The real cover is in place (the stand-in's bridging is already carried by the frames the main window composed while
        // it was up), so it exits.
        DismissArtworkZoomSubstitute();
        // 元素已回主树、推挤已清零，两层裁切此刻才全部恢复：早一步都会把仍在滑回的组件裁掉。
        // The element is back in the main tree and the push is zeroed, so only now do both clipping layers return: any
        // earlier would clip the widgets still sliding back.
        MainBorder.ClipToBounds = true;
        ArtworkZoomExtensionChanged?.Invoke(this, 0);

        // 落位：先清零位移层（放大期间它驮着夹持补偿，带着它测量会读出假差量），再同步布局一次——VisualOffset
        // 此刻才真实建立（刚换树的元素在布局前测不出新位置）。两条管道的残差以微移动画吸收：开窗校准
        // （CalibrateArtworkZoomOnShow）生效时残差 < 0.5px、微滑被跳过；它没生效（两次都量不准）时这里兜底。
        // Settle: zero the shift layer first (it carried the clamp compensation during the zoom; measuring with it on would
        // read a phantom gap), then one synchronous layout pass — only then is the VisualOffset real (a freshly re-parented
        // element measures to nothing before layout). Any residual pipeline gap goes into a micro-slide: with the on-show
        // calibration (CalibrateArtworkZoomOnShow) in effect the residual is under half a pixel and the slide is skipped;
        // when that failed (both attempts implausible) this is the fallback.
        SongImageSettleTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        SongImageSettleTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        SongImageSettleTranslate.X = 0;
        SongImageSettleTranslate.Y = 0;
        if (settleFrom is not { } from)
            return;
        UpdateLayout();
        var now = SongImageBorder.PointToScreen(new Point(0, 0));
        var dx = from.X - now.X;
        var dy = from.Y - now.Y;
        if (PresentationSource.FromVisual(this)?.CompositionTarget is not { } target)
            return;
        var dpi = target.TransformToDevice;
        // 校准回填：实测差量只并入「封面渲染偏移校准 E」，绝不触碰 host 摆放——往摆放里反馈会双重补偿（曾致
        // comp 每轮翻倍的指数发散）。E 的反馈斜率为 1：差量多少 E 加多少，下一轮同差量即被抵消，不可能放大；
        // 环境变化时差量反向出现，E 随之自动回落。半物理像素以内视为噪声。
        // Calibration feedback: the measured gap folds ONLY into the rendered-offset calibration E, never into the host
        // placement — feedback there double-compensates (it once doubled comp every round into an exponential runaway).
        // E's feedback has slope 1: the gap adds once and is cancelled next round, so it cannot amplify; when the
        // environment changes the gap re-appears sign-flipped and E settles back. Residuals under half a physical pixel
        // count as noise.
        if (Math.Abs(dx) >= 0.5)
            _artworkZoomCoverOffsetCalibrationX += dx;
        if (Math.Abs(dy) >= 0.5)
            _artworkZoomCoverOffsetCalibrationY += dy;
        if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5)
            return;
        // 缩放此刻必为 1 倍（收拢已完成才送回），group 内 Translate 与 Scale 的先后顺序不影响结果。
        // The scale is exactly 1× by now (the collapse finished before the return), so the group's Translate/Scale order is
        // irrelevant to the result.
        SongImageSettleTranslate.X = dx / dpi.M11;
        SongImageSettleTranslate.Y = dy / dpi.M22;
        // 代号递增并闭包捕获：Completed 只在代号仍匹配（本轮微滑未被新一轮放大取代）时才清零位移层。
        // Bump the generation and capture it: the Completed handler zeroes the shift layer only while its token still
        // matches (this slide was not superseded by a newer zoom).
        var generation = ++_artworkZoomSettleGeneration;
        var slideX = new System.Windows.Media.Animation.DoubleAnimation
        {
            To = 0,
            Duration = new Duration(ArtworkZoomSettleDuration),
            EasingFunction = new PowerEase { Power = 2, EasingMode = EasingMode.EaseOut }
        };
        var slideY = new System.Windows.Media.Animation.DoubleAnimation
        {
            To = 0,
            Duration = new Duration(ArtworkZoomSettleDuration),
            EasingFunction = new PowerEase { Power = 2, EasingMode = EasingMode.EaseOut }
        };
        slideX.Completed += (_, _) =>
        {
            if (generation != _artworkZoomSettleGeneration)
                return;
            SongImageSettleTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            SongImageSettleTranslate.X = 0;
        };
        slideY.Completed += (_, _) =>
        {
            if (generation != _artworkZoomSettleGeneration)
                return;
            SongImageSettleTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            SongImageSettleTranslate.Y = 0;
        };
        SongImageSettleTranslate.BeginAnimation(TranslateTransform.XProperty, slideX);
        SongImageSettleTranslate.BeginAnimation(TranslateTransform.YProperty, slideY);
    }

    /// <summary>
    /// 在主画布原格位置放一份替身封面（见 <see cref="_artworkZoomSubstitute"/>）：与真封面<b>同构</b>——同一把封面画刷、同一把暂停遮罩
    /// （<see cref="SongImagePauseScrimBrush"/>，故换歌与暂停遮罩都自动跟随）、按当前显隐与字形复制的占位符号。层级压在封面（ZIndex 2）
    /// 之下，且放大轮廓始终把原格包住，故真封面在场时它完全被遮住、缩到 1× 也不会露边；已存在时先撤旧，连续放大不叠加。
    /// "同构"是刻意的：替身要在真封面离开主树的那几帧里充当"这一格"，合成必须与静态、与放大态一模一样，否则换树处就是一次可见的跳变。
    /// Places a stand-in cover at the original slot in the main canvas (see <see cref="_artworkZoomSubstitute"/>), <b>isomorphic</b> to the
    /// real cover: the same cover brush, the same paused scrim (<see cref="SongImagePauseScrimBrush"/>, so track changes and the paused
    /// dimming both follow by themselves), and the placeholder glyph mirrored with its current visibility and glyph. It sits under the
    /// artwork (ZIndex 2) and the enlarged outline encloses the original slot, so the real cover hides it whole and it never peeks out even
    /// at 1×. An existing one is dismissed first, so consecutive zooms never stack. The isomorphism is deliberate: the stand-in stands in
    /// for the slot during the frames the real cover spends outside the main tree, and its composite has to match the static slot and the
    /// zoomed cover exactly, or the hand-off becomes a visible jump.
    /// </summary>
    private void ShowArtworkZoomSubstitute()
    {
        DismissArtworkZoomSubstitute();

        var icon = new Wpf.Ui.Controls.SymbolIcon
        {
            Symbol = SongImagePlaceholder.Symbol,
            FontSize = SongImagePlaceholder.FontSize,
            Filled = SongImagePlaceholder.Filled,
            Foreground = SongImagePlaceholder.Foreground,
            Visibility = SongImagePlaceholder.Visibility
        };
        // 与真封面共用同一把遮罩画刷：浓度与颜色都由那把画刷承担（见 ApplyArtworkDimming 与 ApplyAppearanceSettings），
        // 替身因此不需要任何人来同步。
        // Shares the real cover's scrim brush: that brush owns both the strength and the colour (see ApplyArtworkDimming and
        // ApplyAppearanceSettings), so the stand-in needs no one to sync it.
        var scrim = new Border
        {
            CornerRadius = SongImageBorder.CornerRadius,
            Background = SongImagePauseScrimBrush
        };
        var substitute = new Border
        {
            Width = SongImageBorder.ActualWidth,
            Height = SongImageBorder.ActualHeight,
            CornerRadius = SongImageBorder.CornerRadius,
            ClipToBounds = true,
            Background = SongImage,
            IsHitTestVisible = false,
            Child = new System.Windows.Controls.Grid { Children = { scrim, icon } }
        };
        Canvas.SetLeft(substitute, Canvas.GetLeft(SongImageBorder));
        Canvas.SetTop(substitute, Canvas.GetTop(SongImageBorder));
        Panel.SetZIndex(substitute, 1);
        MainCanvas.Children.Add(substitute);
        _artworkZoomSubstitute = substitute;
    }

    /// <summary>撤掉替身封面（幂等）：送回主树、控件离树、连续放大重建时都会走到。/ Dismisses the stand-in cover (idempotent): the return to the main tree, the control leaving the tree, and consecutive zoom activations all reach it.</summary>
    private void DismissArtworkZoomSubstitute()
    {
        if (_artworkZoomSubstitute is not { } substitute)
            return;

        MainCanvas.Children.Remove(substitute);
        _artworkZoomSubstitute = null;
    }

    /// <summary>
    /// 开窗即校准。封面渲染偏移校准（<c>_artworkZoomCoverOffsetCalibrationX/Y</c>）原先只在第一次收回的落位测量里学到，
    /// 于是本会话的第一次放大必然带着未校准偏差长出来——用户看到的就是"第一次动画往上挪了 3~4px"。这里在 Popup 刚开、
    /// 封面还没显形之前先量一次补掉。同轮量得到（Popup 已定位）就当场落定，第一帧即正确；量不到（窗口尚未定位，读回
    /// 默认位置）则退到下一拍再量并走同曲线短动画补正。两次都量不准就什么都不做，仍由收回落位测量兜底。
    /// Calibrates the moment the Popup opens. The rendered-offset calibration used to be learned only from the first
    /// reclaim's settle measurement, so the session's first zoom always grew out with that bias — the "first animation
    /// nudges up 3-4 px" the user sees. Measuring here, before the cover is visible, lands it on the right spot from the
    /// very first frame; a reading taken before the Popup's window has been positioned is rejected and retried one tick
    /// later behind a short animation on the same curve. When neither attempt yields a plausible gap nothing happens and
    /// the reclaim's settle measurement stays the fallback.
    /// </summary>
    private void CalibrateArtworkZoomOnShow(
        Point homeScreen,
        double dpiScale,
        System.Windows.Controls.Grid host,
        System.Windows.Controls.Primitives.Popup popup)
    {
        var generation = ++_artworkZoomShowCalibrationGeneration;
        if (TryApplyArtworkZoomShowCalibration(homeScreen, dpiScale, host, popup, animated: false))
            return;

        Dispatcher.BeginInvoke(() =>
        {
            if (generation != _artworkZoomShowCalibrationGeneration)
                return;
            TryApplyArtworkZoomShowCalibration(homeScreen, dpiScale, host, popup, animated: true);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// 量一次 Popup 里封面与原树位置的差量：可信则补进校准 E，并把当前这一帧也挪到正确的点上，返回 true。
    /// 差量超出合理范围视为窗口尚未定位，返回 false 且什么都不做。animated 决定当前帧是直接落位（同轮、尚无可见帧）
    /// 还是走短动画（晚到的那一拍可能已在生长中，直接落位会看见一次跳变；过渡被关闭时同样直接落位）。
    /// Takes one reading of how far the Popup's cover sits from the main-tree spot: if plausible it folds into the
    /// calibration and moves the current frame onto the correct spot too, returning true. A gap outside the plausible range
    /// means the window is not positioned yet — returns false and does nothing. It also moves the current frame, landing it
    /// outright (same turn, nothing visible yet) or behind a short animation (the late retry can land mid-growth, where a
    /// bare jump would be seen; transitions off lands outright as well).
    /// </summary>
    private bool TryApplyArtworkZoomShowCalibration(
        Point homeScreen,
        double dpiScale,
        System.Windows.Controls.Grid host,
        System.Windows.Controls.Primitives.Popup popup,
        bool animated)
    {
        if (!popup.IsOpen || !ReferenceEquals(SongImageBorder.Parent, host))
            return false;
        if (PresentationSource.FromVisual(SongImageBorder) is null)
            return false;

        UpdateLayout();
        // 晚到的那一拍可能已在生长中，此刻缩放未必还是 1 倍：PointToScreen 会把渲染变换算进去，先按当前倍率扣掉
        // 缩放自身造成的位移，剩下的才是「布局层面的渲染偏差」。
        // The late retry can land mid-growth, where the scale is no longer 1: PointToScreen folds render transforms in, so
        // the scale's own displacement is subtracted first and only the layout-level bias is left.
        var scale = SongImageScale.ScaleX;
        var measured = SongImageBorder.PointToScreen(new Point(0, 0));
        var dx = measured.X - 0.5 * SongImageBorder.ActualWidth * (1 - scale) * dpiScale - homeScreen.X;
        var dy = measured.Y - SongImageBorder.ActualHeight * (1 - scale) * dpiScale - homeScreen.Y;
        if (Math.Abs(dx) > ArtworkZoomShowCalibrationMaxGapPx || Math.Abs(dy) > ArtworkZoomShowCalibrationMaxGapPx)
            return false;
        if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5)
            return true;

        // E 记的是「Popup 里封面的位置 − 主树原位」，与收回落位测量同一个量纲；再把这一帧的位移反向补掉。
        // E holds "the Popup cover's spot minus the main-tree spot", the same quantity the reclaim's settle measurement
        // produces; the current frame then takes the opposite shift so it lands on the corrected spot right away.
        _artworkZoomCoverOffsetCalibrationX += dx;
        _artworkZoomCoverOffsetCalibrationY += dy;
        MoveArtworkZoomSettleBy(-dx / dpiScale, -dy / dpiScale, animated);
        return true;
    }

    /// <summary>把落位层在当前值上平移给定量（DIP）。animated 为假（或过渡被关闭）时直接落位，为真时走与生长同曲线
    /// 的短动画——补正藏在放大的过程里，看不出单独的一次挪动。
    /// Shifts the settle layer by the given amount (DIP) on top of its current value: with animated false (or transitions off)
    /// it lands outright, with true it rides a short animation on the growth's own curve, hiding the correction inside the zoom.
    /// </summary>
    private void MoveArtworkZoomSettleBy(double offsetX, double offsetY, bool animated)
    {
        var targetX = SongImageSettleTranslate.X + offsetX;
        var targetY = SongImageSettleTranslate.Y + offsetY;
        if (!animated || !CurrentMotion.UseTransitions)
        {
            SongImageSettleTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            SongImageSettleTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            SongImageSettleTranslate.X = targetX;
            SongImageSettleTranslate.Y = targetY;
            return;
        }

        var easing = new PowerEase { Power = 2, EasingMode = EasingMode.EaseOut };
        SongImageSettleTranslate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation
        {
            To = targetX,
            Duration = CurrentMotion.PositionDuration,
            EasingFunction = easing
        });
        SongImageSettleTranslate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation
        {
            To = targetY,
            Duration = CurrentMotion.PositionDuration,
            EasingFunction = easing
        });
    }

    /// <summary>
    /// 收集原地放大期间需要向右避让的元素并准备它们的推挤变换：文字区、文字悬停块、静置层四个小组件、静置进度条
    /// 与悬停层宿主都住在封面右侧，封面放大时把它们整体右移同样的量，放大于是读作"推开布局"而不是"盖在文字上"。
    /// 渲染变换不参与布局，位置引擎随时重写 Canvas.SetLeft/Margin 也冲不掉推挤。文字区的推挤走专用支，不与入场动画
    /// 共用实例——否则换歌恰逢悬停时入场动画会整体换掉 RenderTransform、抹掉推挤，标题/艺术家因此停止随放大移动
    /// （BUG 2 的根因）。
    /// Collects the elements that must yield to the right during the in-place zoom and prepares their push transforms: the text
    /// region, its hover block, the rest layer's four widgets, the rest progress, and the hover-layer host all live right of the
    /// artwork, and shifting them right by the same amount makes the zoom read as "pushing the layout apart" rather than "covering
    /// the text". A render transform stays out of layout, so the position engine rewriting Canvas.SetLeft/Margin never washes the
    /// push away. The text region's push lives on its own branch, not shared with the entrance animation — otherwise a track change
    /// while hovering would replace the whole RenderTransform and erase the push, freezing the title/artist against the zoom
    /// (root of BUG 2).
    /// </summary>
    private void BuildArtworkZoomPushTransforms()
    {
        _artworkZoomPushTransforms = new List<TranslateTransform>();
        foreach (var element in ArtworkZoomPushElements())
            _artworkZoomPushTransforms.Add(GetOrCreatePushTransform(element));
    }

    /// <summary>
    /// 取得（或建立）某个元素的推挤变换。文字区不能让入场动画与放大推挤共用同一个 RenderTransform：
    /// 前者换歌时整体换掉它。因此文字区的推挤走 TransformGroup 中的专用支，其余元素维持原来的裸 TranslateTransform。
    /// Gets (or builds) an element's push transform. The text region must not let the entrance animation and the zoom-push share
    /// one RenderTransform — the former replaces it outright on a track change — so its push lives on a dedicated branch of a
    /// TransformGroup while the other elements keep their bare TranslateTransform.
    /// </summary>
    private TranslateTransform GetOrCreatePushTransform(UIElement element)
    {
        if (ReferenceEquals(element, SongInfoStackPanel))
            return GetOrCreateSongInfoPushTransform();
        if (element.RenderTransform is TranslateTransform existing)
            return existing;
        var transform = new TranslateTransform();
        element.RenderTransform = transform;
        return transform;
    }

    /// <summary>
    /// 取得（或建立）文字区的推挤支，并保证它与入场位移支同处一个 TransformGroup：两者若共用同一个 RenderTransform
    /// 实例，换歌时的入场动画会整体换掉它、抹掉推挤（BUG 2 根因）。推挤支与入场支是两个独立的 TranslateTransform，
    /// 分别由放大推挤动画与入场动画驱动，互不覆盖。
    /// Gets (or builds) the text region's push branch and ensures it shares a TransformGroup with the entrance branch: had the two
    /// shared one RenderTransform instance, the track-change entrance would replace it outright and wipe the push (root of BUG 2).
    /// The push and entrance branches are two independent TranslateTransforms, driven by the zoom-push and entrance animations
    /// respectively, so neither overwrites the other.
    /// </summary>
    private TranslateTransform GetOrCreateSongInfoPushTransform()
    {
        _songInfoPushTransform ??= new TranslateTransform();
        if (SongInfoStackPanel.RenderTransform is TransformGroup group)
        {
            if (!group.Children.Contains(_songInfoPushTransform))
                group.Children.Add(_songInfoPushTransform);
        }
        else if (SongInfoStackPanel.RenderTransform is TranslateTransform bare)
        {
            // 入场动画留下的裸 TranslateTransform 当作入场支，推挤支补进来，合成一个 group。
            // A bare TranslateTransform left by the entrance animation becomes the entrance branch; the push branch is added.
            _songInfoEntranceTransform ??= bare;
            var merged = new TransformGroup();
            merged.Children.Add(_songInfoPushTransform);
            merged.Children.Add(_songInfoEntranceTransform);
            SongInfoStackPanel.RenderTransform = merged;
        }
        else
        {
            _songInfoEntranceTransform ??= new TranslateTransform();
            var newGroup = new TransformGroup();
            newGroup.Children.Add(_songInfoPushTransform);
            newGroup.Children.Add(_songInfoEntranceTransform);
            SongInfoStackPanel.RenderTransform = newGroup;
        }
        return _songInfoPushTransform;
    }

    /// <summary>封面右侧需要推挤避让的全部元素；横向任务栏里它们按布局引擎的排布都在封面之右。/ All elements that must yield right of the artwork; in a horizontal taskbar the layout engine keeps every one of them right of the cover.</summary>
    private IEnumerable<UIElement> ArtworkZoomPushElements()
    {
        yield return SongInfoStackPanel;
        yield return SongInfoHoverOverlay;
        yield return TaskbarRestProgress;
        yield return TaskbarSpectrumHoverSurface;
        yield return TaskbarPerformanceHoverSurface;
        yield return TaskbarOutputDeviceHoverSurface;
        yield return TaskbarVolumeHoverSurface;
        yield return HoverRevealHost;
    }

    /// <summary>把推挤推进到目标量；easing 为空时直接跳变（用于关闭过渡或立即路径）。/ Advances the push to the target offset; a null easing jumps outright (for transitions off or the immediate path).</summary>
    private void AnimateArtworkZoomPush(double targetOffset, TimeSpan duration, IEasingFunction? easing)
    {
        if (_artworkZoomPushTransforms is null)
            return;
        foreach (var transform in _artworkZoomPushTransforms)
        {
            if (easing is null)
            {
                transform.BeginAnimation(TranslateTransform.XProperty, null);
                transform.X = targetOffset;
            }
            else
            {
                transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation
                {
                    To = targetOffset,
                    Duration = new Duration(duration),
                    EasingFunction = easing
                });
            }
        }
    }

    /// <summary>推挤清零并丢弃清单；收回路径无条件调用，防止半推状态残留。/ Zeroes the push and drops the list; reclaim paths call it unconditionally so no half-pushed state survives.</summary>
    private void ResetArtworkZoomPush()
    {
        if (_artworkZoomPushTransforms is null)
            return;
        foreach (var transform in _artworkZoomPushTransforms)
        {
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.X = 0;
        }
        _artworkZoomPushTransforms = null;
    }

    /// <summary>
    /// 原地放大的动画本体：生长（→2 倍）与收拢（→1 倍）都在 Popup 里完成、底部锚定不动，右侧内容的推挤以同一条曲线
    /// 同一个时长进退——放大于是读作"把布局推开"，而不是"一张图突然盖上"。收拢动画结束后（Completed）才把元素送回
    /// 主树；中途回来的指针已先在 ShowArtworkHoverZoom 里清掉收拢标记，倍率被重新拉起来，不会误收。
    /// The in-place zoom's animation core: growth (→2×) and collapse (→1×) both play inside the Popup with the bottom anchor
    /// unmoved, while the right-hand push slides in and out on the same curve and over the same duration — so the zoom reads as
    /// "pushing the layout apart" instead of "a card suddenly slapped on". The element returns to the main tree only after the
    /// collapse finishes (Completed); a pointer returning midway has already cleared the collapse flag in ShowArtworkHoverZoom and
    /// the factor is pulled back up, so the reclaim never misfires.
    /// </summary>
    private void AnimateArtworkZoom(bool growing, bool immediate, double hintTargetOpacity)
    {
        var motion = CurrentMotion;
        var targetScale = growing ? ArtworkHoverZoomScaleFactor : 1.0;
        var targetPush = growing ? _artworkZoomPushOffset : 0.0;

        if (immediate || !motion.UseTransitions)
        {
            SongImageScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            SongImageScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            SongImageScale.ScaleX = targetScale;
            SongImageScale.ScaleY = targetScale;
            SetArtworkHoverHintOpacity(hintTargetOpacity);
            AnimateArtworkZoomPush(targetPush, TimeSpan.Zero, null);
            if (!growing)
                ReclaimArtworkZoom(immediate: true);
            return;
        }

        var duration = motion.PositionDuration;
        // 生长用二次缓出（比全局三次缓出更柔），收拢用正弦缓入缓出——从放大态收回来更"丝滑"，两头都缓。
        // Growth eases out quadratically (softer than the shared cubic); the collapse uses a sine ease-in-out so the tuck back
        // from the enlarged state is silky, easing at both ends.
        IEasingFunction easing = growing
            ? new PowerEase { Power = 2, EasingMode = EasingMode.EaseOut }
            : new SineEase { EasingMode = EasingMode.EaseInOut };
        var scaleXAnimation = new DoubleAnimation
        {
            To = targetScale,
            Duration = duration,
            EasingFunction = easing
        };
        SongImageScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnimation);
        SongImageScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation
        {
            To = targetScale,
            Duration = duration,
            EasingFunction = easing
        });
        AnimateArtworkZoomPush(targetPush, duration, easing);
        SongImageHoverHint.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
        {
            To = hintTargetOpacity,
            Duration = duration,
            EasingFunction = easing
        });

        if (!growing)
        {
            scaleXAnimation.Completed += (_, _) =>
            {
                if (_artworkZoomCollapsing)
                    ReclaimArtworkZoom();
            };
        }
    }

    /// <summary>
    /// 原地放大期间判定指针是否真的离开了封面。换树的一瞬 WPF 会先丢一次命中，立刻收起会把封面闪回主画布，
    /// 因此离开事件不直接收起，而是延迟两拍（Background 优先级）后再判定，判定走
    /// <see cref="IsPointerOverArtwork"/>（原尺寸那一格 + 边缘容差，屏幕几何、不受捕获影响）而非
    /// <c>IsMouseOver</c>：后者在捕获残留期间会谎报，而幽灵捕获正是收不回去的常见背景。它与
    /// <c>_artworkZoomHoverSyncTimer</c> 的轮询共用同一个判据，两条路径结论必然一致——事件只是比轮询早几毫秒
    /// 到达的同一条结论。
    /// Decides whether the pointer truly left the artwork during the in-place zoom. The re-parent itself drops the hit for a
    /// moment, and collapsing at once would flash the cover back into the bar, so the leave event defers the decision by two
    /// ticks (Background priority) before deciding, and the decision runs through
    /// <see cref="IsPointerOverArtwork"/> (the original slot plus the edge tolerance, screen geometry, capture-immune) rather
    /// than <c>IsMouseOver</c>, which lies while a stale capture lingers — and a ghost capture is precisely the backdrop this
    /// collapse often has to run against. It shares that one criterion with the <c>_artworkZoomHoverSyncTimer</c> poll, so the
    /// two paths can only ever agree — the event is merely the same conclusion arriving a few milliseconds earlier.
    /// </summary>
    private void VerifyArtworkZoomLeave()
    {
        if (_artworkHoverZoomPopup?.IsOpen != true)
            return;

        // 换树（封面移入 Popup）那一瞬 WPF 会先丢一次命中而误发离开：以「show 之后多久」判断是否在重挂窗口内，
        // 窗口内忽略（那是换树的副作用）；窗口外才是真实离开。真实离开不再依赖几何判定——几何判定在陈旧捕获或跨屏
        // DPI 下可能误报"仍在"，正是放大卡死在完全放大帧的根因之一——直接收回，更可靠。
        // The re-parent (cover moved into the Popup) drops the hit for a moment and spuriously raises leave: gate on how long
        // since the show to detect that artifact; within the window it is ignored. A real leave afterwards no longer leans on
        // the geometry verdict, which can misreport "still over" under a stale capture or cross-screen DPI — a root of the
        // zoom getting stuck at full size — and reclaims outright, more reliably.
        var sinceShow = Stopwatch.GetElapsedTime(_artworkZoomShowStamp).TotalMilliseconds;
        if (sinceShow < ArtworkZoomReparentGuardMs)
            return;

        AnimateArtworkHover(false);
    }

    /// <summary>
    /// 按<b>屏幕几何</b>判定指针是否悬在封面上：读 Win32 的物理光标位置，与封面<b>在栏内的原尺寸那一格</b>
    /// （不是放大后的轮廓）做包含判定，四周再放开 <see cref="ArtworkHoverEdgeToleranceDip"/>。这是唯一在任何鼠标
    /// 捕获残留期间仍然说真话的判据——物理光标的位置不受捕获重定向影响，而 <c>IsMouseOver</c> 与
    /// <c>Mouse.DirectlyOver</c> 都会被遗留捕获改写：SubTree 捕获把命中测试沙盒进捕获子树，已换树进 Popup 的封面
    /// 在子树之外，两个属性恒为假/恒指向别处，"拖动尝试后封面认不出鼠标"的误收与误放都源于此。封面断连（滞留已
    /// 关闭的 Popup）时几何判定返回假——事实也正该如此，那个元素此刻确实收不到任何输入。
    /// 判定<b>刻意不跟随缩放</b>：悬停区若跟着放大后的轮廓一起长大，指针就永远出不了区（放大轮廓把原格整个包住），
    /// 收拢只能靠另一个更小的判据兜底，两种判据并存必然互相打架。以原格为纲后，放大、收拢、落位微滑全程共用同一个
    /// 矩形——判定稳定，且"离开封面"的语义与用户看到的封面位置一致。
    /// Decides from <b>screen geometry</b> whether the pointer hovers the artwork: it reads the physical cursor via Win32 and
    /// tests containment against the artwork's <b>original in-bar slot</b> (not the enlarged outline) widened by
    /// <see cref="ArtworkHoverEdgeToleranceDip"/>. This is the only verdict that keeps telling the truth while any stale mouse
    /// capture lingers — the physical cursor's position is immune to capture redirection, whereas both <c>IsMouseOver</c> and
    /// <c>Mouse.DirectlyOver</c> get rewritten by it: a SubTree capture sandboxes hit-testing inside the captured subtree, the
    /// re-parented artwork sits outside it, and both properties read false / point elsewhere forever — the source of both the
    /// false collapse and the false grow behind "the cover stops recognizing the mouse after a drag attempt". A disconnected
    /// artwork (stranded inside a closed Popup) makes the geometry verdict false, which is exactly right: that element truly
    /// receives no input right now. The verdict <b>deliberately ignores the scale</b>: a hover zone that grew with the enlarged
    /// outline could never be left at all (the enlarged outline encloses the original slot whole), leaving the collapse to a
    /// second, smaller criterion — and two criteria are bound to fight each other. Keyed to the original slot instead, the
    /// growth, the collapse, and the settle micro-slide all share one rectangle: the verdict is stable, and "left the cover"
    /// means what the cover's position tells the user it means.
    /// </summary>
    private bool IsPointerOverArtwork()
    {
        if (!NativeMethods.GetCursorPos(out var cursor))
            return false;
        // 封面已断连（滞留已关闭的 Popup 等）时 PointToScreen 无从换算，视觉矩形本身也不再有效。
        // A disconnected artwork (stranded in a closed Popup and the like) leaves PointToScreen meaningless, and its visual
        // rect is no longer valid either.
        if (PresentationSource.FromVisual(SongImageBorder) is null)
            return false;
        var width = SongImageBorder.ActualWidth;
        var height = SongImageBorder.ActualHeight;
        if (width <= 0 || height <= 0 || double.IsNaN(width) || double.IsNaN(height))
            return false;

        // 用封面自身的呈现源取 DPI：放大时封面在 Popup 里，Popup 可能与主窗口不在同一块屏幕（DPI 不同），
        // 用主窗口的 DPI 会把原格矩形算错，导致几何判定误报"仍在"或"已离开"——前者正是放大卡死的根因之一。
        // Use the cover's own presentation source for the DPI: while zoomed the cover lives in the Popup, which can sit on a
        // different monitor (different DPI) than the main window; the main window's DPI would mis-size the slot rectangle and
        // make the geometry verdict misreport "still over" or "already left" — the former being a root of the stuck zoom.
        var dpiScale = PresentationSource.FromVisual(SongImageBorder)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        var tolerance = ArtworkHoverEdgeToleranceDip * dpiScale;
        // 锚点取"底边中点"：放大以它为原点（RenderTransformOrigin=(0.5,1)），该点在 1× 与放大态下屏幕位置相同，
        // 于是同一个锚点就能还原出原格；缩放到 1× 时原点取值根本不影响 PointToScreen。
        // The anchor is the bottom-centre: the growth scales about it (RenderTransformOrigin = (0.5,1)), so it sits at the
        // same screen spot at 1× and enlarged, and one anchor reconstructs the original slot either way; at 1× the origin
        // value does not affect PointToScreen at all.
        var anchor = SongImageBorder.PointToScreen(new Point(width * 0.5, height));
        var halfWidth = width * 0.5 * dpiScale;
        var heightPx = height * dpiScale;
        return cursor.X >= anchor.X - halfWidth - tolerance && cursor.X <= anchor.X + halfWidth + tolerance &&
            cursor.Y >= anchor.Y - heightPx - tolerance && cursor.Y <= anchor.Y + tolerance;
    }

    /// <summary>
    /// 原地放大期间的悬停区同步（由 <c>_artworkZoomHoverSyncTimer</c> 每约 30 ms 调一次）：几何判定指针是否还在
    /// 原格 + 容差内——在内且正在收拢就重新长开，在外就收拢。已在放大（Popup 开着且未收拢）时什么都不做：重复调用
    /// <see cref="AnimateArtworkHover"/> 会把生长动画从头续期，生长速率被拖成渐近线，看起来像卡在半路。
    /// Popup 已关（收回完成）时顺手停表——轮询只为放大存续期间服务。
    /// The hover-zone sync while the in-place zoom lasts (ticked by <c>_artworkZoomHoverSyncTimer</c> roughly every 30 ms):
    /// the geometry verdict decides whether the pointer still sits within the original slot plus the tolerance — inside while
    /// collapsing grows it back, outside collapses it. Nothing happens while the zoom is already up (Popup open, not
    /// collapsing): a repeated <see cref="AnimateArtworkHover"/> would restart the growth animation from full, dragging its
    /// rate into an asymptote that reads as stuck halfway. A closed Popup (the reclaim landed) stops the clock too — the poll
    /// exists for the zoom's lifetime only.
    /// </summary>
    private void SyncArtworkZoomHoverFromPointer()
    {
        if (_artworkHoverZoomPopup?.IsOpen != true)
        {
            _artworkZoomHoverSyncTimer.Stop();
            return;
        }

        if (!IsPointerOverArtwork())
        {
            AnimateArtworkHover(false);
            return;
        }

        // 已在放大（未收拢）就不打扰；收拢中途回到原格则立刻重新长开。
        // Leave a settled zoom alone; a pointer back inside mid-collapse grows it again at once.
        if (_artworkZoomCollapsing)
            AnimateArtworkHover(true);
    }

    /// <summary>显示大图预览；已打开时只刷新内容，不重播进入动画。/ Shows the large preview; an already-open popup only refreshes its content without replaying the entrance.</summary>
    private void ShowArtworkHoverPreview(bool immediate = false)
    {
        if (_artworkHoverPreview is not { } popup)
            return;

        RefreshArtworkHoverPreviewContent();
        if (popup.IsOpen)
            return;

        popup.IsOpen = true;
        if (_artworkHoverPreviewScale is not { } scale)
            return;

        var motion = CurrentMotion;
        if (immediate || !motion.UseTransitions)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = 1;
            scale.ScaleY = 1;
            return;
        }

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation
        {
            From = 0.75,
            To = 1,
            Duration = motion.StandardDuration,
            EasingFunction = CreateEaseOut()
        });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation
        {
            From = 0.75,
            To = 1,
            Duration = motion.StandardDuration,
            EasingFunction = CreateEaseOut()
        });
    }

    /// <summary>收起大图预览。关闭是立即的：它不承载交互，退场动画只会拖住任务栏上方的空间。/ Collapses the large preview. Closing is immediate: it carries no interaction, and an exit animation would only hold space above the taskbar.</summary>
    private void HideArtworkHoverPreview()
    {
        if (_artworkHoverPreview?.IsOpen == true)
            _artworkHoverPreview.IsOpen = false;
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
        if (_artworkHoverZoomHost is { } host && ReferenceEquals(SongImageBorder.Parent, host)
            && _artworkHoverZoomPopup?.IsOpen != true)
            ReclaimArtworkZoom();

        if (_artworkHoverZoomPopup?.IsOpen != true)
            return;

        Dispatcher.BeginInvoke(() =>
        {
            if (_artworkHoverZoomPopup?.IsOpen == true)
                AnimateArtworkHover(IsPointerOverArtwork());
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// 宿主在拖动按下时调用：立即（无动画）收回原地放大，让拖动照常进行。
    ///
    /// 收回必须<b>同步且无动画</b>。拖动是"按下即开始"的手势，若放收拢动画跑（约 220 ms），整个拖动过程都会与收拢、
    /// 推挤复位、封面换树交叠——拖动中收拢、推挤残留、位置基准漂移，正是当初"放大期间索性禁拖"要躲的东西。立即收回
    /// 把封面送回主树、推挤清零、区域扩展收回，拖动于是落在干净的静态布局上。
    /// Called by the host on a drag press: immediately (no animation) reclaims the in-place zoom so the drag can proceed.
    ///
    /// The reclaim must be <b>synchronous and animation-free</b>. Dragging begins on the press, so letting the ~220 ms collapse
    /// run would overlap the whole drag with the collapse, the push reset, and the artwork re-parenting — collapsing mid-drag,
    /// leftover pushes, drifting position bases: precisely what once made "no dragging while zoomed" look attractive. The
    /// immediate reclaim returns the cover to the main tree, zeroes the push, and retracts the region extension, leaving the
    /// drag on a clean, static layout.
    /// </summary>
    public void CollapseArtworkZoomForDrag()
    {
        if (!IsArtworkZoomActive)
            return;

        // 复用 Unloaded 那条"不等一帧"的收回：缩放归位 + 立即拆 Popup（见 HideArtworkHoverZoom）。
        // Reuse the "cannot wait a frame" reclaim the Unloaded path uses: reset the scale and tear the Popup down at once
        // (see HideArtworkHoverZoom).
        HideArtworkHoverZoom(immediate: true);
    }

    /// <summary>把最新快照同步进大图预览：封面源、播放/暂停符号与显隐门禁。暂停语言与主程序封面格一致——图像减暗、
    /// 封面主色双杠、不叠遮罩；播放中才是遮罩+白色双杠（见 UpdateSongInfo 的暂停分支）。
    /// Syncs the latest snapshot into the large preview: artwork source, play/pause glyph, and its gates. The paused language matches
    /// the main program's artwork slot — a dimmed image, bars in the artwork's dominant colour, no scrim; the scrim with white bars
    /// belongs to playing (see UpdateSongInfo's paused branch).</summary>
    private void RefreshArtworkHoverPreviewContent()
    {
        if (_artworkHoverPreviewImage is null)
            return;

        _artworkHoverPreviewImage.Source = _snapshot.Artwork;
        if (_artworkHoverPreview?.Child is Border root)
        {
            // 与通知和完整层共用框比例上限；极端比例或未知尺寸由 Uniform 留白，保留整张封面。
            var artwork = _snapshot.Artwork as BitmapSource;
            root.Width = ArtworkBoxPolicy.Resolve(ArtworkHoverPreviewHeightDip,
                artwork?.PixelWidth ?? 0, artwork?.PixelHeight ?? 0).Width;
        }
        _artworkHoverPreviewImage.Opacity = _isPaused ? ArtworkPausedDimOpacity : 1.0;
        var showGlyph = _canPlayPause && ResolveArtworkClickAction() == PlayerClickAction.TogglePlayPause;
        if (_artworkHoverPreviewScrim is not null)
        {
            _artworkHoverPreviewScrim.Visibility = showGlyph ? Visibility.Visible : Visibility.Collapsed;
            // 暂停时不叠遮罩：主程序此时是"遮罩封面 + 双杠"，再压一层灰会把那张遮罩糊成一片。
            // No scrim while paused: the main program shows a "scrimmed cover + bars" there, and a grey wash would muddy that scrim.
            _artworkHoverPreviewScrim.Background = _isPaused
                ? Brushes.Transparent
                : new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0));
        }
        if (showGlyph && _artworkHoverPreviewIcon is not null)
        {
            _artworkHoverPreviewIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Pause24;
            _artworkHoverPreviewIcon.Foreground = _isPaused
                ? SongImagePlaceholder.Foreground ?? Brushes.White
                : Brushes.White;
        }
    }

    /// <summary>封面这一格当前绑定的点击动作；门禁与提示符号都由它决定。/ The click action currently bound to the artwork slot; it gates the hint and picks its glyph.</summary>
    private PlayerClickAction ResolveArtworkClickAction() =>
        _currentMode == WindowMode.Taskbar
            ? PlayerClickBindingPolicy.Resolve(SettingsManager.Current.Interaction, artwork: true)
            : PlayerClickAction.TogglePlayPause;

    /// <summary>
    /// 封面悬停的放大与播放/暂停提示，三种模式由 <c>ArtworkHoverMode</c> 选择：默认在栏内放大 1.1 倍；原地放大（Zoom）把
    /// 封面元素本体移进透明 Popup、底部锚定放大 2 倍、只往上生长，同时把右侧内容右移同样的放大量；大图模式（Preview）
    /// 经独立 Popup 把放大的封面画到任务栏上方、突出任务栏。提示图标与主程序的封面格同一套语言：播放中显示遮罩+暂停
    /// 双杠，已暂停时封面格本身就是"双杠+遮罩"（UpdateSongInfo 的暂停分支），不再叠加会重影的提示层；点击绑定被用户
    /// 改成其它动作时不显示图标，只保留放大。无媒体时这一格是快速启动音符，整体维持原交互。
    /// Artwork hover zoom plus the play/pause hint, three modes chosen by <c>ArtworkHoverMode</c>: the default zooms 1.1× inside the bar;
    /// the in-place zoom (Zoom) moves the artwork element itself into a transparent Popup, anchored at its bottom and grown 2× upward
    /// only, while the right-hand content shifts right by the same amount; the large mode (Preview) renders the enlarged cover through
    /// its own Popup above the taskbar, popping out beyond the bar. The hint glyph speaks the same language as the main program's
    /// artwork slot: a scrim plus the pause bars while playing, and when paused the slot itself already reads "bars over a scrimmed
    /// cover" (UpdateSongInfo's paused branch) so no hint layer is stacked to double the glyph. With the click binding rebound to
    /// another action no glyph appears and only the zoom remains. Without media the slot is the quick-launch note and stays as is.
    /// </summary>
    private void AnimateArtworkHover(bool isHovered, bool immediate = false)
    {
        var hoverMode = SettingsManager.Current.Interaction.ArtworkHoverMode;
        var hoverEligible = isHovered && CanUseTaskbarComponentHover() && _snapshot.Artwork is not null;
        var previewActive = hoverEligible && hoverMode == ArtworkHoverMode.Preview;
        var zoomActive = hoverEligible && hoverMode == ArtworkHoverMode.Zoom;
        var inlineZoomActive = hoverEligible && hoverMode == ArtworkHoverMode.Off;
        var hintActive = (inlineZoomActive || zoomActive) && !_isPaused && _canPlayPause
            && ResolveArtworkClickAction() == PlayerClickAction.TogglePlayPause;
        // 快照更新（换歌、播放状态翻转）据此在悬停期间刷新提示：暂停↔播放的翻转在任何悬停形态下都可能发生。
        // Snapshot updates (track change, playback flip) refresh the presentation off this flag: a pause/play flip can happen
        // under any hover shape.
        _artworkHoverHintActive = hoverEligible;

        if (previewActive)
            ShowArtworkHoverPreview(immediate);
        else
            HideArtworkHoverPreview();

        var zoomPopupWasOpen = _artworkHoverZoomPopup?.IsOpen == true;
        // 收拢已在途（Popup 开着、收拢标记仍在、且本次要走动画）：目标态与本次请求完全一致，不再重挂收拢动画。
        // 重挂会把正在播放的收拢动画整个替换成新动画——时长从头计，收拢末段被反复续期，看起来就是"卡住不动"；
        // 被顶掉的旧动画还可能误发 Completed 提前收回。重复的 MouseLeave 复核与每次左键抬起后的 Resync 都会走到
        // 这里，都是同一目标态的重复请求。immediate 与关闭过渡的路径不受此守卫，照常强制收回。
        // A collapse already in flight (Popup open, the collapse flag still set, and this call takes the animated path):
        // the target state already matches the request, so the collapse animation is NOT re-armed. Re-arming replaces the
        // running animation outright — the duration restarts from full, the collapse's tail gets endlessly extended, and it
        // reads as "stuck"; the displaced animation can also misfire its Completed into a premature reclaim. Both the
        // repeated leave re-checks and the post-release Resync land here as duplicate requests for the same end-state. The
        // immediate and transitions-off paths bypass this guard and force-reclaim as usual.
        var zoomCollapseInFlight = zoomPopupWasOpen && _artworkZoomCollapsing && !immediate && CurrentMotion.UseTransitions;
        // 顺序有意义：激活先把元素送进 Popup 再播生长动画；收回先在 Popup 里平滑收拢（HideArtworkHoverZoom 只做标记），
        // 收拢动画结束后才由 Completed 把元素送回主树——盖上去与缩回来的每一帧都在指针眼前连续播放。
        // The order matters: activating moves the element into the Popup first and then plays the growth; reclaiming collapses
        // smoothly inside the Popup first (HideArtworkHoverZoom only marks), and only after the collapse does Completed return
        // the element to the main tree — every frame of both the cover and the uncover plays continuously under the pointer.
        if (zoomActive)
            ShowArtworkHoverZoom();
        else if (!zoomCollapseInFlight)
            HideArtworkHoverZoom(immediate);

        if (hintActive)
        {
            SongImageHoverHintIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Pause24;
            // 提示层在占位图标之上，两者同时可见会叠出重影；提示激活期间让占位图标让位。
            // The hint layer sits above the placeholder glyph and the two together double up; the placeholder yields while the hint is active.
            SongImagePlaceholder.Visibility = Visibility.Collapsed;
        }
        else
        {
            RestoreArtworkPlaceholderVisibility();
        }

        if (zoomActive || zoomPopupWasOpen)
        {
            // 原地放大走自己的动画路径：生长与收拢都在 Popup 里，右侧推挤同曲线进退（见 AnimateArtworkZoom）。
            // 在途收拢到此为止：推挤与提示的退场动画正由那一次收拢带着走向同一目标，重挂只会重置时钟。
            // The in-place zoom takes its own animation path: growth and collapse both play inside the Popup while the
            // right-hand push slides on the same curve (see AnimateArtworkZoom). A collapse already in flight stops
            // here: that collapse is already walking the push and the hint to the same targets, and re-arming would
            // only reset the clock.
            if (!zoomActive && zoomCollapseInFlight)
                return;
            AnimateArtworkZoom(zoomActive, immediate, hintActive ? 1 : 0);
            return;
        }

        var motion = CurrentMotion;
        var scale = inlineZoomActive ? ArtworkHoverScaleFactor : 1.0;
        if (immediate || !motion.UseTransitions)
        {
            SongImageScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            SongImageScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            SongImageScale.ScaleX = scale;
            SongImageScale.ScaleY = scale;
            SetArtworkHoverHintOpacity(hintActive ? 1 : 0);
            return;
        }

        var duration = motion.StandardDuration;
        var easing = hintActive || inlineZoomActive || zoomActive ? CreateEaseOut() : CreateEaseInOut();
        SongImageScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation
        {
            To = scale,
            Duration = duration,
            EasingFunction = easing
        });
        SongImageScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation
        {
            To = scale,
            Duration = duration,
            EasingFunction = easing
        });
        SongImageHoverHint.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
        {
            To = hintActive ? 1 : 0,
            Duration = duration,
            EasingFunction = easing
        });
    }

    /// <summary>
    /// 悬停期间的快照刷新（进度轮询、换歌、播放状态翻转、悬停配色变化）只更新提示的<b>呈现</b>——符号、门禁与
    /// 大图预览内容——绝不重跑缩放动画：AnimateArtworkHover 的 immediate 路径会把生长中的封面瞬间拉到目标倍率，
    /// 看起来像"以极快速度放到最大"。原地放大与大图预览承载的都是真实元素或实时刷新的内容，动画状态无需介入。
    /// Snapshot refreshes while hovering (progress polls, track changes, playback flips, hover palette changes) update only the
    /// hint's <b>presentation</b> — glyph, gates, and the large preview's content — and never replay the zoom animation: the
    /// immediate path of AnimateArtworkHover teleports a still-growing cover to its target factor, reading as an ultra-fast snap
    /// to full size. Both the in-place zoom and the large preview carry live content, so no animation state needs touching.
    /// </summary>
    private void RefreshArtworkHoverPresentation()
    {
        if (_artworkHoverPreview?.IsOpen == true)
        {
            RefreshArtworkHoverPreviewContent();
            return;
        }

        var hoverMode = SettingsManager.Current.Interaction.ArtworkHoverMode;
        // 门禁用 IsPointerOverArtwork（屏幕几何）而非 IsMouseOver：封面的悬停快照刷新可能恰逢陈旧捕获存续——
        // 那时 IsMouseOver 谎报 false，正在放大的封面会被一次普通的换歌/暂停刷新误收。其余组件不换树，无需此防。
        // The gate reads IsPointerOverArtwork (screen geometry) rather than IsMouseOver: a hover-time snapshot refresh can land
        // while a stale capture lingers — IsMouseOver then lies false and an enlarged cover gets wrongly collapsed by an
        // ordinary track change or pause flip. The other widgets never re-parent, so they need no such guard.
        var hoverEligible = IsPointerOverArtwork() && CanUseTaskbarComponentHover() && _snapshot.Artwork is not null;
        var inlineZoomActive = hoverEligible && hoverMode == ArtworkHoverMode.Off;
        var zoomActive = hoverEligible && hoverMode == ArtworkHoverMode.Zoom;
        var hintActive = (inlineZoomActive || zoomActive) && !_isPaused && _canPlayPause
            && ResolveArtworkClickAction() == PlayerClickAction.TogglePlayPause;

        if (hintActive)
        {
            SongImageHoverHintIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Pause24;
            // 提示层在占位图标之上，两者同时可见会叠出重影；提示激活期间让占位图标让位。
            // The hint layer sits above the placeholder glyph and the two together double up; the placeholder yields while the hint is active.
            SongImagePlaceholder.Visibility = Visibility.Collapsed;
            SetArtworkHoverHintOpacity(1);
        }
        else
        {
            RestoreArtworkPlaceholderVisibility();
            SetArtworkHoverHintOpacity(0);
        }
    }

    /// <summary>
    /// 给悬停提示层落一个确定的 Opacity：<b>先摘掉动画再赋值</b>——动画一旦在属性上，本地赋值一律被动画值压过。
    /// 放大动画（<see cref="AnimateArtworkZoom"/> 的 hintTargetOpacity）会给这一层挂动画，左键抬起后的兜底重同步
    /// （<see cref="ResyncArtworkHoverFromPointer"/>）还会按"抬起那一刻"的播放状态把它重挂一遍；不摘就会把提示层卡在那个旧
    /// 目标上——表现为"放大动画途中点暂停，白色双杠关不掉、压在取色的占位双杠之上"（提示层在上，看到的自然是白的），
    /// 或反过来的"恢复播放后提示层长不出来"。凡是给这一层落确定值的地方都走这里（挂动画不算落值，另算），"先摘动画"于是
    /// 不可能被漏掉。
    /// Sets a definite Opacity on the hover hint layer, detaching any animation <b>first</b>: while one owns the property a local
    /// assignment is always overridden by the animated value. The zoom animation (<see cref="AnimateArtworkZoom"/>'s
    /// hintTargetOpacity) arms one on this layer, and the post-release re-sync (<see cref="ResyncArtworkHoverFromPointer"/>) re-arms
    /// it from the playback state as of the release — leaving it attached strands the layer at that stale target, which shows up as
    /// "a pause flip mid-zoom cannot turn the white bars off, so they sit on top of the tinted placeholder bars (the hint layer is
    /// above, so white is what shows)", or the mirror case "the hint never grows back after playback resumes". Every place that
    /// lands a definite value on this layer goes through here (arming an animation is a different act, not a landing), so the
    /// detach can never be skipped.
    /// </summary>
    private void SetArtworkHoverHintOpacity(double opacity)
    {
        SongImageHoverHint.BeginAnimation(UIElement.OpacityProperty, null);
        SongImageHoverHint.Opacity = opacity;
    }

    /// <summary>按当前播放状态恢复封面占位符的显隐，语义与 UpdateSongInfo 的封面分支一致。
    /// Restores the artwork placeholder visibility from the current playback state, matching UpdateSongInfo's artwork branch.</summary>
    private void RestoreArtworkPlaceholderVisibility()
    {
        if (!_isConnected || _snapshot.Artwork is null)
            SongImagePlaceholder.Visibility = Visibility.Visible;
        else if (_isPaused)
            SongImagePlaceholder.Visibility = Visibility.Visible;
        else
            SongImagePlaceholder.Visibility = Visibility.Collapsed;
    }

    private void RefreshTaskbarHoverAppearance()
    {
        // 封面的悬停态用 IsPointerOverArtwork（屏幕几何，捕获免疫）判定，理由同 RefreshArtworkHoverPresentation：
        // 唯有封面会被换树进 Popup，其余组件住在主树里，IsMouseOver 对它们始终诚实。
        // The artwork's hover state comes from IsPointerOverArtwork (screen geometry, capture-immune), same reasoning as in
        // RefreshArtworkHoverPresentation: only the artwork ever re-parents into its Popup, while the other widgets live in the
        // main tree where IsMouseOver stays honest.
        AnimateComponentHover(SongImageHoverOverlay, IsPointerOverArtwork(), immediate: true);
        RefreshArtworkHoverPresentation();
        AnimateComponentHover(SongInfoHoverOverlay, IsTextSurfaceHovered, immediate: true);
        AnimateComponentHover(TaskbarSpectrumHoverSurface, TaskbarSpectrumHoverSurface.IsMouseOver, immediate: true);
        AnimateComponentHover(TaskbarPerformanceHoverSurface, TaskbarPerformanceHoverSurface.IsMouseOver, immediate: true);
        AnimateComponentHover(TaskbarOutputDeviceHoverSurface, TaskbarOutputDeviceHoverSurface.IsMouseOver, immediate: true);
        AnimateComponentHover(TaskbarVolumeHoverSurface, TaskbarVolumeHoverSurface.IsMouseOver, immediate: true);
    }

    private void SongImageBorder_MouseEnter(object sender, MouseEventArgs e)
    {
        AnimateComponentHover(SongImageHoverOverlay, true);
        // 进事件也以原格几何为准：指针可以从侧面落进"放大后的轮廓、但已出原格"的那一圈，此时不该长开——
        // 长开会立刻被悬停区轮询收回，两者拉成永不停的收/放振荡。1× 静止态下指针刚进入元素，判定必然为真，
        // 常规悬停不受影响。
        // The enter event keys off the original-slot geometry too: the pointer can drop in from the side into the ring
        // "inside the enlarged outline but outside the original slot", where a grow would be collapsed again by the very next
        // hover-zone tick — the two would drag each other into an endless grow/collapse oscillation. At rest (1×) the pointer
        // has just entered the element, so the verdict is true and ordinary hovering is unaffected.
        AnimateArtworkHover(IsPointerOverArtwork());
    }

    private void SongImageBorder_MouseLeave(object sender, MouseEventArgs e)
    {
        AnimateComponentHover(SongImageHoverOverlay, false);
        if (_artworkHoverZoomPopup?.IsOpen == true)
        {
            // 原地放大期间不立即收起：换树本身会丢一次命中（见 VerifyArtworkZoomLeave），延迟判定后由它收口。
            // Do not collapse at once during the in-place zoom: the re-parent itself drops the hit (see VerifyArtworkZoomLeave),
            // which settles the decision after the delay.
            VerifyArtworkZoomLeave();
        }
        else
        {
            AnimateArtworkHover(false);
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
        AnimateDirectFullPanelHandle(true);
        _hoverCloseTimer.Stop();
        _hoverOpenTimer.Stop();
        if (SettingsManager.Current.TaskbarExperience.HoverLayerEnabled)
            _hoverOpenTimer.Start();
    }

    private void SongInfoStackPanel_MouseLeave(object sender, MouseEventArgs e)
    {
        _hoverOpenTimer.Stop();
        ApplyMarqueeLayout(Math.Max(0, SongInfoStackPanel.Width));
        if (!_isTaskbarHoverVisible)
        {
            if (!TaskbarDirectFullPanelHandle.IsMouseOver)
            {
                AnimateComponentHover(SongInfoHoverOverlay, false);
                AnimateDirectFullPanelHandle(false);
            }
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

    private void TaskbarDirectFullPanelHandle_MouseEnter(object sender, MouseEventArgs e)
    {
        if (!CanShowDirectFullPanelHandle())
            return;
        AnimateComponentHover(SongInfoHoverOverlay, true);
        AnimateDirectFullPanelHandle(true);
    }

    private void TaskbarDirectFullPanelHandle_MouseLeave(object sender, MouseEventArgs e)
    {
        if (SongInfoStackPanel.IsMouseOver)
            return;
        AnimateComponentHover(SongInfoHoverOverlay, false);
        AnimateDirectFullPanelHandle(false);
    }

    private void ShowTaskbarHoverLayer()
    {
        // 宿主正在跟随任务栏动画时不开悬停层：展开会给整块文字区装上 BlurEffect 并播一段裁剪动画，
        // 这两样都与 Shell 的任务栏动画抢同一条合成管线，正是"触边收起时卡顿"里最贵的那一笔。
        // The hover layer is not opened while the host is following the taskbar animation: revealing it installs a BlurEffect on the whole text area and
        // plays a clip animation, and both compete for the same compositing pipeline as the Shell's taskbar animation — the most expensive part of the
        // jank seen when the taskbar hides right after a hover.
        if (_isHostVisibilitySuspended)
            return;

        if (!_isConnected || _currentMode != WindowMode.Taskbar || _isVertical ||
            !SettingsManager.Current.TaskbarExperience.HoverLayerEnabled ||
            (!SongInfoStackPanel.IsMouseOver && !HoverRevealHost.IsMouseOver))
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
        HoverRevealClip.BeginAnimation(RectangleGeometry.RectProperty, null);
        var currentWidth = Math.Clamp(HoverRevealClip.Rect.Width, 0, targetWidth);
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
            animation.Completed += (_, _) => FinishTaskbarHoverLayerHide();
        HoverRevealClip.BeginAnimation(RectangleGeometry.RectProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void HideTaskbarHoverLayer(bool immediate = false)
    {
        _hoverOpenTimer.Stop();
        _hoverCloseTimer.Stop();
        _hoverHideFallbackTimer.Stop();
        var keepRegularHover = CanUseTaskbarComponentHover() &&
                               (SongInfoStackPanel.IsMouseOver || TaskbarDirectFullPanelHandle.IsMouseOver);
        if (immediate || HoverRevealHost.Visibility != Visibility.Visible)
        {
            FinishTaskbarHoverLayerHide(immediate);
            AnimateSongInfoCovered(false, immediate: true);
            AnimateComponentHover(SongInfoHoverOverlay, keepRegularHover, immediate);
            AnimateDirectFullPanelHandle(keepRegularHover, immediate: true);
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
        hide.Completed += (_, _) => FinishTaskbarHoverLayerHide();
        HoverRevealClip.BeginAnimation(RectangleGeometry.RectProperty, hide, HandoffBehavior.SnapshotAndReplace);

        // 兜底：动画回调可能永远不来——渲染时钟停走（任务栏自动隐藏、窗口被遮挡）或这次动画被下一次动画顶掉时，
        // 收起动作会卡在半途、悬停层留在屏幕上。计时器只依赖 Dispatcher，因此一定会把状态收干净。
        // Fallback: the animation callback may never arrive — when the render clock stops (auto-hidden taskbar, occluded window) or the
        // animation is replaced by the next one, the collapse freezes halfway and the hover layer stays on screen. The timer only depends
        // on the dispatcher, so the state is always cleaned up.
        _hoverHideFallbackTimer.Interval = CurrentMotion.ExitDuration + HoverHideFallbackMargin;
        _hoverHideFallbackTimer.Start();
    }

    /// <summary>
    /// 把悬停层收干净：裁剪归零、宽度归零、隐藏并取消命中测试。可以重复调用（动画回调与兜底计时器都会走到这里）。
    /// Finishes hiding the hover layer: zeroes the clip and the width, hides it, and drops hit testing. It is safe to call repeatedly,
    /// since both the animation callback and the fallback timer land here.
    /// </summary>
    private void FinishTaskbarHoverLayerHide(bool immediate = false)
    {
        _hoverHideFallbackTimer.Stop();
        HoverRevealClip.BeginAnimation(RectangleGeometry.RectProperty, null);
        HoverRevealHost.Width = 0;
        HoverRevealClip.Rect = TaskbarRevealRect(0);
        HoverRevealHost.Visibility = Visibility.Collapsed;
        HoverRevealHost.IsHitTestVisible = false;
        ApplyMarqueeLayout(Math.Max(0, SongInfoStackPanel.Width));
        _isTaskbarHoverVisible = false;
        if (!SongInfoStackPanel.IsMouseOver && !TaskbarDirectFullPanelHandle.IsMouseOver)
        {
            AnimateComponentHover(SongInfoHoverOverlay, false, immediate);
            AnimateDirectFullPanelHandle(false, immediate);
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
        AnimateDirectFullPanelHandle(false, immediate: true);
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
