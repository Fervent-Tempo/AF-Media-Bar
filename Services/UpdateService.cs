using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using AFMediaBar.Models;
using Microsoft.Win32;

namespace AFMediaBar.Services;

/// <summary>
/// 获取轻量 HTTPS 版本清单，但不会修改正在运行的可执行文件。
/// Fetches a small HTTPS version manifest without modifying the running executable.
/// </summary>
internal sealed class UpdateService : IDisposable
{
    private const string SettingsKeyPath = @"Software\AFMediaBar";
    private const string AutomaticChecksEnabledValueName = "AutomaticUpdateChecksEnabled";
    private const string LastCheckValueName = "UpdateLastCheckUtc";
    private const string SkippedVersionValueName = "UpdateSkippedVersion";
    private static readonly TimeSpan AutomaticCheckInterval = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    // 按顺序尝试多个公开来源，降低单个 GitHub 入口不可用造成的影响。 / Try multiple public sources in order to tolerate a blocked or unavailable GitHub endpoint.
    internal static IReadOnlyList<Uri> ManifestUris { get; } =
    [
        new("https://fervent-tempo.github.io/AF-Media-Bar/latest.json"),
        new("https://cdn.jsdelivr.net/gh/Fervent-Tempo/AF-Media-Bar@main/docs/latest.json"),
        new("https://raw.githubusercontent.com/Fervent-Tempo/AF-Media-Bar/main/docs/latest.json")
    ];

    private readonly HttpClient _httpClient;
    private readonly Version _currentVersion;
    private int _isChecking;
    private bool _disposed;

    internal UpdateService()
    {
        _currentVersion = NormalizeVersion(
            Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("AFMediaBar", _currentVersion.ToString(3)));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    internal Version CurrentVersion => _currentVersion;

    internal bool AutomaticChecksEnabled
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

    internal UpdateInfo? LatestRelease { get; private set; }

    internal UpdateInfo? LatestUpdate { get; private set; }

    internal event EventHandler<UpdateInfo>? UpdateAvailable;

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
    internal async Task<UpdateCheckResult> CheckForUpdatesAsync(
        bool force,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return UpdateCheckResult.Failed("更新服务已关闭。");
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
                        lastError = $"版本清单返回 HTTP {(int)response.StatusCode}。";
                        continue;
                    }

                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    if (!TryParseManifest(json, manifestUri, out var update, out var parseError))
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
                    TaskCanceledException or
                    JsonException)
                {
                    lastError = exception.Message;
                }
                catch (ObjectDisposedException) when (_disposed)
                {
                    return UpdateCheckResult.Failed("更新服务已关闭。");
                }
            }

            var errorMessage = string.IsNullOrWhiteSpace(lastError)
                ? "无法访问版本清单。"
                : "无法访问版本清单，请稍后重试。\n" + lastError;
            DiagnosticsLogService.Write("update-check-failed", details: errorMessage);
            return UpdateCheckResult.Failed(errorMessage);
        }
        finally
        {
            Interlocked.Exchange(ref _isChecking, 0);
        }
    }

    internal bool IsVersionSkipped(Version version)
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

    internal void SetAutomaticChecksEnabled(bool enabled)
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

    internal void SkipVersion(Version version)
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

    internal void ClearSkippedVersion()
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

    internal static Uri? GetPreferredDownloadUri(UpdateInfo update)
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

    /// <summary>
    /// 解析并过滤清单中的外部数据，只保留 HTTPS 链接和有效 SHA-256。
    /// Parses and filters external manifest data, retaining only HTTPS links and valid SHA-256 values.
    /// </summary>
    private static bool TryParseManifest(
        string json,
        Uri manifestUri,
        out UpdateInfo? update,
        out string error)
    {
        update = null;
        error = "版本清单格式无效。";
        UpdateManifestDto? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<UpdateManifestDto>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        if (manifest is null || manifest.SchemaVersion != 1)
        {
            return false;
        }

        if (!Version.TryParse(manifest.Version, out var parsedVersion))
        {
            error = "版本清单中的版本号无效。";
            return false;
        }

        var version = NormalizeVersion(parsedVersion);
        var releaseNotesUri = ParseHttpsUri(manifest.ReleaseNotesUrl);
        var downloads = ParseHttpsUris(manifest.Downloads);
        if (releaseNotesUri is null && downloads.Count == 0)
        {
            error = "版本清单没有可用的 HTTPS 下载或说明链接。";
            return false;
        }

        var minimumVersion = Version.TryParse(
            manifest.MinimumSupportedVersion,
            out var parsedMinimumVersion)
            ? NormalizeVersion(parsedMinimumVersion).ToString(3)
            : null;
        DateTimeOffset? releaseDate = DateTimeOffset.TryParse(
            manifest.ReleaseDate,
            out var parsedReleaseDate)
            ? parsedReleaseDate
            : null;
        var changelog = manifest.Changelog?
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Take(12)
            .ToArray() ?? [];
        var title = string.IsNullOrWhiteSpace(manifest.Title)
            ? $"AF Media Bar {version.ToString(3)}"
            : manifest.Title.Trim();

        update = new UpdateInfo(
            version,
            version.ToString(3),
            title,
            releaseDate,
            minimumVersion,
            manifest.Mandatory,
            changelog,
            releaseNotesUri,
            downloads,
            ParseSha256(manifest.Sha256),
            manifestUri);
        return true;
    }

    private static Dictionary<string, string> ParseSha256(
        IReadOnlyDictionary<string, string>? values)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (values is null)
        {
            return result;
        }

        foreach (var (name, value) in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var checksum = value.Trim();
            if (checksum.Length == 64 && checksum.All(Uri.IsHexDigit))
            {
                result[name] = checksum.ToLowerInvariant();
            }
        }

        return result;
    }

    private static Dictionary<string, Uri> ParseHttpsUris(
        IReadOnlyDictionary<string, string>? values)
    {
        var result = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
        if (values is null)
        {
            return result;
        }

        foreach (var (name, value) in values)
        {
            var uri = ParseHttpsUri(value);
            if (uri is not null)
            {
                result[name] = uri;
            }
        }

        return result;
    }

    private static Uri? ParseHttpsUri(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            ? uri
            : null;
    }

    private static Version NormalizeVersion(Version version)
    {
        return new Version(
            Math.Max(0, version.Major),
            Math.Max(0, version.Minor),
            Math.Max(0, version.Build));
    }

    private sealed class UpdateManifestDto
    {
        public int SchemaVersion { get; set; }
        public string? Version { get; set; }
        public string? ReleaseDate { get; set; }
        public string? MinimumSupportedVersion { get; set; }
        public bool Mandatory { get; set; }
        public string? Title { get; set; }
        public List<string>? Changelog { get; set; }
        public string? ReleaseNotesUrl { get; set; }
        public Dictionary<string, string>? Downloads { get; set; }
        public Dictionary<string, string>? Sha256 { get; set; }
    }
}
