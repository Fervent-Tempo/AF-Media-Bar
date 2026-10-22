namespace AFMediaBar.Classes.Models.Updates;

/// <summary>
/// 版本清单（<c>docs/latest.json</c>）中一个可自动下载的安装包直链。
///
/// 只有直链与 <see cref="Sha256"/> 是必需的：完整性只由哈希证明，长度既不参与判断也不参与校验，
/// 因此 <see cref="Size"/> 是可选的（写了也不影响结果，未写不会让条目失效）。
/// One automatically downloadable installer link from the version manifest (<c>docs/latest.json</c>).
///
/// Only the link and <see cref="Sha256"/> are required: integrity is proven by the hash alone, and the length
/// neither decides nor verifies anything, so <see cref="Size"/> is optional — providing it changes no outcome and
/// omitting it does not invalidate the entry.
/// </summary>
/// <param name="Url">安装包直链；必须是 http/https 且路径以 <c>.exe</c> 结尾。/ Direct installer link; must be http/https and end with <c>.exe</c>.</param>
/// <param name="Size">清单声明的字节数（可选，仅供参考与人工核对）。/ Size declared by the manifest, optional and informational only.</param>
/// <param name="Sha256">安装包的小写或大写十六进制 SHA-256；这是唯一的完整性依据。/ Hexadecimal SHA-256 of the installer, the only integrity evidence.</param>
public sealed record UpdatePackageAsset(string Url, long? Size, string Sha256);

/// <summary>
/// 公开版本清单的只读投影。
///
/// 这是发布侧与程序之间唯一的契约：程序只读取这里出现的字段，未知字段一律忽略，因此清单可以只做追加式演进。
/// A read-only projection of the public version manifest.
///
/// This is the only contract between the release side and the application: the application reads only the fields
/// declared here and ignores unknown ones, so the manifest can evolve by appending.
/// </summary>
/// <param name="Version">清单声明的版本号。/ Version declared by the manifest.</param>
/// <param name="ReleaseDate">
/// 发布日期。它是日历日期而不是时刻，因此用 <see cref="DateOnly"/>：解析成带时区的时刻会让同一个清单在
/// UTC 以东和以西显示成不同的日期。
/// Release date. This is a calendar date rather than an instant, hence <see cref="DateOnly"/>: parsing it into an
/// offset-bearing instant would render the same manifest as different dates east and west of UTC.
/// </param>
/// <param name="Title">版本标题；用于更新亮点。/ Release title used by the update highlights.</param>
/// <param name="Changelog">更新亮点条目，已按展示顺序去空。/ Highlight entries, already trimmed and ordered for display.</param>
/// <param name="ReleaseNotesUrl">该版本的 Release 说明页。/ Release notes page for this version.</param>
/// <param name="ReleasePageUrl">最新 Release 页面；人工下载入口。/ Latest release page used by the manual download entry.</param>
/// <param name="MinimumSupportedVersion">低于该版本时视为必须更新。/ Versions below this one are treated as a required update.</param>
/// <param name="Mandatory">清单是否直接把该版本标记为必须更新。/ Whether the manifest marks the release as required.</param>
/// <param name="Packages">可自动安装的安装包直链，按清单顺序尝试。/ Installer links that may be installed automatically, attempted in manifest order.</param>
/// <param name="Accelerators">
/// 加速站点模板覆盖：<c>null</c> 表示使用程序内置默认列表，空列表表示明确禁用加速，非空列表表示按该顺序尝试。
/// Accelerator template override: <c>null</c> keeps the built-in defaults, an empty list disables acceleration
/// explicitly, and a non-empty list is attempted in that order.
/// </param>
/// <param name="PortableDownloadUrl">便携版 zip 直链；只用于人工下载入口。/ Portable zip link, used only by the manual download entry.</param>
public sealed record UpdateManifest(
    string Version,
    DateOnly? ReleaseDate,
    string? Title,
    IReadOnlyList<string> Changelog,
    string? ReleaseNotesUrl,
    string? ReleasePageUrl,
    string? MinimumSupportedVersion,
    bool Mandatory,
    IReadOnlyList<UpdatePackageAsset> Packages,
    IReadOnlyList<string>? Accelerators,
    string? PortableDownloadUrl)
{
    /// <summary>可选的分组亮点；旧清单继续使用 Changelog。</summary>
    public ReleaseHighlightsContent? ReleaseHighlights { get; init; }

    /// <summary>清单是否提供了可自动安装的安装包。/ Whether the manifest offers an automatically installable package.</summary>
    public bool HasInstallablePackage => Packages.Count > 0;
}

/// <summary>
/// 清单解析结果：失败时带一句可直接展示的原因，成功时可以没有可安装包（那是"仅手动下载"而不是失败）。
/// Manifest parse result: a failure carries a reason that can be shown as-is, while a success may still carry no
/// installable package, which means "manual download only" rather than a failure.
/// </summary>
/// <param name="Manifest">解析成功时的清单。/ Parsed manifest on success.</param>
/// <param name="FailureReason">解析失败时的原因；成功时为 null。/ Failure reason, or null on success.</param>
public sealed record UpdateManifestParseResult(UpdateManifest? Manifest, string? FailureReason)
{
    /// <summary>解析是否成功。/ Whether parsing succeeded.</summary>
    public bool Succeeded => Manifest is not null;

    /// <summary>构造成功结果。/ Creates a success result.</summary>
    /// <param name="manifest">解析出的清单。/ Parsed manifest.</param>
    public static UpdateManifestParseResult Success(UpdateManifest manifest) => new(manifest, null);

    /// <summary>构造失败结果。/ Creates a failure result.</summary>
    /// <param name="reason">可直接展示的失败原因。/ Reason that can be shown as-is.</param>
    public static UpdateManifestParseResult Failure(string reason) => new(null, reason);
}
