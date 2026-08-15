using System.IO;
using Microsoft.Win32;

namespace AFMediaBar.Services;

/// <summary>
/// 管理当前用户的开机启动项，并迁移旧版程序名称对应的注册表值。
/// Manages per-user startup registration and migrates values from earlier application names.
/// </summary>
internal static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AF Media Bar";
    private const string AFShellValueName = "AFShell";
    private const string TaskbarPlayerValueName = "TaskbarPlayer";

    internal static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            MigrateLegacyValues(key);
            return key.GetValue(ValueName) is string value &&
                IsCurrentExecutableRegistration(value);
        }
    }

    internal static void Migrate()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        RemoveInvalidRegistration(key);
        MigrateLegacyValues(key);
    }

    internal static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        DeleteLegacyValues(key);
        if (enabled)
        {
            var executablePath = GetCurrentExecutablePath();
            key.SetValue(ValueName, BuildRegistration(executablePath));
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    private static void DeleteLegacyValues(RegistryKey key)
    {
        key.DeleteValue(AFShellValueName, throwOnMissingValue: false);
        key.DeleteValue(TaskbarPlayerValueName, throwOnMissingValue: false);
    }

    private static void MigrateLegacyValues(RegistryKey key)
    {
        var hasLegacyValue = key.GetValue(AFShellValueName) is string ||
            key.GetValue(TaskbarPlayerValueName) is string;
        if (!hasLegacyValue)
        {
            return;
        }

        var executablePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath))
        {
            key.SetValue(ValueName, BuildRegistration(Path.GetFullPath(executablePath)));
            DeleteLegacyValues(key);
        }
    }

    private static void RemoveInvalidRegistration(RegistryKey key)
    {
        if (key.GetValue(ValueName) is not string value ||
            IsCurrentExecutableRegistration(value))
        {
            return;
        }

        key.DeleteValue(ValueName, throwOnMissingValue: false);
        DiagnosticsLogService.Write(
            "invalid-startup-registration-removed",
            details: "The Run value did not match the current executable.");
    }

    private static bool IsCurrentExecutableRegistration(string value)
    {
        try
        {
            var executablePath = GetCurrentExecutablePath();
            return string.Equals(
                value.Trim(),
                BuildRegistration(executablePath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string GetCurrentExecutablePath()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("Unable to determine the application path.");
        }

        executablePath = Path.GetFullPath(executablePath);
        if (!File.Exists(executablePath) ||
            !string.Equals(Path.GetExtension(executablePath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The application executable path is invalid.");
        }

        return executablePath;
    }

    private static string BuildRegistration(string executablePath)
    {
        return $"\"{executablePath}\"";
    }
}
