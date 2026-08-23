using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using AFMediaBar.Abstractions;
using AFMediaBar.Models;
using Microsoft.Win32;

namespace AFMediaBar.Services;

/// <summary>
/// 获取轻量 HTTPS 版本清单，但不会修改正在运行的可执行文件。
/// Fetches a small HTTPS version manifest without modifying the running executable.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private const string SettingsKeyPath = @"Software\AFMediaBar";
    private const string AutomaticChecksEnabledValueName = "AutomaticUpdateChecksEnabled";
    private const string LastCheckValueName = "UpdateLastCheckUtc";
    private const string SkippedVersionValueName = "UpdateSkippedVersion";
    private static readonly TimeSpan AutomaticCheckInterval = TimeSpan.FromHours(24);
    // 按顺序尝试多个公开来源，降低单个 GitHub 入口不可用造成的影响。 / Try multiple public sources in order to tolerate a blocked or unavailable GitHub endpoint.
    public static IReadOnlyList<Uri> ManifestUris { get; } =
    [
        new("https://fervent-tempo.github.io/AF-Media-Bar/latest.json"),
        new("https://cdn.jsdelivr.net/gh/Fervent-Tempo/AF-Media-Bar@main/docs/latest.json"),
        new("https://raw.githubusercontent.com/Fervent-Tempo/AF-Media-Bar/main/docs/latest.json")
    ];

    private readonly HttpClient _httpClient;
    private readonly Version _currentVersion;
    private readonly IStringLocalizer _localizer;
    private int _isChecking;
    private bool _disposed;

    public UpdateService(IStringLocalizer localizer)
    {
        _localizer = localizer;
        _currentVersion = NormalizeVersion(
            Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0));
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("AFMediaBar", _currentVersion.ToString(3)));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public Version CurrentVersion => _currentVersion;

    public bool AutomaticChecksEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
                return key?.GetValue(AutomaticChecksEnabledValueName) switch
                {
                    int value => value != 0,
                    long value => value != 0,
                    _ => true
                };
            }
            catch
            {
                return true;
            }
        }
    }

    public UpdateInfo? LatestRelease { get; private set; }

    public UpdateInfo? LatestUpdate { get; private set; }

    public event EventHandler<UpdateInfo>? UpdateAvailable;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _httpClient.Dispose();
    }

    /// <summary>
    /// 检查公开版本清单；自动检查会执行每日节流，手动检查可通过 force 跳过节流。
    /// Checks public manifests; automatic checks are throttled daily, while force bypasses the throttle for manual checks.
    /// </summary>
    public async Task<UpdateCheckResult> CheckForUpdatesAsync(
        bool force,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return UpdateCheckResult.Failed(_localizer.Get("Msg.UpdateServiceClosed"));
        }

        if (!force && !ShouldCheckAutomatically())
        {
            return UpdateCheckResult.NotDue;
        }

        if (Interlocked.Exchange(ref _isChecking, 1) != 0)
        {
            return UpdateCheckResult.AlreadyChecking;
        }

        try
        {
            if (!force)
            {
                // 先记录自动检查尝试，避免网络故障时每次启动都重复请求。 / Record the automatic attempt first so an outage does not trigger requests on every launch.
                SaveLastCheckUtc(DateTimeOffset.UtcNow);
            }

            string? lastError = null;
            foreach (var manifestUri in ManifestUris)
            {
                try
                {
                    using var response = await _httpClient.GetAsync(
                        manifestUri,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        lastError = _localizer.Get(
                            "Msg.UpdateManifestHttpFormat",
                            (int)response.StatusCode);
                        continue;
                    }

                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    if (!UpdateManifestParser.TryParse(
                            _localizer,
                            json,
                            manifestUri,
                            out var update,
                            out var parseError))
                    {
                        lastError = parseError;
                        continue;
                    }

                    var parsedUpdate = update!;
                    SaveLastCheckUtc(DateTimeOffset.UtcNow);
                    LatestRelease = parsedUpdate;
                    LatestUpdate = parsedUpdate.Version > _currentVersion ? parsedUpdate : null;
                    if (LatestUpdate is null)
                    {
                        return UpdateCheckResult.NoUpdate(parsedUpdate);
                    }

                    var result = UpdateCheckResult.Available(LatestUpdate);
                    try
                    {
                        UpdateAvailable?.Invoke(this, LatestUpdate);
                    }
                    catch (Exception exception)
                    {
                        DiagnosticsLogService.Write("update-available-subscriber", exception);
                        // 通知订阅者失败不能破坏已经成功的网络检查。 / A subscriber failure must not turn a successful network check into an app failure.
                    }

                    return result;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (
                    exception is HttpRequestException or
                    TaskCanceledException)
                {
                    lastError = exception.Message;
                }
                catch (ObjectDisposedException) when (_disposed)
                {
                    return UpdateCheckResult.Failed(
                        _localizer.Get("Msg.UpdateServiceClosed"));
                }
            }

            var errorMessage = string.IsNullOrWhiteSpace(lastError)
                ? _localizer.Get("Msg.UpdateManifestUnavailable")
                : _localizer.Get("Msg.UpdateManifestUnavailableRetryFormat", lastError);
            DiagnosticsLogService.Write("update-check-failed", details: errorMessage);
            return UpdateCheckResult.Failed(errorMessage);
        }
        finally
        {
            Interlocked.Exchange(ref _isChecking, 0);
        }
    }

    public bool IsVersionSkipped(Version version)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
            var value = key?.GetValue(SkippedVersionValueName) as string;
            return Version.TryParse(value, out var skipped) &&
                NormalizeVersion(skipped) == NormalizeVersion(version);
        }
        catch
        {
            return false;
        }
    }

    public void SetAutomaticChecksEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
            key?.SetValue(
                AutomaticChecksEnabledValueName,
                enabled ? 1 : 0,
                RegistryValueKind.DWord);
        }
        catch
        {
            // 注册表只读时仍允许程序继续运行。 / A read-only registry must not block the application.
        }
    }

    public void SkipVersion(Version version)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
            key?.SetValue(
                SkippedVersionValueName,
                NormalizeVersion(version).ToString(3),
                RegistryValueKind.String);
        }
        catch
        {
            // 忽略版本属于可选偏好，保存失败不能影响检查或启动。 / Skipping is optional and must never affect update checks or app startup.
        }
    }

    public void ClearSkippedVersion()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: true);
            key?.DeleteValue(SkippedVersionValueName, throwOnMissingValue: false);
        }
        catch
        {
            // 重置可选更新偏好失败时，不阻塞其它设置恢复。 / Failure to reset an optional update preference must not block other settings.
        }
    }

    public static Uri? GetPreferredDownloadUri(UpdateInfo update)
    {
        if (update.DownloadUris.TryGetValue("github", out var github))
        {
            return github;
        }

        return update.DownloadUris.Values.FirstOrDefault() ?? update.ReleaseNotesUri;
    }

    private bool ShouldCheckAutomatically()
    {
        if (!AutomaticChecksEnabled)
        {
            return false;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
            var value = key?.GetValue(LastCheckValueName) as string;
            if (!DateTimeOffset.TryParse(
                value,
                null,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var lastCheck))
            {
                return true;
            }

            var elapsed = DateTimeOffset.UtcNow - lastCheck.ToUniversalTime();
            return elapsed < TimeSpan.Zero || elapsed >= AutomaticCheckInterval;
        }
        catch
        {
            return true;
        }
    }

    private static void SaveLastCheckUtc(DateTimeOffset timestamp)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
            key?.SetValue(
                LastCheckValueName,
                timestamp.ToUniversalTime().ToString("O"),
                RegistryValueKind.String);
        }
        catch
        {
            // 注册表只读时仍允许程序继续运行。 / A read-only registry must not block the application.
        }
    }

    private static Version NormalizeVersion(Version version)
    {
        return new Version(
            Math.Max(0, version.Major),
            Math.Max(0, version.Minor),
            Math.Max(0, version.Build));
    }

}
