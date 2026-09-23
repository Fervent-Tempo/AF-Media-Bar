using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
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

    /// <summary>
    /// 注册一个跑马灯元素，并把它的小数位移变换挂到渲染上。歌词的高亮层作为第二层传入，两层共用同一个变换实例，
    /// 因此小数位移同时作用在底色层与高亮层上，亮区与字形始终保持对齐。
    /// Registers one marquee element and attaches its fractional-offset transform to the render. The lyric highlight layer is passed as
    /// the second layer so both share one transform instance: the fractional offset then applies to both layers and the reveal stays
    /// aligned with the glyphs.
    /// </summary>
    /// <param name="element">被推进的文本元素。/ The advanced text element.</param>
    /// <param name="mirroredLayer">需要一起位移的镜像层，没有则省略。/ Mirrored layer that moves along, omitted when there is none.</param>
    private void AddMarqueeText(TextBlock element, TextBlock? mirroredLayer = null)
    {
        var state = new MarqueeTextState(element);
        element.RenderTransform = state.Transform;
        if (mirroredLayer is not null)
        {
            // 镜像层用自己的变换实例，但每帧写同一个位移：亮区因此与字形一起移动，且不依赖共享 Freezable 的行为。
            // The mirrored layer gets its own transform instance written with the same offset every frame, so the reveal moves with the
            // glyphs without depending on shared-Freezable behaviour.
            state.MirroredTransform = new TranslateTransform();
            mirroredLayer.RenderTransform = state.MirroredTransform;
        }

        _marqueeTexts.Add(state);
    }

    /// <summary>
    /// 立即应用跑马灯与文字宽度。这里 MUST 同步执行而不是投递到 Loaded 优先级：宿主几何刚写完容器宽度，
    /// 紧接着就是"这段文字放不放得下"的答案，中间插入任何一次几何更新都会让这一步落在旧宽度上。
    /// Applies the marquee and the text widths immediately. This must run synchronously instead of being posted at Loaded priority: the
    /// host geometry has just written the container widths and the answer to "does this text fit" follows right after; letting a geometry
    /// update slip in between leaves this step working from the previous width.
    /// </summary>
    /// <param name="availableWidth">文字区的可用宽度（DIP）。/ Available width of the text region in DIP.</param>
    internal void ApplyMarqueeLayout(double availableWidth)
    {
        // 判据是"文字真的超出了可用宽度"，而不是长度模式：跟随内容模式下媒体栏被任务栏安全上限夹住时，
        // 文字同样会超出，此时也必须能推进看全，否则用户只能看到被截断的标题。
        // 后台剪枝期间否决推进：屏幕熄灭时 16 ms 的逐帧推进同样只有功耗没有画面（见 TaskBarMediaControl.ApplyBackgroundPruneLevel）。
        // The criterion is the text actually overflowing its available width rather than the length mode: in follow-content mode the bar
        // is clamped by the taskbar's safe maximum, the text overflows there too, and it has to stay readable as well.
        // Background pruning vetoes advancing for the same reason the display going dark does: a 16 ms frame advance costs power and shows nothing
        // (see TaskBarMediaControl.ApplyBackgroundPruneLevel).
        var enabled = _currentMode == WindowMode.Taskbar &&
                      !_isVertical &&
                      _isConnected &&
                      !IsAdvancePruned &&
                      CurrentMotion.UseContinuousMotion;
        var anyAdvancing = false;
        foreach (var state in _marqueeTexts)
            anyAdvancing |= ConfigureMarqueeText(state, enabled, availableWidth);

        if (!anyAdvancing)
        {
            _marqueeTimer.Stop();
            return;
        }

        if (!_marqueeTimer.IsEnabled)
            _marqueeTimer.Start();
    }

    /// <summary>
    /// 按当前内容与可用宽度决定一个文本元素用哪种方式推进，并把该写回的属性写回。
    ///
    /// 正在按歌词时间轴推进的歌词行用跟随式（只丢弃已唱过的字，已唱段始终是窗口前缀）；擦亮层即使隐藏也沿用同一轨迹。
    /// 其余元素用轮转式（没有"唱到哪"的概念，循环滚动才能反复读到）。
    /// Decides how one text element advances for its current content and available width, and writes back the properties that belong to
    /// that decision.
    ///
    /// A lyric line advancing on its lyric timeline uses the follow mode, which only discards characters that are already sung and keeps
    /// the sung run as a prefix of the window. Hiding the highlight layer keeps this same trajectory. Everything else uses rotation,
    /// which loops because it has no notion of "how far the singing has reached".
    /// </summary>
    /// <param name="state">该元素的推进状态。/ Advance state of that element.</param>
    /// <param name="enabled">当前是否允许推进。/ Whether advancing is currently allowed.</param>
    /// <param name="availableWidth">可用宽度（DIP）。/ Available width in DIP.</param>
    /// <returns>该元素当前是否正在推进。/ Whether that element is currently advancing.</returns>
    private bool ConfigureMarqueeText(MarqueeTextState state, bool enabled, double availableWidth)
    {
        var element = state.Element;
        var available = double.IsFinite(availableWidth) ? Math.Max(0, availableWidth) : 0;
        // 应用写入的才是原文：只有当前文本不是我们上一次写进去的窗口时才重新捕获，否则会把窗口误当原文，
        // 于是每推进一轮内容就被缩掉一截。歌词行的当前文本走记录值——Inlines 下 `Text` 读不回实际内容。
        // Only what the application wrote counts as the content: it is re-captured when the current text is not the window written last
        // time, otherwise the window would be mistaken for the content and the text would shrink once per round. Lyric rows read the
        // recorded value, because `Text` does not read back inline content.
        var currentContent = GetMarqueeContent(element);
        if (!string.Equals(currentContent, state.Window, StringComparison.Ordinal))
            state.Base = currentContent;

        if (!state.Advancing)
            state.Alignment = ResolveConfiguredAlignment(element);

        var measured = string.IsNullOrEmpty(state.Base) ? 0 : MeasureMarqueeWidth(state.Base, element);
        var overflow = enabled && available > 0
            ? TaskbarExperiencePolicy.CalculateMarqueeOverflow(measured, available)
            : 0;
        var following = overflow > 1 && state.Base.Length > 0 && IsFollowMarqueeElement(element) && _lyricTimelineActive;
        // 歌词行的推进属于亮区：暂停时亮区停住，歌词也 MUST NOT 改走轮转自己跑起来（那会让暂停中的歌词一直循环滚动），
        // 此时它按普通裁剪显示整行开头。标题与歌手没有"唱到哪"这种进度，暂停时照常轮转。
        // A lyric row advances with the reveal: while playback is paused the reveal stands still, and the row MUST NOT fall back to rotating
        // on its own, which would keep a paused line scrolling forever; it is then drawn with the regular trimming instead. The title and
        // artist have no such progress, so they keep rotating while paused.
        var lyricFrozenByPause = IsLyricMarqueeElement(element) && _isPaused;
        var advancing = overflow > 1 && state.Base.Length > 0 && !lyricFrozenByPause;
        var key = $"{advancing}|{following}|{available:0.##}|{state.Base}";
        if (!string.Equals(state.Key, key, StringComparison.Ordinal))
        {
            // 内容、宽度、方式或允许状态变化时从头开始，并重新走一遍起读停留。
            // A content, width, mode, or permission change restarts from the head and repeats the lead-in pause.
            state.Key = key;
            state.Position = 0;
            // -1 表示"还没有写过窗口"：下一帧一定会重写窗口文字，不会把上一行的窗口留在屏幕上。
            // A value of minus one means "no window written yet", so the next frame always rewrites the window text instead of leaving the
            // previous line's window on screen.
            state.WindowStart = -1;
            state.OffsetDip = 0;
            state.RevealWidth = 0;
            state.LeadIn = MarqueeTiming.LeadInDuration;
        }

        state.AvailableWidth = available;
        state.Following = following;
        if (!advancing)
        {
            state.Advancing = false;
            state.Position = 0;
            state.WindowStart = -1;
            state.OffsetDip = 0;
            state.RevealWidth = 0;
            state.Window = state.Base;
            state.PrefixWidths = null;
            state.PrefixMeasuredCharacters = 0;
            state.SungPosition = 0;
            state.SungWidthDip = 0;
            state.WindowWidthDip = 0;
            if (!string.Equals(GetMarqueeContent(element), state.Base, StringComparison.Ordinal))
                SetMarqueeContent(element, state.Base);
            // 放得下时保持用户设置的裁剪提示；放不下但当前不允许推进时也一样，省略号至少说明后面还有内容。
            // When it fits, the configured trimming hint stays; the same applies while advancing is not allowed, where the ellipsis at
            // least says that more text follows.
            if (element.TextTrimming != TextTrimming.CharacterEllipsis)
                element.TextTrimming = TextTrimming.CharacterEllipsis;
            if (Math.Abs(element.Width - available) > 0.01)
                element.Width = available;
            if (element.TextAlignment != state.Alignment)
                element.TextAlignment = state.Alignment;
            ApplyMarqueeOffset(state);
            return false;
        }

        state.Advancing = true;
        state.WindowLength = MarqueeRotationPolicy.ResolveWindowLength(state.Base.Length);
        state.ContentWidth = measured;
        if (following)
        {
            // 跟随式的位置、位移与裁剪宽度都按宽度求解，因此这里直接走一帧：擦亮帧（33 ms）可能先于第一帧推进（16 ms）到来，
            // 先算一次可以让头一帧就停在正确的位置上，而不是先显示原文开头。
            // The follow mode solves its position, offset, and clip width by width, so one frame is taken right here: the reveal frame
            // (33 ms) can arrive before the first advance frame (16 ms), and doing it here puts the line at the right place immediately
            // instead of showing the content's head first.
            AdvanceFollow(state);
            return true;
        }

        UpdateMarqueeWindow(state);
        return true;
    }

    /// <summary>
    /// 元素上显示的还是不是我们最后写进去的那个窗口。应用会在两次推进之间改写文字（歌词呈现、标题更新），
    /// 那时必须立刻重写窗口：否则屏幕上的文字会被整行原文顶掉、跳回行首，直到下一次"整数位置跨过"才恢复。
    /// Whether the element still shows the window written last time. The application rewrites the text between frames (lyric presentation,
    /// title updates), and the window has to be rewritten at once: otherwise the whole line replaces it, the text on screen snaps back to the
    /// line's head, and it only recovers at the next integer position crossing.
    /// </summary>
    /// <param name="state">该元素的推进状态。/ Advance state of that element.</param>
    private bool ShowsMarqueeWindow(MarqueeTextState state) =>
        string.Equals(GetMarqueeContent(state.Element), state.Window, StringComparison.Ordinal);

    /// <summary>该元素是否可使用歌词时间轴跟随推进。/ Whether the element can follow the lyric timeline.</summary>
    /// <param name="element">文本元素。/ Text element.</param>
    private bool IsFollowMarqueeElement(TextBlock element) => ReferenceEquals(element, SongLyrics);

    /// <summary>该元素是否是歌词行（两行都算）。/ Whether the element is a lyric row, either row.</summary>
    /// <param name="element">文本元素。/ Text element.</param>
    private bool IsLyricMarqueeElement(TextBlock element) =>
        ReferenceEquals(element, SongLyrics) || ReferenceEquals(element, SongLyricsSecondary);

    /// <summary>
    /// 按当前位置改写窗口：跟随式按亮区求解起点，轮转式按位置轮转字符串。窗口字符串只在整数位置跨过时才变化，
    /// 小数部分由 <see cref="ApplyMarqueeOffset"/> 写进渲染变换。
    /// Rewrites the window for the current position: the follow mode solves its start from the reveal and the rotation mode rotates the
    /// string. The window string only changes when the integer position crosses; the fraction is written into the render transform by
    /// <see cref="ApplyMarqueeOffset"/>.
    /// </summary>
    /// <param name="state">该元素的推进状态。/ Advance state of that element.</param>
    private void UpdateMarqueeWindow(MarqueeTextState state)
    {
        state.WindowStart = state.Following
            ? MarqueeFollowPolicy.SnapStart(
                state.Base,
                (int)Math.Floor(state.Position),
                Math.Max(0, state.Base.Length - 1))
            : MarqueeRotationPolicy.ResolveWindowStart(state.Base, (int)Math.Floor(state.Position));
        if (!state.Following)
        {
            UpdateRotationOffset(state);
        }

        WriteMarqueeWindow(state);
    }

    /// <summary>
    /// 懒测一段文字的前缀宽度表并返回"已经测到第几个字符"，表按需向前长。
    ///
    /// 轮转式按 <c>原文 + 接缝</c> 测（偏移即索引），跟随式按原文测（已唱位置即索引）；两者都从同一张表读出行进速度、
    /// 小数位移与裁剪宽度，因此三者永远一致。换文字、换字号或换字重时整表重测。
    /// Lazily measures the prefix-width table of one text and returns how many characters have been measured, growing on demand.
    ///
    /// The rotation mode measures `content + separator` so the offset is the index, while the follow mode measures the content so the sung
    /// position is the index. Both read the travel speed, the fractional offset, and the clip width from that one table, which keeps the three
    /// consistent. A different text, font size, or font weight re-measures everything.
    /// </summary>
    /// <param name="state">该元素的推进状态。/ Advance state of that element.</param>
    /// <param name="text">要测量的文字。/ The text to measure.</param>
    /// <param name="requiredCharacters">本次至少需要的字符数。/ Characters needed by this step at the very least.</param>
    private int EnsurePrefixWidths(MarqueeTextState state, string text, int requiredCharacters)
    {
        var element = state.Element;
        var textLength = text.Length;
        var gap = ResolveCharacterSpacingDip();
        var valid = state.PrefixWidths is { } cached &&
                    cached.Length == textLength + 1 &&
                    string.Equals(state.PrefixContent, text, StringComparison.Ordinal) &&
                    Math.Abs(state.PrefixFontSize - element.FontSize) < 0.01 &&
                    state.PrefixFontWeight == element.FontWeight &&
                    Equals(state.PrefixFontFamily, element.FontFamily) &&
                    Math.Abs(state.PrefixGap - gap) < 0.001;
        if (!valid)
        {
            state.PrefixWidths = new double[textLength + 1];
            state.PrefixMeasuredCharacters = 0;
            state.PrefixContent = text;
            state.PrefixFontSize = element.FontSize;
            state.PrefixFontWeight = element.FontWeight;
            state.PrefixFontFamily = element.FontFamily;
            state.PrefixGap = gap;
        }

        var widths = state.PrefixWidths!;
        var target = Math.Clamp(requiredCharacters, state.PrefixMeasuredCharacters, textLength);
        for (var index = state.PrefixMeasuredCharacters + 1; index <= target; index++)
            widths[index] = MeasureMarqueeWidth(text[..index], element);

        state.PrefixMeasuredCharacters = target;
        return target;
    }

    /// <summary>
    /// 把一个正在推进的元素写成当前窗口。窗口文字与容器等宽并硬裁：推进期间不需要省略号（字符会依次进入窗口），
    /// 硬裁也让逐字擦亮的裁剪边界与可见字形一一对应。
    /// Writes one advancing element as its current window. The window text is exactly as wide as its container and hard-cut: an ellipsis
    /// is pointless while characters keep entering the window, and a hard cut keeps the reveal clip aligned with the visible glyphs.
    /// </summary>
    /// <param name="state">该元素的推进状态。/ Advance state of that element.</param>
    private void WriteMarqueeWindow(MarqueeTextState state)
    {
        var element = state.Element;
        var window = state.Following
            ? MarqueeFollowPolicy.BuildWindow(state.Base, state.WindowStart)
            : MarqueeRotationPolicy.BuildWindow(state.Base, state.WindowStart);
        state.Window = window;

        if (!string.Equals(GetMarqueeContent(element), state.Window, StringComparison.Ordinal))
            SetMarqueeContent(element, state.Window);

        if (element.TextTrimming != TextTrimming.None)
            element.TextTrimming = TextTrimming.None;

        if (Math.Abs(element.Width - state.AvailableWidth) > 0.01)
            element.Width = state.AvailableWidth;

        // 窗口比容器宽时，居中或右对齐会把开头推出容器，窗口就不再是"从头开始"；推进期间统一按左对齐渲染，
        // 停止推进时由 ApplyMarqueeLayout 按设置恢复用户的对齐方式。
        // When the window is wider than its container, centring or right alignment would push its head outside and the window would no
        // longer read as "starting from the beginning"; while advancing it is rendered left-aligned, and ApplyMarqueeLayout restores the
        // user's alignment from the settings once advancing stops.
        if (element.TextAlignment != TextAlignment.Left)
            element.TextAlignment = TextAlignment.Left;

        state.DevicePixelScale = ResolveDevicePixelScale(element);
        ApplyMarqueeOffset(state);
    }

    /// <summary>该元素所在显示器的 DPI 缩放，用于把小数位移对齐到设备像素；取不到时按 1 处理。/ DPI scale of the display the element is on, used to snap the fractional offset to device pixels, or one when it cannot be read.</summary>
    /// <param name="element">文本元素。/ Text element.</param>
    private static double ResolveDevicePixelScale(Visual element)
    {
        var scale = VisualTreeHelper.GetDpi(element).DpiScaleX;
        return double.IsFinite(scale) && scale > 0 ? scale : 1;
    }

    /// <summary>
    /// 把位置的小数部分写进渲染变换：窗口字符串只在整数位置跨过时改写，小数部分由连续的位移补上，滚动因此是连续的。
    /// 位移与行进速度同出前缀宽度表（`OffsetDip`），因此"这个字已经移出多少"与"这个字有多宽"永远一致，跨字符边界不会跳。
    /// 歌词两层各有一个变换实例，每帧写同一个位移，亮区因此与字形一起移动。
    /// Writes the fractional part of the position into the render transform: the window string is rewritten only when the integer position
    /// crosses, and the fraction becomes a continuous translation, which is what makes the scroll smooth. The offset and the travel speed come
    /// from the same prefix-width table (`OffsetDip`), so "how much of this character has left" always matches "how wide this character is" and
    /// nothing jumps at a character boundary. Each lyric layer has its own transform instance, written with the same offset every frame, so the
    /// reveal moves with the glyphs.
    /// </summary>
    /// <param name="state">该元素的推进状态。/ Advance state of that element.</param>
    private static void ApplyMarqueeOffset(MarqueeTextState state)
    {
        var offset = state.Advancing ? state.OffsetDip : 0;
        if (!double.IsFinite(offset))
            offset = 0;

        // 位移对齐到设备像素：字形压在半个像素上会丢掉 ClearType 而发虚，而每帧一个设备像素的步进在 60 fps 下同样是连续的。
        // Snap the offset to whole device pixels: a glyph sitting on a half pixel loses ClearType and looks soft, while a step of one
        // device pixel per frame is just as continuous at 60 fps.
        offset = SnapToDevicePixel(offset, state.DevicePixelScale);

        WriteMarqueeOffset(state.Transform, offset);
        WriteMarqueeOffset(state.MirroredTransform, offset);
    }

    /// <summary>把位移对齐到设备像素。/ Snaps an offset to whole device pixels.</summary>
    /// <param name="offset">位移（DIP）。/ Offset in DIP.</param>
    /// <param name="scale">该元素所在显示器的 DPI 缩放。/ DPI scale of the display the element is on.</param>
    private static double SnapToDevicePixel(double offset, double scale) =>
        double.IsFinite(scale) && scale > 0 ? Math.Round(offset * scale) / scale : offset;

    /// <summary>把一个变换的位移写成目标值；变换不存在或已经一致时不做任何事。/ Writes one transform's offset, doing nothing when there is no transform or it already matches.</summary>
    /// <param name="transform">目标变换。/ Target transform.</param>
    /// <param name="offset">位移（DIP）。/ Offset in DIP.</param>
    private static void WriteMarqueeOffset(TranslateTransform? transform, double offset)
    {
        if (transform is not null && Math.Abs(transform.X - offset) > 0.01)
            transform.X = offset;
    }

    /// <summary>把位置按窗口长度回绕到 [0, length)。/ Wraps a position into [0, length) by the window length.</summary>
    /// <param name="position">连续位置（字符）。/ Continuous position in characters.</param>
    /// <param name="length">窗口长度。/ Window length.</param>
    private static double WrapPosition(double position, int length)
    {
        if (length <= 0 || !double.IsFinite(position))
            return 0;

        var wrapped = position % length;
        return wrapped < 0 ? wrapped + length : wrapped;
    }

    /// <summary>
    /// 读取按歌词时间轴跟随的主歌词行窗口：窗口内已唱段的裁剪宽度与窗口整体宽度。没有启用跟随时返回 false，
    /// 调用方按整行前缀换算擦亮。
    /// Reads the window of the main lyric line that follows its timeline: the clip width of the sung run inside it and the window's total
    /// width. Returns false when the follow mode is off, in which case the caller converts the reveal from the whole line instead.
    /// </summary>
    /// <param name="sungWidthDip">窗口内已唱段的宽度（DIP）。/ Width of the sung run inside the window, in DIP.</param>
    /// <param name="windowWidthDip">当前窗口的整体宽度（DIP），用于换算字形内缩。/ Total width of the current window in DIP, used to convert the glyph inset.</param>
    /// <param name="contentPosition">原文中已唱到的位置（字符，可含小数）。/ Sung position in the content, in possibly fractional characters.</param>
    /// <param name="contentLength">原文的字符数。/ Content length in characters.</param>
    internal bool TryGetFollowWindow(
        out double sungWidthDip,
        out double windowWidthDip,
        out double contentPosition,
        out int contentLength)
    {
        foreach (var state in _marqueeTexts)
        {
            if (!IsFollowMarqueeElement(state.Element) || !state.Following)
                continue;

            sungWidthDip = state.SungWidthDip;
            windowWidthDip = state.WindowWidthDip;
            contentPosition = state.SungPosition;
            contentLength = state.Base.Length;
            return true;
        }

        sungWidthDip = 0;
        windowWidthDip = 0;
        contentPosition = 0;
        contentLength = 0;
        return false;
    }

    /// <summary>
    /// 按帧推进每个正在推进的元素：起读停留期间保持原位，之后每帧前进
    /// <see cref="MarqueeTiming.CharactersPerFrame"/> 个字符。跟随式每帧按亮区重新求解位置，因此唱得快时窗口跟得快。
    /// Advances every element by one frame: it stays put during the lead-in pause and then moves by
    /// <see cref="MarqueeTiming.CharactersPerFrame"/> characters per frame. The follow mode re-solves its position from the reveal on every
    /// frame, so a fast line scrolls just as fast.
    /// </summary>
    private void AdvanceMarqueeStep()
    {
        var anyAdvancing = false;
        foreach (var state in _marqueeTexts)
        {
            if (!state.Advancing)
                continue;

            anyAdvancing = true;
            // 起读停留只属于轮转式：跟随式的位置由亮区决定，等一拍会让亮区先跑出窗口。
            // The lead-in pause belongs to the rotation mode only: a follow window's position is decided by the reveal, and waiting a beat
            // would let the reveal leave the window first.
            if (!state.Following && state.LeadIn > TimeSpan.Zero)
            {
                state.LeadIn -= MarqueeTiming.FrameInterval;
                if (state.LeadIn < TimeSpan.Zero)
                {
                    state.LeadIn = TimeSpan.Zero;
                }

                continue;
            }

            if (state.Following)
            {
                AdvanceFollow(state);
                continue;
            }

            AdvanceRotation(state);
        }

        if (!anyAdvancing)
            _marqueeTimer.Stop();
    }

    /// <summary>
    /// 轮转式的一帧：**按像素定速**推进。
    ///
    /// 每帧的字符增量 = 每帧像素 ÷ 当前开头字符的步进宽度，因此屏幕上的移动速度恒定；若改成"每个字固定停留多久"，
    /// 宽字就会走得快、窄字走得慢，每跨一个字符边界速度突变一次，看起来就是"一个字一个字地蹦"。
    /// 窗口字符串只在整数位置跨过时改写，小数部分由前缀宽度表换算成位移，与行进速度同出一张表。
    /// One rotation frame, advancing at a **constant pixel speed**.
    ///
    /// The per-frame character delta is the per-frame pixel distance divided by the advance of the character at the window's head, which keeps the
    /// on-screen speed constant. A fixed dwell time per character would instead make wide characters move fast and narrow ones slow, so the speed
    /// would jump at every character boundary and the text would read as hopping one character at a time. The window string is rewritten only when
    /// the integer position crosses, and the fractional part becomes a translation read from the same prefix-width table as the travel speed.
    /// </summary>
    /// <param name="state">该元素的推进状态。/ Advance state of that element.</param>
    private void AdvanceRotation(MarqueeTextState state)
    {
        var source = MarqueeRotationPolicy.BuildSource(state.Base);
        if (source.Length == 0 || state.WindowLength <= 0)
        {
            return;
        }

        // 只需要测到"当前开头字符"再加一个：表随轮转向前长，均摊每跨一个字符测一次。
        // Only the current head character plus one has to be measured: the table grows with the rotation, amortised to one measurement per
        // character crossed.
        var head = MarqueeRotationPolicy.NormalizeOffset((int)Math.Floor(state.Position), state.WindowLength);
        var measured = EnsurePrefixWidths(state, source, Math.Min(source.Length, head + 2));
        var widths = state.PrefixWidths;
        var headAdvance = MarqueeFollowPolicy.ResolveWidthAt(widths, measured, head + 1) -
                          MarqueeFollowPolicy.ResolveWidthAt(widths, measured, head);
        var step = headAdvance > 0.01 ? MarqueeTiming.DipPerFrame / headAdvance : 0;
        state.Position = WrapPosition(state.Position + step, state.WindowLength);
        var windowStart = MarqueeRotationPolicy.ResolveWindowStart(state.Base, (int)Math.Floor(state.Position));
        if (windowStart != state.WindowStart || !ShowsMarqueeWindow(state))
        {
            // 整数位置跨过，或应用改写了文字：改写窗口字符串。
            // The integer position crossed, or the application rewrote the text: rewrite the window string.
            state.WindowStart = windowStart;
            UpdateRotationOffset(state);
            WriteMarqueeWindow(state);
            return;
        }

        UpdateRotationOffset(state);
        ApplyMarqueeOffset(state);
    }

    /// <summary>
    /// 轮转式的位移：位置的小数部分在"当前开头字符"这一段里已经走过多宽。速度与位移同出一张前缀宽度表，
    /// 因此跨字符边界时位移正好接上（差值为零），不会出现肉眼可见的跳动。
    /// The rotation offset: how much of the head character's advance the fractional position has already covered. The speed and the offset come
    /// from the same prefix-width table, so the offset lines up exactly across a character boundary with no visible jump.
    /// </summary>
    /// <param name="state">该元素的推进状态。/ Advance state of that element.</param>
    private void UpdateRotationOffset(MarqueeTextState state)
    {
        var source = MarqueeRotationPolicy.BuildSource(state.Base);
        if (source.Length == 0)
        {
            state.OffsetDip = 0;
            return;
        }

        var required = Math.Min(source.Length, Math.Max(state.WindowStart, (int)Math.Floor(state.Position)) + 2);
        var measured = EnsurePrefixWidths(state, source, required);
        state.OffsetDip = -(MarqueeFollowPolicy.ResolveWidthAt(state.PrefixWidths, measured, state.Position) -
                            MarqueeFollowPolicy.ResolveWidthAt(state.PrefixWidths, measured, state.WindowStart));
    }

    /// <summary>
    /// 跟随式的一帧：按**宽度**求解窗口位置——已唱段有多宽，就把窗口左移到"亮区停在容器的 80% 处"。
    ///
    /// 位置因此只取决于亮区（对亮区单调），不会与"窗口内还能放下几个字"互相追赶；那正是混排长歌词抖动的原因。
    /// 位移与裁剪宽度同出一张前缀宽度表，所以亮区边界、字形与窗口位置三者始终一致。
    /// One follow frame: the window position is solved by **width** — however wide the sung run is, the window slides left until the reveal
    /// rests at 80% of the container.
    ///
    /// The position therefore depends only on the reveal (it is monotone in it) and never chases "how many characters still fit", which is
    /// exactly what used to make long mixed-width lyric lines jitter. The offset and the clip width come from the same prefix-width table,
    /// so the reveal edge, the glyphs, and the window position always agree.
    /// </summary>
    /// <param name="state">该元素的推进状态。/ Advance state of that element.</param>
    private void AdvanceFollow(MarqueeTextState state)
    {
        var contentLength = state.Base.Length;
        var progress = ResolveCurrentLyricProgress();
        var sung = progress is null ? 0 : MarqueeFollowPolicy.ResolveSungPosition(progress.Value, contentLength);
        state.SungPosition = sung;

        // 只需要测到"已经唱到的那一个字"：表随演唱向前长，均摊每唱一个字测一次。
        // Only the characters sung so far have to be measured: the table grows with the singing, which amortises to one measurement per
        // sung character.
        var measured = EnsurePrefixWidths(state, state.Base, (int)Math.Ceiling(sung) + 1);
        var widths = state.PrefixWidths;

        // 播放位置来自播放器上报的时间轴，很多播放器按整秒上报，于是外推值每秒会被修正回退一次。
        // 呈现层只承认前进，并且每帧最多追赶一小段，因此整块文字不会跟着上报的回退往回跳，也不会被一次台阶整块推走。
        // The playback position comes from the player's reported timeline, and many players report whole seconds, so the extrapolated value is
        // corrected backwards once per second. The presentation only accepts forward motion and catches up by a bounded amount per frame, so
        // the line never jumps backwards with a report and never gets pushed a whole step at once either.
        var rawWidth = MarqueeFollowPolicy.ResolveWidthAt(widths, measured, sung);
        state.RevealWidth = MarqueeFollowPolicy.ResolveRevealWidth(
            state.RevealWidth,
            rawWidth,
            MarqueeFollowPolicy.MaximumRevealAdvanceEm * state.Element.FontSize);
        var revealedWidth = state.RevealWidth;

        var dropped = MarqueeFollowPolicy.ResolveDroppedWidth(revealedWidth, state.AvailableWidth);
        state.Position = MarqueeFollowPolicy.ResolvePositionAtWidth(widths, measured, dropped);

        var windowStart = MarqueeFollowPolicy.SnapStart(
            state.Base,
            (int)Math.Floor(state.Position),
            Math.Max(0, contentLength - 1));
        var windowLeftWidth = MarqueeFollowPolicy.ResolveWidthAt(widths, measured, windowStart);
        state.OffsetDip = -(MarqueeFollowPolicy.ResolveWidthAt(widths, measured, state.Position) - windowLeftWidth);
        state.SungWidthDip = revealedWidth - windowLeftWidth;
        state.WindowWidthDip = state.ContentWidth - windowLeftWidth;

        if (windowStart != state.WindowStart || !ShowsMarqueeWindow(state))
        {
            state.WindowStart = windowStart;
            WriteMarqueeWindow(state);
            return;
        }

        ApplyMarqueeOffset(state);
    }

    /// <summary>
    /// 停止推进并把每个元素还原成应用写入的原文、取消位移，同时恢复用户设置的对齐方式。悬停进入、切换模式与卸载都走这里，
    /// 因为"还原"必须与"停止计时器"是同一件事，否则屏幕上会留下一段被推进过的文字。
    /// Stops advancing, restores every element to the content the application wrote, clears the offset, and restores the configured
    /// alignment. Hover entry, mode switches, and unloading all go through here, because restoring and stopping the timer have to be one
    /// operation — otherwise an advanced window would be left on screen.
    /// </summary>
    private void StopMarqueeAnimations()
    {
        _marqueeTimer.Stop();
        foreach (var state in _marqueeTexts)
        {
            state.Key = string.Empty;
            state.Position = 0;
            state.WindowStart = -1;
            state.OffsetDip = 0;
            state.RevealWidth = 0;
            state.LeadIn = MarqueeTiming.LeadInDuration;
            state.Advancing = false;
            state.Following = false;
            state.Window = state.Base;
            state.PrefixWidths = null;
            state.PrefixMeasuredCharacters = 0;
            state.SungPosition = 0;
            state.SungWidthDip = 0;
            state.WindowWidthDip = 0;
            if (!string.Equals(GetMarqueeContent(state.Element), state.Base, StringComparison.Ordinal))
                SetMarqueeContent(state.Element, state.Base);
            if (state.Element.TextAlignment != state.Alignment)
                state.Element.TextAlignment = state.Alignment;
            ApplyMarqueeOffset(state);
        }
    }

    /// <summary>
    /// 把设置里的对齐方式写到元素上，但**正在推进**的元素除外：推进期间窗口必须保持左对齐，否则开头会被推出容器，
    /// 而停止推进时跑马灯自己会按设置恢复。悬停进入会重跑一次体验设置，因此这里必须让路。
    /// Writes the configured alignment onto an element, except while that element is advancing: an advancing window has to stay
    /// left-aligned or its head leaves the container, and the marquee restores the configured value once it stops. Hover entry re-runs the
    /// experience settings, so this has to yield.
    /// </summary>
    /// <param name="element">文本元素。/ Text element.</param>
    /// <param name="alignment">设置里的对齐方式。/ Alignment from the settings.</param>
    private void ApplyConfiguredTextAlignment(TextBlock element, TextAlignment alignment)
    {
        foreach (var state in _marqueeTexts)
        {
            if (ReferenceEquals(state.Element, element) && state.Advancing)
                return;
        }

        if (element.TextAlignment != alignment)
            element.TextAlignment = alignment;
    }

    /// <summary>
    /// 该元素在设置里配置的对齐方式。推进期间渲染按左对齐，结束后必须回到设置里的值，
    /// 因此这里读设置而不是读元素（元素上那个值可能已经被推进覆盖）。
    /// The alignment configured in the settings for that element. Advancing renders left-aligned and has to return to the configured value
    /// afterwards, so this reads the settings rather than the element, whose own value may already have been overwritten.
    /// </summary>
    /// <param name="element">文本元素。/ Text element.</param>
    private TextAlignment ResolveConfiguredAlignment(TextBlock element)
    {
        if (ReferenceEquals(element, SongLyrics) ||
            ReferenceEquals(element, SongLyricsHighlight) ||
            ReferenceEquals(element, SongLyricsSecondary))
        {
            return SettingsManager.Current.LyricsTextAlignment switch
            {
                LyricsTextAlignment.Left => TextAlignment.Left,
                LyricsTextAlignment.Right => TextAlignment.Right,
                _ => TextAlignment.Center
            };
        }

        return SettingsManager.Current.TaskbarExperience.Normalize().MediaTextAlignment switch
        {
            TaskbarMediaTextAlignment.Center => TextAlignment.Center,
            TaskbarMediaTextAlignment.Right => TextAlignment.Right,
            _ => TextAlignment.Left
        };
    }

    /// <summary>
    /// 一个文本元素的推进状态：应用写入的原文、我们最后写进去的窗口、连续位置与窗口度量缓存。
    /// Advance state of one text element: the content the application wrote, the window written last time, the continuous position, and
    /// the window's cached metrics.
    /// </summary>
    private sealed class MarqueeTextState(TextBlock element)
    {
        /// <summary>被推进的文本元素。/ The advanced text element.</summary>
        public TextBlock Element { get; } = element;

        /// <summary>小数位移用的渲染变换；歌词两层的镜像层另持一个实例，两个实例每帧写同一个值。 / Render transform carrying the fractional offset; a lyric line's mirrored layer has its own instance and both are written with the same value every frame.</summary>
        public TranslateTransform Transform { get; } = new();

        /// <summary>需要跟着一起位移的镜像层变换（歌词高亮层），没有则为 null。/ Transform of the mirrored layer that moves along, the lyric highlight layer, or null when there is none.</summary>
        public TranslateTransform? MirroredTransform { get; set; }

        /// <summary>应用写入的原文。/ Content written by the application.</summary>
        public string Base { get; set; } = string.Empty;

        /// <summary>我们最后写进去的窗口文字。/ Window text written last time.</summary>
        public string Window { get; set; } = string.Empty;

        /// <summary>决定是否需要重启推进的一致性键。/ Consistency key deciding whether advancing has to restart.</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>连续位置（字符）：轮转式按窗口长度回绕，跟随式是原文里的起点。/ Continuous position in characters: wrapped by the window length in the rotation mode and the content start in the follow mode.</summary>
        public double Position { get; set; }

        /// <summary>当前窗口字符串对应的整数位置；-1 表示"还没有写过窗口"。/ Integer position behind the current window string, where minus one means no window written yet.</summary>
        public int WindowStart { get; set; } = -1;

        /// <summary>轮转式窗口的字符数（原文加间隔）。/ Rotation window length in characters: content plus separator.</summary>
        public int WindowLength { get; set; }

        /// <summary>起读停留的剩余时间（只对轮转式有效）。/ Remaining lead-in time, which only the rotation mode uses.</summary>
        public TimeSpan LeadIn { get; set; } = MarqueeTiming.LeadInDuration;

        /// <summary>该元素当前是否正在推进。/ Whether that element is currently advancing.</summary>
        public bool Advancing { get; set; }

        /// <summary>该元素是否使用跟随式。/ Whether that element uses the follow mode.</summary>
        public bool Following { get; set; }

        /// <summary>当前可用宽度（DIP）。/ Current available width in DIP.</summary>
        public double AvailableWidth { get; set; }

        /// <summary>该元素所在显示器的 DPI 缩放，用于把小数位移对齐到设备像素（窗口每次变化时刷新）。/ DPI scale of the display the element is on, used to snap the fractional offset to device pixels; refreshed whenever the window changes.</summary>
        public double DevicePixelScale { get; set; } = 1;

        /// <summary>设置里配置的对齐方式。/ Alignment configured in the settings.</summary>
        public TextAlignment Alignment { get; set; } = TextAlignment.Left;

        /// <summary>
        /// 跟随式用的原文前缀宽度表：第 i 项是前 i 个字符的宽度，随演唱按需向前长（<see cref="PrefixMeasuredCharacters"/> 记录已测到哪）。
        /// 位置、位移与裁剪宽度都从这张表读，因此三者始终一致。
        /// Prefix-width table of the content used by the follow mode: the i-th entry is the width of the first i characters, and it grows on
        /// demand with the singing, with <see cref="PrefixMeasuredCharacters"/> telling how far it has been measured. The position, the
        /// offset, and the clip width are all read from this table, so the three always agree.
        /// </summary>
        public double[]? PrefixWidths { get; set; }

        /// <summary>前缀宽度表已经测过的字符数。/ Characters already measured in the prefix-width table.</summary>
        public int PrefixMeasuredCharacters { get; set; }

        /// <summary>前缀宽度表对应的原文，换行时整表重测。/ Content the prefix-width table belongs to; a new line re-measures everything.</summary>
        public string? PrefixContent { get; set; }

        /// <summary>前缀宽度表对应的字号。/ Font size the prefix-width table was measured with.</summary>
        public double PrefixFontSize { get; set; }

        /// <summary>前缀宽度表对应的字重。/ Font weight the prefix-width table was measured with.</summary>
        public FontWeight PrefixFontWeight { get; set; }

        /// <summary>前缀宽度表对应的字族。/ Font family the prefix-width table was measured with.</summary>
        public FontFamily? PrefixFontFamily { get; set; }

        /// <summary>前缀宽度表对应的字距目标（DIP）；同一个显示串在不同字距下的宽度不同，因此它也是表的一部分。
        /// Target character spacing the prefix-width table was measured with, in DIP; one display string renders at different widths
        /// per gap, so it is part of the table as well.</summary>
        public double PrefixGap { get; set; }

        /// <summary>原文的整体宽度（DIP），用于换算窗口总宽度。/ Total width of the content in DIP, used for the window's overall width.</summary>
        public double ContentWidth { get; set; }

        /// <summary>跟随式的位移（DIP）：窗口位置到整数起点之间的宽度。/ Offset of the follow mode in DIP, which is the width between the exact window position and its integer start.</summary>
        public double OffsetDip { get; set; }

        /// <summary>呈现层使用的已唱宽度（DIP）：只前进，每帧最多追赶一小段，因此播放器上报的回退不会把整块文字带回去。/ Sung width used for presentation, in DIP: it only moves forward and catches up by a bounded amount per frame, so a backwards correction from the player's reported timeline never drags the line back.</summary>
        public double RevealWidth { get; set; }

        /// <summary>跟随式里原文中已唱到的位置（字符，可含小数）。/ Sung position in the content for the follow mode, in possibly fractional characters.</summary>
        public double SungPosition { get; set; }

        /// <summary>跟随式里窗口内已唱段的裁剪宽度（DIP）。/ Clip width of the sung run inside the window, in DIP.</summary>
        public double SungWidthDip { get; set; }

        /// <summary>跟随式里窗口的整体宽度（DIP）。/ Total width of the follow window in DIP.</summary>
        public double WindowWidthDip { get; set; }
    }

    private static double MeasureTextWidth(string text, TextBlock source) =>
        MeasureTextWidthExact(text, source) + 4;

    /// <summary>
    /// 测量文字的自然宽度，不含跑马灯为折返预留的 4 DIP。
    /// Measures the natural text width without the 4 DIP the marquee reserves for its turn-around.
    ///
    /// 逐字擦亮需要这个不带补偿的宽度：裁剪边界按字形实际占用的宽度换算，多出来的 4 DIP 会让擦亮在行尾提前结束。
    /// Syllable highlighting needs the width without that compensation: the clip edge converts glyph width, and the extra 4 DIP
    /// would make the reveal finish before the end of the line.
    /// </summary>
    private static double MeasureTextWidthExact(string text, TextBlock source)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var pixelsPerDip = VisualTreeHelper.GetDpi(source).PixelsPerDip;
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(source.FontFamily, source.FontStyle, source.FontWeight, source.FontStretch),
            source.FontSize,
            Brushes.Transparent,
            pixelsPerDip)
        {
            Trimming = TextTrimming.None
        };
        return formatted.WidthIncludingTrailingWhitespace;
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
                if (SongInfoStackPanel.RenderTransform is TranslateTransform instantTransform)
                {
                    instantTransform.BeginAnimation(TranslateTransform.XProperty, null);
                    instantTransform.X = 0;
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
            var translateTransform = new TranslateTransform();
            SongInfoStackPanel.RenderTransform = translateTransform;
            translateTransform.BeginAnimation(TranslateTransform.XProperty, translateAnimation);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private bool CanUseTaskbarComponentHover() =>
        _isConnected && _currentMode == WindowMode.Taskbar && !_isVertical;

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
        if (isHovered && !CanUseTaskbarComponentHover())
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

    private void RefreshTaskbarHoverAppearance()
    {
        AnimateComponentHover(SongImageHoverOverlay, SongImageBorder.IsMouseOver, immediate: true);
        AnimateComponentHover(SongInfoHoverOverlay, IsTextSurfaceHovered, immediate: true);
        AnimateComponentHover(TaskbarSpectrumHoverSurface, TaskbarSpectrumHoverSurface.IsMouseOver, immediate: true);
        AnimateComponentHover(TaskbarPerformanceHoverSurface, TaskbarPerformanceHoverSurface.IsMouseOver, immediate: true);
        AnimateComponentHover(TaskbarOutputDeviceHoverSurface, TaskbarOutputDeviceHoverSurface.IsMouseOver, immediate: true);
        AnimateComponentHover(TaskbarVolumeHoverSurface, TaskbarVolumeHoverSurface.IsMouseOver, immediate: true);
    }

    private void SongImageBorder_MouseEnter(object sender, MouseEventArgs e) =>
        AnimateComponentHover(SongImageHoverOverlay, true);

    private void SongImageBorder_MouseLeave(object sender, MouseEventArgs e)
    {
        AnimateComponentHover(SongImageHoverOverlay, false);

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

        // 悬停不打断跑马灯：指针移入移出只是用户要操作按钮，标题/歌手/歌词的推进位置与节奏 MUST 继续，
        // 否则每次移入都会从开头重读一遍。
        // Hovering does not interrupt the marquee: moving the pointer in and out only means the user wants a button, and the advance
        // position and pace of the title, artist, and lyrics MUST continue, otherwise every entry restarts the text from its head.
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
        HoverRevealClip.Rect = new Rect(0, 0, currentWidth, HoverRevealHost.Height);
        if (!CurrentMotion.UseTransitions)
        {
            HoverRevealClip.Rect = new Rect(0, 0, targetWidth, HoverRevealHost.Height);
            return;
        }

        var reveal = new RectAnimation
        {
            From = new Rect(0, 0, currentWidth, HoverRevealHost.Height),
            To = new Rect(0, 0, targetWidth, HoverRevealHost.Height),
            Duration = CurrentMotion.PanelDuration,
            EasingFunction = CreateEaseOut()
        };
        HoverRevealClip.BeginAnimation(RectangleGeometry.RectProperty, reveal, HandoffBehavior.SnapshotAndReplace);
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
            To = new Rect(0, 0, 0, HoverRevealHost.Height),
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
        HoverRevealClip.Rect = new Rect(0, 0, 0, HoverRevealHost.Height);
        HoverRevealHost.Visibility = Visibility.Collapsed;
        HoverRevealHost.IsHitTestVisible = false;
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
