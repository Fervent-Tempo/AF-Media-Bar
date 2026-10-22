using System.Globalization;
using System.Text.Json;
using AFMediaBar.Classes.Models.Updates;
using AFMediaBar.Resources;

namespace AFMediaBar.Classes.Services.Updates;

/// <summary>
/// 版本清单（<c>docs/latest.json</c>）的解析策略。
///
/// 三条取舍固定在这里，改动它们等于改动发布契约：
/// 1) 未知字段一律忽略，清单可以只做追加式演进；
/// 2) 单个安装包条目缺直链、缺长度或缺合法 SHA-256 时只丢弃该条目，而不是让整份清单失败——后面可能还有一条
///    可用的镜像，用户不该因为第一条写错就完全失去更新；
/// 3) 清单本身合法但没有可用安装包是"仅手动下载"，不是失败。
/// Parsing policy for the version manifest (<c>docs/latest.json</c>).
///
/// Three decisions are fixed here because changing them changes the publishing contract:
/// 1) unknown fields are ignored, so the manifest can evolve by appending;
/// 2) a single package entry missing a link, a length or a valid SHA-256 is dropped on its own instead of failing
///    the whole manifest, because a usable mirror may still follow and one typo should not remove updates entirely;
/// 3) a valid manifest without a usable package means "manual download only", not a failure.
/// </summary>
public static class UpdateManifestParser
{
    /// <summary>当前程序支持的最高清单 schema 版本。/ Highest manifest schema version this application understands.</summary>
    public const int SupportedSchemaVersion = 1;

    /// <summary>更新的亮点条目数量上限，用于约束设置页高度。/ Upper bound on highlight entries, which bounds the settings page height.</summary>
    public const int MaximumChangelogEntries = 20;

    private const int Sha256HexLength = 64;

    /// <summary>
    /// 解析清单文本。
    /// Parses manifest text.
    /// </summary>
    /// <param name="json">清单内容。/ Manifest content.</param>
    /// <returns>成功时的清单，或可直接展示的失败原因。/ The manifest on success, or a reason that can be shown as-is.</returns>
    public static UpdateManifestParseResult Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return UpdateManifestParseResult.Failure(Translations.Get("Update.Reason.ManifestEmpty"));
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return UpdateManifestParseResult.Failure(Translations.Get("Update.Reason.ManifestInvalidJson"));
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return UpdateManifestParseResult.Failure(Translations.Get("Update.Reason.ManifestRootNotObject"));
            }

            if (root.TryGetProperty("schemaVersion", out var schemaElement))
            {
                if (schemaElement.ValueKind != JsonValueKind.Number ||
                    !schemaElement.TryGetInt32(out var schemaVersion) ||
                    schemaVersion < 1)
                {
                    return UpdateManifestParseResult.Failure(Translations.Get("Update.Reason.ManifestSchemaInvalid"));
                }

                if (schemaVersion > SupportedSchemaVersion)
                {
                    return UpdateManifestParseResult.Failure(
                        Translations.Format(
                            "Update.Reason.ManifestSchemaTooNew",
                            schemaVersion,
                            SupportedSchemaVersion));
                }
            }

            var version = ReadString(root, "version");
            if (version is null)
            {
                return UpdateManifestParseResult.Failure(Translations.Get("Update.Reason.ManifestVersionMissing"));
            }

            if (!UpdateVersionPolicy.TryParse(version, out _))
            {
                return UpdateManifestParseResult.Failure(
                    Translations.Format("Update.Reason.ManifestVersionUnparsable", version));
            }

            var changelog = ReadChangelog(root);
            var manifest = new UpdateManifest(
                version,
                ReadDate(root, "releaseDate"),
                ReadString(root, "title"),
                changelog,
                ReadUrl(root, "releaseNotesUrl"),
                ReadUrl(root, "releasePageUrl"),
                ReadString(root, "minimumSupportedVersion"),
                ReadBoolean(root, "mandatory"),
                ReadPackages(root),
                ReadAccelerators(root),
                ReadNestedUrl(root, "downloads", "github"))
            {
                ReleaseHighlights = root.TryGetProperty("releaseHighlights", out var content)
                    ? ReleaseHighlightsPolicy.ReadContent(content) : null
            };

            return UpdateManifestParseResult.Success(manifest);
        }
    }

    private static IReadOnlyList<UpdatePackageAsset> ReadPackages(JsonElement root)
    {
        if (!root.TryGetProperty("packages", out var packages) || packages.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<UpdatePackageAsset>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        UpdatePackageAsset? canonical = null;
        foreach (var item in packages.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var url = ReadUrl(item, "url");
            if (url is null || !IsDirectInstallerUrl(url))
            {
                continue;
            }

            var sha256 = ReadString(item, "sha256");
            if (sha256 is null || !IsSha256(sha256))
            {
                continue;
            }

            // 长度不再参与任何判断，因此它是可选的：写了就记下来供人工核对，没写或写坏都不让条目失效。
            // The length no longer decides anything, so it is optional: a usable value is kept for manual
            // cross-checking, while a missing or broken one does not invalidate the entry.
            var size = ReadOptionalSize(item);

            // 所有条目描述的是同一个安装包（GitHub 直链加它的镜像），因此哈希必须一致；两边都声明了长度时也要一致。
            // 不一致的条目不是镜像，拿它去下载只会装上一个清单没有承诺过的文件。
            // Every entry describes the same installer (the GitHub direct link plus its mirrors), so the hashes must
            // agree, and so must two declared sizes. An entry that disagrees is not a mirror, and downloading it would
            // install a file the manifest never vouched for.
            canonical ??= new UpdatePackageAsset(url, size, sha256.ToLowerInvariant());
            if (!string.Equals(canonical.Sha256, sha256, StringComparison.OrdinalIgnoreCase) ||
                (canonical.Size is { } canonicalSize && size is { } declaredSize && canonicalSize != declaredSize))
            {
                continue;
            }

            if (seen.Add(url))
            {
                result.Add(new UpdatePackageAsset(url, size, sha256.ToLowerInvariant()));
            }
        }

        return result;
    }

    /// <summary>
    /// 读取可选的清单长度：只有正整数才被记下来，其余一律视为"未声明"。
    /// Reads the optional declared length: only a positive number is kept, everything else counts as undeclared.
    /// </summary>
    /// <param name="item">安装包条目。/ Package entry.</param>
    private static long? ReadOptionalSize(JsonElement item) =>
        item.TryGetProperty("size", out var element) &&
        element.ValueKind == JsonValueKind.Number &&
        element.TryGetInt64(out var size) &&
        size > 0
            ? size
            : null;

    private static IReadOnlyList<string>? ReadAccelerators(JsonElement root)
    {
        if (!root.TryGetProperty("accelerators", out var accelerators) || accelerators.ValueKind != JsonValueKind.Array)
        {
            // 字段缺失或写法无效：回到内置默认列表。写错的加速配置不应该把加速能力一起拿走。
            // Missing or malformed: fall back to the built-in defaults, because a typo must not take acceleration away.
            return null;
        }

        if (accelerators.GetArrayLength() == 0)
        {
            // 显式空数组是"明确关闭加速"的唯一写法。
            // An explicit empty array is the only way to disable acceleration.
            return [];
        }

        var result = new List<string>();
        foreach (var item in accelerators.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var template = GitHubAcceleratorPolicy.NormalizeTemplate(item.GetString());
            if (template.Length == 0 || result.Contains(template, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(template);
        }

        return result.Count > 0 ? result : null;
    }

    private static IReadOnlyList<string> ReadChangelog(JsonElement root)
    {
        if (!root.TryGetProperty("changelog", out var changelog) || changelog.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<string>();
        foreach (var item in changelog.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var text = item.GetString()?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            result.Add(text);
            if (result.Count >= MaximumChangelogEntries)
            {
                break;
            }
        }

        return result;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = property.GetString()?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static bool ReadBoolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) &&
        property.ValueKind is JsonValueKind.True;

    private static string? ReadUrl(JsonElement element, string name)
    {
        var text = ReadString(element, name);
        return text is not null && IsUsableUrl(text) ? text : null;
    }

    private static string? ReadNestedUrl(JsonElement element, string parent, string name) =>
        element.TryGetProperty(parent, out var container) && container.ValueKind == JsonValueKind.Object
            ? ReadUrl(container, name)
            : null;

    private static DateOnly? ReadDate(JsonElement element, string name)
    {
        var text = ReadString(element, name);
        return text is not null &&
               DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            ? value
            : null;
    }

    /// <summary>
    /// 清单里的地址是否可用：https，或**仅限本机回环**的 http。
    ///
    /// 回环例外是给验收用的：它让"本地清单 + 本地服务器"能走完整个更新链路，而不必先伪造一份受信任证书。
    /// 回环地址无法离开本机、也不可能被中间人改写，因此这条例外不放大任何真实风险；除此之外一切明文地址仍然
    /// 被拒绝。
    /// Whether an address from the manifest is usable: https, or http <b>only on the loopback address</b>.
    ///
    /// The loopback exception exists for acceptance testing: it lets a local manifest plus a local server drive the
    /// whole update chain without forging a trusted certificate first. A loopback address cannot leave the machine
    /// and cannot be rewritten by anyone in between, so the exception adds no real risk; every other plaintext
    /// address is still rejected.
    /// </summary>
    /// <param name="url">待判断地址。/ Address to test.</param>
    private static bool IsUsableUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && uri.IsLoopback;
    }

    /// <summary>
    /// 是否是可执行的安装包直链。
    ///
    /// 只认 <c>.exe</c>：网盘分享页、Release 页面和 zip 都可能是清单里的合法链接，但它们都不可能被静默安装，
    /// 因此不能进入自动下载路径。查询串不参与判断，但会被保留在地址里。
    /// Whether this is a direct installer link.
    ///
    /// Only <c>.exe</c> qualifies: share pages, release pages and zip archives are all legitimate manifest links,
    /// but none of them can be installed silently, so none may enter the automatic download path. A query string is
    /// ignored for the decision but kept in the address.
    /// </summary>
    /// <param name="url">待判断地址。/ Address to test.</param>
    private static bool IsDirectInstallerUrl(string url) =>
        IsUsableUrl(url) &&
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    private static bool IsSha256(string value)
    {
        if (value.Length != Sha256HexLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            var isHex = character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
            if (!isHex)
            {
                return false;
            }
        }

        return true;
    }
}
