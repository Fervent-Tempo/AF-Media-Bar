// 按真实横轴预算决定文字密度，不修改保存的设置或原始歌词。
using AFMediaBar.Classes.Services.Lyrics;

namespace AFMediaBar.Classes.Services;

/// <summary>任务栏临时文字预算；主文字优先，空间恢复后可直接恢复副信息。</summary>
public readonly record struct TaskbarTextBudget(double FontScale, double PaddingDip, bool HideSecondary);

/// <summary>横向行高与竖向列宽共用的可测试预算策略。</summary>
public static class SmallTaskbarPresentationPolicy
{
    /// <summary>低于此实际厚度时使用紧凑几何留白，与文字是否收起无关。</summary>
    public const double CompactCrossAxisThresholdDip = 36;
    /// <summary>生成当前画面，保留原帧和逐字时间信息。</summary>
    public static LyricsPresentationFrame Project(LyricsPresentationFrame frame, bool hideSecondary) =>
        hideSecondary ? frame with
        {
            Next = string.Empty,
            CurrentTranslation = string.Empty,
            NextTranslation = string.Empty,
            TranslationMode = false
        } : frame;

    /// <summary>主文字先占用可用横轴，只有副信息放不下时才将其收起。</summary>
    public static TaskbarTextBudget Resolve(double crossAxisDip, bool vertical, double primaryFontSize,
        double secondaryFontSize, double gapDip)
    {
        var cross = double.IsFinite(crossAxisDip) ? Math.Max(1, crossAxisDip) : 44;
        var primary = double.IsFinite(primaryFontSize) ? Math.Max(1, primaryFontSize) : 14;
        var secondary = double.IsFinite(secondaryFontSize) ? Math.Max(1, secondaryFontSize) : 12;
        var gap = double.IsFinite(gapDip) ? Math.Max(0, gapDip) : 0;
        var padding = cross < CompactCrossAxisThresholdDip ? Math.Min(1, cross / 8) : 2;
        var available = Math.Max(1, cross - padding * 2);
        var lineHeight = vertical ? 1 : 1.25;
        var hideSecondary = (primary + secondary) * lineHeight + gap > available + .1;
        var fontScale = Math.Min(1, available / (primary * lineHeight));
        return new(fontScale, padding, hideSecondary);
    }
}

/// <summary>只有实际收起信息且通知成功投递后才消耗本次会话的提示机会。</summary>
public sealed class SmallTaskbarNotificationGate
{
    public bool WasShown { get; private set; }
    public bool TryNotify(bool hasSuppressedInformation, Func<bool> publish)
    {
        if (WasShown || !hasSuppressedInformation) return false;
        WasShown = publish();
        return WasShown;
    }
}
