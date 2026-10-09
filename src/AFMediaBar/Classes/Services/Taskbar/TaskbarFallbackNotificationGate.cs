// 隐藏宿主拥有此通知冷却状态；不拥有计时器或系统通知资源。
namespace AFMediaBar.Classes.Services;

/// <summary>合并多个任务栏的空间不足提示尝试，防止通知刷屏。</summary>
public sealed class TaskbarFallbackNotificationGate
{
    private double _lastAttemptSeconds = double.NegativeInfinity;

    /// <summary>使用单调时间申请一次通知机会；通知失败也不立即重试。</summary>
    public bool TryBegin(double nowSeconds)
    {
        if (!double.IsFinite(nowSeconds) || nowSeconds - _lastAttemptSeconds < 30)
            return false;
        _lastAttemptSeconds = nowSeconds;
        return true;
    }
}
