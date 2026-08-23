using System.Text.Json;
using AFMediaBar.Abstractions;
using AFMediaBar.Models;

namespace AFMediaBar.Services;

/// <summary>
/// 验证外部更新清单并转换为强类型模型，只接受 HTTPS 链接和有效 SHA-256。
/// Validates external update manifests and maps them to typed models, accepting only HTTPS links and valid SHA-256 values.
/// </summary>
public static class UpdateManifestParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static bool TryParse(
        IStringLocalizer localizer,
        string json,
        Uri manifestUri,
        out UpdateInfo? update,
        out string error)
    {
        update = null;
        error = localizer.Get("Msg.UpdateManifestInvalid");
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
            error = localizer.Get("Msg.UpdateManifestVersionInvalid");
            return false;
        }

        var version = NormalizeVersion(parsedVersion);
        var releaseNotesUri = ParseHttpsUri(manifest.ReleaseNotesUrl);
        var downloads = ParseHttpsUris(manifest.Downloads);
        if (releaseNotesUri is null && downloads.Count == 0)
        {
            error = localizer.Get("Msg.UpdateManifestLinksMissing");
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
