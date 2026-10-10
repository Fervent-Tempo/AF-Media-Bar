// 媒体文字循环的纯时序策略；不拥有动画时钟、控件或设置。
namespace AFMediaBar.Classes.Services;

internal enum MetadataMarqueePhase { HeadHold, ToTail, TailHold, ToHead }
internal readonly record struct MetadataMarqueeCursor(MetadataMarqueePhase Phase, double Offset, TimeSpan HoldRemaining);
internal readonly record struct MetadataMarqueeSegment(MetadataMarqueePhase Phase, TimeSpan Start, TimeSpan End, double From, double To);

internal sealed record MetadataMarqueePlan(double TextWidth, double ViewportWidth, bool Repeat,
    double InitialOffset, IReadOnlyList<MetadataMarqueeSegment> Segments)
{
    internal double Gap => Math.Min(24, ViewportWidth * 0.1);
    internal double Period => TextWidth + Gap;
    internal double Tail => Math.Max(0, TextWidth - ViewportWidth);
    internal TimeSpan Duration => Segments.Count == 0 ? TimeSpan.Zero : Segments[^1].End;

    internal MetadataMarqueeCursor Sample(TimeSpan elapsed)
    {
        var seconds = Math.Max(0, elapsed.TotalSeconds);
        if (Repeat && Duration > TimeSpan.Zero) seconds %= Duration.TotalSeconds;
        foreach (var segment in Segments)
        {
            if (seconds >= segment.End.TotalSeconds) continue;
            var fraction = Math.Clamp((seconds - segment.Start.TotalSeconds) / (segment.End - segment.Start).TotalSeconds, 0, 1);
            return new(segment.Phase, segment.From + (segment.To - segment.From) * fraction,
                segment.Phase is MetadataMarqueePhase.HeadHold or MetadataMarqueePhase.TailHold
                    ? TimeSpan.FromSeconds(segment.End.TotalSeconds - seconds) : TimeSpan.Zero);
        }
        return new(MetadataMarqueePhase.ToHead, Period, TimeSpan.Zero);
    }
}

internal static class MetadataMarqueePolicy
{
    internal static readonly TimeSpan TailHold = TimeSpan.FromMilliseconds(800);

    internal static MetadataMarqueePlan Create(double textWidth, double viewportWidth, MetadataMarqueeCursor? continuation = null)
    {
        var gap = Math.Min(24, viewportWidth * 0.1);
        var period = textWidth + gap;
        var tail = Math.Max(0, textWidth - viewportWidth);
        var cursor = continuation ?? new(MetadataMarqueePhase.HeadHold, 0, MarqueeTiming.LeadInDuration);
        var initial = Math.Max(0, cursor.Offset);
        if (initial >= period)
        {
            // 接缝随视口/DPI 缩短且已被经过时，以相同副本位置续跑下一轮，不跳到原文开头。
            initial %= period;
            cursor = new(MetadataMarqueePhase.ToTail, initial, TimeSpan.Zero);
        }
        var position = initial;
        var time = TimeSpan.Zero;
        var segments = new List<MetadataMarqueeSegment>(4);
        void Add(MetadataMarqueePhase phase, double target, TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero) return;
            segments.Add(new(phase, time, time + duration, position, target));
            time += duration;
            position = target;
        }
        void Move(MetadataMarqueePhase phase, double target) => Add(phase, target,
            TimeSpan.FromSeconds(Math.Max(0, target - position) / MarqueeTiming.ScrollSpeedDipPerSecond));

        if (cursor.Phase == MetadataMarqueePhase.HeadHold)
            Add(MetadataMarqueePhase.HeadHold, initial, cursor.HoldRemaining);
        if (cursor.Phase is MetadataMarqueePhase.HeadHold or MetadataMarqueePhase.ToTail ||
            cursor.Phase == MetadataMarqueePhase.TailHold && position < tail)
        {
            // 扩宽后尾部目标可能已被经过；本轮不回退或补播停顿。
            if (position <= tail)
            {
                Move(MetadataMarqueePhase.ToTail, tail);
                Add(MetadataMarqueePhase.TailHold, position, TailHold);
            }
        }
        else if (cursor.Phase == MetadataMarqueePhase.TailHold && Math.Abs(position - tail) < 0.01)
            Add(MetadataMarqueePhase.TailHold, position, cursor.HoldRemaining);
        Move(MetadataMarqueePhase.ToHead, period);
        return new(textWidth, viewportWidth, continuation is null, initial, segments);
    }
}
