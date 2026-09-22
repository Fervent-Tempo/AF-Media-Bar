using System.IO;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 限制哪些外部顶层窗口可以被当作任务栏的一部分；截图、录屏和普通应用浮层不得触发媒体栏避让。
/// Restricts which external top-level windows can count as taskbar chrome; capture, recording, and ordinary app overlays must not
/// move the media bar.
/// </summary>
public static class TaskbarOverlayOwnerPolicy
{
    private static readonly HashSet<string> ShellProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer",
        "SearchUI",
        "SearchApp",
        "SearchHost",
        "ShellExperienceHost",
        "StartMenuExperienceHost"
    };

    /// <summary>判断候选窗口是否由 Windows Shell 的任务栏体验进程拥有。/ Determines whether a candidate is owned by a Windows Shell taskbar experience process.</summary>
    public static bool IsShellOwned(string? processName) =>
        !string.IsNullOrWhiteSpace(processName) &&
        ShellProcessNames.Contains(Path.GetFileNameWithoutExtension(processName));
}
