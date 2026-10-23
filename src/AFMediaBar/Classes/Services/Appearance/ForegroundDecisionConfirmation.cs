// 单个采样会话持有候选状态；几何或代际变化时重置，不管理计时器或 UI。
namespace AFMediaBar.Classes.Services;

/// <summary>首个有效颜色立即应用，之后反向颜色需连续两次确认。</summary>
internal sealed class ForegroundDecisionConfirmation
{
    private PlayerForegroundDecision? _candidate;
    internal bool HasPending => _candidate is not null;

    internal PlayerForegroundDecision? Observe(PlayerForegroundDecision? proposed, PlayerForegroundDecision? current)
    {
        if (proposed is null || current is null || proposed == current)
        {
            Reset();
            return proposed ?? current;
        }
        if (_candidate == proposed)
        {
            Reset();
            return proposed;
        }
        _candidate = proposed;
        return current;
    }

    internal void Reset() => _candidate = null;
}
