using Microsoft.Win32;
using AFMediaBar.Interop;
using AFMediaBar.Models;

namespace AFMediaBar.Services;

internal static class TaskbarSettingsService
{
    private const string ExplorerAdvancedKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    internal static TaskbarSettings Read()
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
            // Explorer can briefly lock its settings while applying a taskbar change.
        }

        var appBarData = NativeMethods.AppBarData.Create();
        var state = NativeMethods.SHAppBarMessage(NativeMethods.AbmGetState, ref appBarData);
        return new TaskbarSettings(
            alignment,
            (state & NativeMethods.AbsAutoHide) != 0);
    }
}
