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
    /// 注册一个跑马灯元素，并把它的小数位移变换挂到渲染上。
    /// Registers one marquee element and attaches its fractional-offset transform to the render.
    /// </summary>
    /// <param name="element">被推进的文本元素。/ The advanced text element.</param>
    private void AddMarqueeText(TextBlock element)
    {
        var state = new MarqueeTextState(element);
        element.RenderTransform = state.Transform;
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
    /// 按当前内容与可用宽度决定文本是否轮转，并把相关属性写回。
    /// Decides whether the text rotates for its current content and available width, then writes back the relevant properties.
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
        // 于是每推进一轮内容就被缩掉一截。
        // Only what the application wrote counts as the content: it is re-captured when the current text is not the window written last
        // time, otherwise the window would be mistaken for the content and the text would shrink once per round.
        if (!string.Equals(element.Text, state.Window, StringComparison.Ordinal))
            state.Base = element.Text ?? string.Empty;

        if (!state.Advancing)
            state.Alignment = ResolveConfiguredAlignment(element);

        var measured = string.IsNullOrEmpty(state.Base) ? 0 : MeasureTextWidthExact(state.Base, element);
        var overflow = enabled && available > 0
            ? TaskbarExperiencePolicy.CalculateMarqueeOverflow(measured, available)
            : 0;
        var advancing = overflow > 1 && state.Base.Length > 0;
        var key = $"{advancing}|{available:0.##}|{state.Base}";
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
            state.LeadIn = MarqueeTiming.LeadInDuration;
        }

        state.AvailableWidth = available;
        if (!advancing)
        {
            state.Advancing = false;
            state.Position = 0;
            state.WindowStart = -1;
            state.OffsetDip = 0;
            state.Window = state.Base;
            state.PrefixWidths = null;
            state.PrefixMeasuredCharacters = 0;
            if (!string.Equals(element.Text, state.Base, StringComparison.Ordinal))
                element.Text = state.Base;
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
    private static bool ShowsMarqueeWindow(MarqueeTextState state) =>
        string.Equals(state.Element.Text, state.Window, StringComparison.Ordinal);

    /// <summary>
    /// 按当前位置轮转窗口。窗口字符串只在整数位置跨过时才变化，
    /// 小数部分由 <see cref="ApplyMarqueeOffset"/> 写进渲染变换。
    /// Rotates the window for the current position. The window string only changes when the integer position crosses; the fraction is written into the render transform by
    /// <see cref="ApplyMarqueeOffset"/>.
    /// </summary>
    /// <param name="state">该元素的推进状态。/ Advance state of that element.</param>
    private static void UpdateMarqueeWindow(MarqueeTextState state)
    {
        state.WindowStart = MarqueeRotationPolicy.ResolveWindowStart(state.Base, (int)Math.Floor(state.Position));
        UpdateRotationOffset(state);

        WriteMarqueeWindow(state);
    }

    /// <summary>
    /// 懒测一段文字的前缀宽度表并返回"已经测到第几个字符"，表按需向前长。
    ///
    /// 按 <c>原文 + 接缝</c> 测量，行进速度与小数位移都从同一张表读取。换文字、换字号或换字重时整表重测。
    /// Lazily measures the prefix-width table of one text and returns how many characters have been measured, growing on demand.
    ///
    /// It measures `content + separator`; travel speed and fractional offset both come from that table. A different text, font size,
    /// or font weight re-measures everything.
    /// </summary>
    /// <param name="state">该元素的推进状态。/ Advance state of that element.</param>
    /// <param name="text">要测量的文字。/ The text to measure.</param>
    /// <param name="requiredCharacters">本次至少需要的字符数。/ Characters needed by this step at the very least.</param>
    private static int EnsurePrefixWidths(MarqueeTextState state, string text, int requiredCharacters)
    {
        var element = state.Element;
        var textLength = text.Length;
        var valid = state.PrefixWidths is { } cached &&
                    cached.Length == textLength + 1 &&
                    string.Equals(state.PrefixContent, text, StringComparison.Ordinal) &&
                    Math.Abs(state.PrefixFontSize - element.FontSize) < 0.01 &&
                    state.PrefixFontWeight == element.FontWeight &&
                    Equals(state.PrefixFontFamily, element.FontFamily);
        if (!valid)
        {
            state.PrefixWidths = new double[textLength + 1];
            state.PrefixMeasuredCharacters = 0;
            state.PrefixContent = text;
            state.PrefixFontSize = element.FontSize;
            state.PrefixFontWeight = element.FontWeight;
            state.PrefixFontFamily = element.FontFamily;
        }

        var widths = state.PrefixWidths!;
        var target = Math.Clamp(requiredCharacters, state.PrefixMeasuredCharacters, textLength);
        for (var index = state.PrefixMeasuredCharacters + 1; index <= target; index++)
            widths[index] = MeasureTextWidthExact(text[..index], element);

        state.PrefixMeasuredCharacters = target;
        return target;
    }

    /// <summary>
    /// 把一个正在推进的元素写成当前窗口。窗口文字与容器等宽并硬裁：推进期间不需要省略号（字符会依次进入窗口），
    /// 硬裁让滚动窗口边界保持稳定。
    /// Writes one advancing element as its current window. The window text is exactly as wide as its container and hard-cut: an ellipsis
    /// is pointless while characters keep entering the window, and a hard cut keeps the scrolling boundary stable.
    /// </summary>
    /// <param name="state">该元素的推进状态。/ Advance state of that element.</param>
    private static void WriteMarqueeWindow(MarqueeTextState state)
    {
        var element = state.Element;
        var window = MarqueeRotationPolicy.BuildWindow(state.Base, state.WindowStart);
        state.Window = window;

        if (!string.Equals(element.Text, state.Window, StringComparison.Ordinal))
            element.Text = state.Window;

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
    /// 按帧推进每个正在推进的元素：起读停留期间保持原位，之后每帧前进
    /// <see cref="MarqueeTiming.CharactersPerFrame"/> 个字符。
    /// Advances every element by one frame: it stays put during the lead-in pause and then moves by
    /// <see cref="MarqueeTiming.CharactersPerFrame"/> characters per frame.
    /// </summary>
    private void AdvanceMarqueeStep()
    {
        var anyAdvancing = false;
        foreach (var state in _marqueeTexts)
        {
            if (!state.Advancing)
                continue;

            anyAdvancing = true;
            // 每轮从开头停留片刻，再开始连续滚动。
            // Pause briefly at the head of each cycle, then begin continuous scrolling.
            if (state.LeadIn > TimeSpan.Zero)
            {
                state.LeadIn -= MarqueeTiming.FrameInterval;
                if (state.LeadIn < TimeSpan.Zero)
                {
                    state.LeadIn = TimeSpan.Zero;
                }

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
    private static void AdvanceRotation(MarqueeTextState state)
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
        var headAdvance = MarqueeRotationPolicy.ResolveWidthAt(widths, measured, head + 1) -
                          MarqueeRotationPolicy.ResolveWidthAt(widths, measured, head);
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
    private static void UpdateRotationOffset(MarqueeTextState state)
    {
        var source = MarqueeRotationPolicy.BuildSource(state.Base);
        if (source.Length == 0)
        {
            state.OffsetDip = 0;
            return;
        }

        var required = Math.Min(source.Length, Math.Max(state.WindowStart, (int)Math.Floor(state.Position)) + 2);
        var measured = EnsurePrefixWidths(state, source, required);
        state.OffsetDip = -(MarqueeRotationPolicy.ResolveWidthAt(state.PrefixWidths, measured, state.Position) -
                            MarqueeRotationPolicy.ResolveWidthAt(state.PrefixWidths, measured, state.WindowStart));
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
            state.LeadIn = MarqueeTiming.LeadInDuration;
            state.Advancing = false;
            state.Window = state.Base;
            state.PrefixWidths = null;
            state.PrefixMeasuredCharacters = 0;
            if (!string.Equals(state.Element.Text, state.Base, StringComparison.Ordinal))
                state.Element.Text = state.Base;
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

        /// <summary>应用写入的原文。/ Content written by the application.</summary>
        public string Base { get; set; } = string.Empty;

        /// <summary>我们最后写进去的窗口文字。/ Window text written last time.</summary>
        public string Window { get; set; } = string.Empty;

        /// <summary>决定是否需要重启推进的一致性键。/ Consistency key deciding whether advancing has to restart.</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>按窗口长度回绕的连续字符位置。/ Continuous character position wrapped by the window length.</summary>
        public double Position { get; set; }

        /// <summary>当前窗口字符串对应的整数位置；-1 表示"还没有写过窗口"。/ Integer position behind the current window string, where minus one means no window written yet.</summary>
        public int WindowStart { get; set; } = -1;

        /// <summary>轮转式窗口的字符数（原文加间隔）。/ Rotation window length in characters: content plus separator.</summary>
        public int WindowLength { get; set; }

        /// <summary>起读停留的剩余时间。/ Remaining lead-in time.</summary>
        public TimeSpan LeadIn { get; set; } = MarqueeTiming.LeadInDuration;

        /// <summary>该元素当前是否正在推进。/ Whether that element is currently advancing.</summary>
        public bool Advancing { get; set; }

        /// <summary>当前可用宽度（DIP）。/ Current available width in DIP.</summary>
        public double AvailableWidth { get; set; }

        /// <summary>该元素所在显示器的 DPI 缩放，用于把小数位移对齐到设备像素（窗口每次变化时刷新）。/ DPI scale of the display the element is on, used to snap the fractional offset to device pixels; refreshed whenever the window changes.</summary>
        public double DevicePixelScale { get; set; } = 1;

        /// <summary>设置里配置的对齐方式。/ Alignment configured in the settings.</summary>
        public TextAlignment Alignment { get; set; } = TextAlignment.Left;

        /// <summary>
        /// 第 i 项是前 i 个字符宽度的前缀表，按需向前增长。
        /// Prefix-width table whose i-th entry is the width of the first i characters, grown on demand.
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

        /// <summary>窗口位置到整数起点之间的位移（DIP）。/ Offset in DIP between the exact window position and its integer start.</summary>
        public double OffsetDip { get; set; }
    }

    private static double MeasureTextWidth(string text, TextBlock source) =>
        MeasureTextWidthExact(text, source) + 4;

    /// <summary>
    /// 测量文字的自然宽度，不含跑马灯为折返预留的 4 DIP。
    /// Measures the natural text width without the 4 DIP the marquee reserves for its turn-around.
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
