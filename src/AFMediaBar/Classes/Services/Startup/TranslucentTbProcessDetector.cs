// Detects TranslucentTB without owning settings or changing any external process.
using System.Diagnostics;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>检测当前会话是否正在运行 TranslucentTB。 / Detects whether TranslucentTB is running in the current session.</summary>
public sealed class TranslucentTbProcessDetector
{
    internal const string ProcessName = "TranslucentTB";

    /// <summary>查询进程并立即释放查询得到的句柄包装。 / Queries the process and immediately disposes the returned handle wrappers.</summary>
    public bool IsRunning()
    {
        Process[] processes = [];
        try
        {
            processes = Process.GetProcessesByName(ProcessName);
            return processes.Length > 0;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            AppLogService.Current?.Warn("Startup", $"TranslucentTB detection failed: {exception.Message}");
            return false;
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }
}

/// <summary>TranslucentTB 首次兼容处理的纯策略。 / Pure policy for first-time TranslucentTB compatibility handling.</summary>
public static class TaskbarTransparencyCompatibilityPolicy
{
    /// <summary>决定是否开启磨砂背景并显示一次提示。 / Decides whether to enable the frosted background and show the one-time notice.</summary>
    public static TaskbarTransparencyCompatibilityDecision Resolve(
        bool promptShown,
        bool translucentTbRunning,
        TaskbarBackgroundMaterial currentMaterial)
    {
        var showPrompt = !promptShown && translucentTbRunning;
        return new(
            EnableFrostedBackground: showPrompt && currentMaterial != TaskbarBackgroundMaterial.Frosted,
            ShowPrompt: showPrompt);
    }
}

/// <summary>TranslucentTB 兼容处理决策。 / TranslucentTB compatibility decision.</summary>
public readonly record struct TaskbarTransparencyCompatibilityDecision(bool EnableFrostedBackground, bool ShowPrompt);
