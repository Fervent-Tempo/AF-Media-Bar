// 校验远端首页资料；所有链接只表达只读展示，不产生本地命令。
using System.Text.Json;
using AFMediaBar.Classes.Models.Home;

namespace AFMediaBar.Classes.Services.Home;

/// <summary>首页目录、公开统计和安全链接的纯解析规则。</summary>
public static class HomeContentPolicy
{
    public const string Repository = "Fervent-Tempo/AF-Media-Bar";
    public const string RepositoryUrl = "https://github.com/" + Repository;
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public static bool IsOfficialLink(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) &&
        uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
        (uri.AbsolutePath.Equals("/" + Repository, StringComparison.OrdinalIgnoreCase) ||
         uri.AbsolutePath.StartsWith("/" + Repository + "/", StringComparison.OrdinalIgnoreCase));

    public static bool IsOfficialImage(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) &&
        ((uri.Host == "raw.githubusercontent.com" && uri.AbsolutePath.StartsWith("/" + Repository + "/", StringComparison.OrdinalIgnoreCase)) ||
         (uri.Host == "cdn.jsdelivr.net" && uri.AbsolutePath.StartsWith("/gh/" + Repository + "@", StringComparison.OrdinalIgnoreCase)));

    public static HomeCatalogue ParseCatalogue(string json)
    {
        var catalogue = JsonSerializer.Deserialize<HomeCatalogue>(json, JsonOptions) ?? throw new FormatException("Missing home catalogue.");
        if (catalogue.SchemaVersion != 1 || catalogue.Items is not { Count: > 0 and <= 30 }) throw new FormatException("Invalid home catalogue.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in catalogue.Items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Id) || item.Id.Length > 64 || !ids.Add(item.Id) ||
                item.Kind is not ("release" or "feature" or "development") ||
                item.Localizations is not { Count: > 0 and <= 8 } ||
                item.Localizations.Any(copy => string.IsNullOrWhiteSpace(copy.Value?.Title) || copy.Value.Title.Length > 180 || copy.Value.Summary is null || copy.Value.Summary.Length > 1000) ||
                item.Url is not null && !IsOfficialLink(item.Url) || item.Image is not null && !IsOfficialImage(item.Image))
                throw new FormatException("Invalid home feature.");
        }
        if (catalogue.Support is { } support)
        {
            if (support.Count > 32 || support.Any(item => item is null || string.IsNullOrWhiteSpace(item.Key) || item.Key.Length > 80 ||
                item.Localizations is not { Count: > 0 and <= 8 } || item.Localizations.Any(copy => copy.Value is null || string.IsNullOrWhiteSpace(copy.Value.Title) || copy.Value.Title.Length > 1000 || copy.Value.Summary is null || copy.Value.Summary.Length > 1000)))
                throw new FormatException("Invalid support notes.");
        }
        return catalogue;
    }

    public static int? Count(JsonElement root, string property)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (root.TryGetProperty("incomplete_results", out var incomplete) && incomplete.ValueKind == JsonValueKind.True) return null;
        return root.TryGetProperty(property, out var count) && count.TryGetInt32(out var value) && value >= 0 ? value : null;
    }

    public static IReadOnlyList<ProjectThread> Threads(JsonElement root, bool pullRequests)
    {
        if (root.ValueKind != JsonValueKind.Object) return [];
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return [];
        var result = new List<ProjectThread>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (item.TryGetProperty("pull_request", out _) != pullRequests ||
                !item.TryGetProperty("number", out var number) || !number.TryGetInt32(out var id) || id <= 0 ||
                !item.TryGetProperty("title", out var title) || title.ValueKind != JsonValueKind.String) continue;
            var text = title.GetString() ?? string.Empty;
            if (text.Length > 400) text = text[..400];
            var updated = item.TryGetProperty("updated_at", out var date) && date.TryGetDateTimeOffset(out var time) ? time : DateTimeOffset.MinValue;
            result.Add(new(id, text, item.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True,
                updated, $"{RepositoryUrl}/{(pullRequests ? "pull" : "issues")}/{id}"));
            if (result.Count == 3) break;
        }
        return result;
    }
}
