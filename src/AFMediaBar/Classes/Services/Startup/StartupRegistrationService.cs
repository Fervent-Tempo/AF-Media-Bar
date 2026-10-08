using AFMediaBar.Resources;
using System.Diagnostics;
using Microsoft.Win32;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 管理当前用户的 Run 登记并读取系统审批状态；仅用户主动开启时解除系统禁用，不申请提权。
/// 同时拥有设置变更订阅，由组合根启动，DI 容器释放；设置页不直接执行登记。
/// Reads Windows approval state and restores approval only when the user explicitly enables startup.
/// </summary>
public sealed class StartupRegistrationService : IDisposable
{
    private readonly Func<bool, bool, string?> _applyRegistration;
    private readonly Func<bool?> _readRegistration;
    private bool _started;
    private bool _disposed;
    private bool _applying;
    private bool _previousValue;
    private bool _userAsked;

    /// <summary>创建启动项服务；由组合根启动，DI 容器释放设置订阅。</summary>
    public StartupRegistrationService()
    {
        _applyRegistration = Apply;
        _readRegistration = IsRegistered;
    }

    internal StartupRegistrationService(Func<bool, bool, string?> applyRegistration, Func<bool?> readRegistration)
    {
        _applyRegistration = applyRegistration;
        _readRegistration = readRegistration;
    }

    /// <summary>最近一次登记失败的原因；成功时为 null。</summary>
    public string? LastFailure { get; private set; }

    /// <summary>启动时无法读取登记状态，且后续尚未成功应用用户选择。</summary>
    public bool RegistrationStateUnknown { get; private set; }

    /// <summary>登记完成或失败回退后通知设置页刷新状态。</summary>
    public event EventHandler? StateChanged;

    /// <summary>加载设置后读取实际登记状态同步到设置，并订阅后续修改和整体重置。</summary>
    public string? Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started) return LastFailure;
        var registered = _readRegistration();
        RegistrationStateUnknown = registered is null;
        // 必须先同步、再订阅，避免读取到的实际状态触发一次注册表写入。
        // 无法读取时保留用户设置，但由设置页明确提示状态未经核实。
        if (registered is { } actual)
            SettingsManager.Current.LaunchAtStartup = actual;
        _previousValue = SettingsManager.Current.LaunchAtStartup;
        SettingsManager.SettingsChanged += OnSettingsChanged;
        _started = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return LastFailure;
    }

    /// <summary>处理设置页的主动操作；仅此入口允许开启时解除系统禁用，重置和启动同步不解除。</summary>
    public void SetLaunchAtStartup(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _userAsked = true;
        try { SettingsManager.Current.LaunchAtStartup = enabled; }
        finally { _userAsked = false; }
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (_disposed || _applying) return;
        if (e.PropertyName != nameof(AppSettings.LaunchAtStartup) &&
            !(e.PropertyName is null && e.ResetScope is null or SettingsResetScope.All)) return;

        // 整体替换不会经过设置页 setter。登记与失败回退必须由服务统一处理，
        // 回退产生的嵌套通知不再写注册表，避免重复登记和递归。
        _applying = true;
        try
        {
            var requested = SettingsManager.Current.LaunchAtStartup;
            LastFailure = _applyRegistration(requested, _userAsked);
            if (LastFailure is null)
            {
                var actual = _readRegistration();
                RegistrationStateUnknown = actual is null;
                SettingsManager.Current.LaunchAtStartup = actual ?? requested;
                _previousValue = SettingsManager.Current.LaunchAtStartup;
            }
            else
            {
                var actual = _readRegistration();
                RegistrationStateUnknown = actual is null;
                SettingsManager.Current.LaunchAtStartup = actual ?? _previousValue;
                _previousValue = SettingsManager.Current.LaunchAtStartup;
            }
        }
        finally
        {
            _applying = false;
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>取消设置订阅；重复释放不会再次操作注册表。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_started) SettingsManager.SettingsChanged -= OnSettingsChanged;
    }

    /// <summary>注册表值名；固定值使重复写入始终覆盖同一条记录。 / Registry value name; a fixed name keeps repeated writes on one single entry.</summary>
    public const string ValueName = "AFMediaBar";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    /// <summary>
    /// 登记或取消登记开机自动启动。
    /// Registers or unregisters run-at-startup.
    /// </summary>
    /// <param name="enabled">是否随登录启动。/ Whether the application should start with the session.</param>
    /// <returns>失败原因；成功时为 null。/ The failure reason, or null on success.</returns>
    /// <param name="userAsked">是否为用户主动开关操作；启动同步与恢复默认不得解除系统禁用。</param>
    public string? Apply(bool enabled, bool userAsked = false)
    {
        var executable = ResolveExecutablePath();
        if (executable is null)
            return Translations.Get("Service.Startup.ExecutablePathUnavailable");

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
                return Translations.Get("Service.Startup.RegistryKeyUnavailable");

            if (enabled)
            {
                var command = StartupRegistrationPolicy.BuildCommandLine(executable);
                if (key.GetValue(ValueName) as string != command)
                    key.SetValue(ValueName, command, RegistryValueKind.String);

                // Run 项可能早已存在，不能因此跳过用户明确要求的重新启用。
                if (userAsked)
                {
                    using var approvalKey = Registry.CurrentUser.OpenSubKey(StartupApprovedRunKeyPath, writable: true);
                    var approval = approvalKey?.GetValue(ValueName);
                    if (StartupRegistrationPolicy.IsStartupApproved(approval) is null)
                        return Translations.Get("About.Status.StartupUnknown");
                    if (StartupRegistrationPolicy.BuildEnabledApproval(approval) is { } restored)
                        approvalKey!.SetValue(ValueName, restored, RegistryValueKind.Binary);
                }
            }
            else if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return null;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[StartupRegistrationService] Apply failed: {exception}");
            return exception.Message;
        }
    }

    /// <summary>
    /// 读取当前路径是否已登记且系统允许启动；不可读或审批格式未知时返回 null。不修改审批记录。
    /// </summary>
    public bool? IsRegistered()
    {
        var executable = ResolveExecutablePath();
        if (executable is null)
            return null;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var command = key?.GetValue(ValueName) as string;
            if (!StartupRegistrationPolicy.Matches(command, executable)) return false;
            using var approvalKey = Registry.CurrentUser.OpenSubKey(StartupApprovedRunKeyPath, writable: false);
            return StartupRegistrationPolicy.IsStartupApproved(approvalKey?.GetValue(ValueName));
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[StartupRegistrationService] Read failed: {exception}");
            return null;
        }
    }

    private static string? ResolveExecutablePath()
    {
        var path = Environment.ProcessPath;
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }
}
