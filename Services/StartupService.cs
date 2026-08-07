using Microsoft.Win32;

namespace TaskbarPlayer.Services;

internal static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AFShell";
    private const string LegacyValueName = "TaskbarPlayer";

    internal static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key.GetValue(ValueName) is string)
            {
                key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
                return true;
            }

            if (key.GetValue(LegacyValueName) is not string)
            {
                return false;
            }

            var executablePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                key.SetValue(ValueName, $"\"{executablePath}\"");
                key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
            }

            return true;
        }
    }

    internal static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
        if (enabled)
        {
            var executablePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("无法确定程序路径。");
            key.SetValue(ValueName, $"\"{executablePath}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
