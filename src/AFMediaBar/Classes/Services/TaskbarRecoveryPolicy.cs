namespace AFMediaBar.Classes.Services;

/// <summary>
/// 保留 Explorer 任务栏恢复状态机的重试时序和稳定样本要求。
/// Preserves the retry timing and stable-sample requirements of the Explorer taskbar recovery state machine.
/// </summary>
public static class TaskbarRecoveryPolicy
{
    /// <summary>最大恢复尝试次数。/ Maximum recovery attempts.</summary>
    public const int MaximumAttempts = 8;
    /// <summary>初次恢复等待毫秒数。/ Initial recovery delay in milliseconds.</summary>
    public const int InitialDelayMilliseconds = 900;
    /// <summary>后续恢复等待毫秒数。/ Subsequent recovery delay in milliseconds.</summary>
    public const int RetryDelayMilliseconds = 600;
    /// <summary>判定任务栏稳定所需的连续样本数。/ Consecutive samples required to consider the taskbar stable.</summary>
    public const int RequiredStableSamples = 2;

    /// <summary>返回指定尝试的恢复延迟。/ Returns the recovery delay for the specified attempt.</summary>
    public static TimeSpan GetDelay(int attempt) =>
        TimeSpan.FromMilliseconds(attempt == 0 ? InitialDelayMilliseconds : RetryDelayMilliseconds);
}
