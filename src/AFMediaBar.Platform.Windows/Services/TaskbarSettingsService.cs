using Microsoft.Win32;
using AFMediaBar.Interop;
using AFMediaBar.Models;

namespace AFMediaBar.Services;

/// <summary>
/// 从 Explorer 设置和 Shell AppBar API 读取任务栏对齐与自动隐藏状态。
/// Reads taskbar alignment and auto-hide state from Explorer and the Shell AppBar API.
/// </summary>
public static class TaskbarSettingsService
{
    private const string ExplorerAdvancedKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    public static TaskbarSettings Read()
    {
        var alignment = TaskbarAlignment.Unknown;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                ExplorerAdvancedKeyPath,
                writable: false);
            if (key?.GetValue("TaskbarAl") is int value &&
                Enum.IsDefined(typeof(TaskbarAlignment), value))
            {
                alignment = (TaskbarAlignment)value;
            }
        }
        catch
        {
            // Explorer 应用任务栏变更时可能短暂锁定设置，此时保留默认对齐方式。
            // Explorer may briefly lock its settings during a taskbar change; keep the default alignment.
        }

        var appBarData = NativeMethods.AppBarData.Create();
        var state = NativeMethods.SHAppBarMessage(NativeMethods.AbmGetState, ref appBarData);
        return new TaskbarSettings(
            alignment,
            (state & NativeMethods.AbsAutoHide) != 0);
    }
}
