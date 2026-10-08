using AFMediaBar.Classes.Services.Updates;

namespace AFMediaBar.Classes.Services.Credits;

/// <summary>
/// 两份名单的端点与尝试顺序。
/// The endpoints of both name lists and the order they are attempted in.
///
/// 与更新清单同一套取舍：**先直连、再加速**，且加速只用于内容不改写的端点。
/// 名单本身没有哈希兜底（不像安装包有清单里的 SHA-256），因此它只能从"不改写内容"的地址读取：
/// raw.githubusercontent.com、jsDelivr，以及更新清单里发布侧实测过的加速站点——最后这一档是**备选**，
/// 用户在国内网络下没有它就只能看到空名单，而名单被替换的后果是"显示错的名字"，远轻于装错包。
/// The same trade-off as the update manifest: **direct first, accelerators second**, and accelerators only for endpoints that do not
/// rewrite content.
///
/// The lists have no hash backstop, unlike an installer which is verified against the SHA-256 in the manifest, so they may only be read
/// from addresses that do not rewrite content: raw.githubusercontent.com, jsDelivr, and the accelerator sites the release side has
/// verified in the update manifest. That last tier is a **fallback**: without it a user in China would simply see empty lists, and a
/// substituted list only means wrong names on screen, which is far lighter than installing a wrong package.
/// </summary>
public static class CreditsSourcePolicy
{
    /// <summary>GitHub 贡献者接口（未认证时限流 60 次/小时/IP，因此结果必须缓存）。/ The GitHub contributors API; it is rate-limited to 60 requests per hour per IP without a token, so results have to be cached.</summary>
    public static string ContributorsApiUrl =>
        $"https://api.github.com/repos/{CreditLinks.RepositoryOwner}/{CreditLinks.RepositoryName}/contributors?per_page=100&anon=0";

    /// <summary>仓库里的贡献者快照，用作 API 不可用时的回退。/ The contributor snapshot in the repository, used when the API is unavailable.</summary>
    public static string ContributorsSnapshotUrl =>
        $"https://raw.githubusercontent.com/{CreditLinks.RepositoryOwner}/{CreditLinks.RepositoryName}/release-metadata/release/contributors.json";

    /// <summary>仓库里的赞助名单。/ The sponsor list in the repository.</summary>
    public static string SponsorsUrl =>
        $"https://raw.githubusercontent.com/{CreditLinks.RepositoryOwner}/{CreditLinks.RepositoryName}/{CreditLinks.DefaultBranch}/docs/sponsors.json";

    /// <summary>
    /// jsDelivr 镜像地址；它把分支文件缓存最长 12 小时，因此只能作为备用。
    /// The jsDelivr mirror; it caches branch files for up to 12 hours, so it is a fallback only.
    /// </summary>
    /// <param name="rawUrl">raw.githubusercontent.com 上的地址；为空时返回空字符串。/ The raw.githubusercontent.com address, returning an empty string when it is null or blank.</param>
    /// <returns>镜像地址；输入不是预期的端点时返回空字符串。/ The mirror address, or an empty string when the input is not the expected endpoint.</returns>
    public static string ToJsDelivrMirror(string? rawUrl)
    {
        const string prefix = "https://raw.githubusercontent.com/";
        if (string.IsNullOrWhiteSpace(rawUrl) || !rawUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        // raw 的路径是 owner/repo/branch/...，jsDelivr 的同义写法是 owner/repo@branch/...
        // A raw path is owner/repo/branch/..., whose jsDelivr equivalent is owner/repo@branch/...
        var rest = rawUrl[prefix.Length..];
        var firstSlash = rest.IndexOf('/');
        if (firstSlash <= 0)
        {
            return string.Empty;
        }

        var secondSlash = rest.IndexOf('/', firstSlash + 1);
        if (secondSlash <= 0)
        {
            return string.Empty;
        }

        var ownerAndRepo = rest[..secondSlash];
        var branchAndPath = rest[(secondSlash + 1)..];
        var branchSeparator = branchAndPath.IndexOf('/');
        if (branchSeparator <= 0)
        {
            return string.Empty;
        }

        var branch = branchAndPath[..branchSeparator];
        var path = branchAndPath[(branchSeparator + 1)..];
        return $"https://cdn.jsdelivr.net/gh/{ownerAndRepo}@{branch}/{path}";
    }

    /// <summary>
    /// 展开一份名单的完整尝试序列：直连 → jsDelivr → 更新清单里的加速站点（如果有）。
    /// Expands the full attempt sequence for one list: direct, then jsDelivr, then the accelerators from the update manifest, if any.
    /// </summary>
    /// <param name="rawUrl">raw.githubusercontent.com 上的地址；为空时返回空计划。/ The raw.githubusercontent.com address, yielding an empty plan when it is null or blank.</param>
    /// <param name="accelerators">清单提供的加速模板；null 用内置默认，空数组表示不加速。/ Accelerator templates from the manifest: null uses the built-in defaults and an empty array disables acceleration.</param>
    /// <returns>去重后的尝试序列。/ The deduplicated attempt sequence.</returns>
    public static IReadOnlyList<string> BuildListPlan(string? rawUrl, IReadOnlyList<string>? accelerators)
    {
        var plan = new List<string>();
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return plan;
        }

        plan.Add(rawUrl);

        var mirror = ToJsDelivrMirror(rawUrl);
        if (mirror.Length > 0)
        {
            plan.Add(mirror);
        }

        // 加速只作用于 github.com 直链：这两份名单在 raw 主机上，因此先用与更新链路同一套模板改写一次，
        // 只有模板本身接受 raw 地址时才追加（见 GitHubAcceleratorPolicy.CanAccelerate）。
        // Accelerators only apply to github.com direct links, while these lists live on the raw host, so the same templates the update path
        // uses are applied once and only appended when the template accepts the raw address (see GitHubAcceleratorPolicy.CanAccelerate).
        foreach (var candidate in GitHubAcceleratorPolicy.Expand(rawUrl, accelerators))
        {
            if (!plan.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                plan.Add(candidate);
            }
        }

        return plan;
    }

    /// <summary>贡献者接口的尝试序列。接口主机不是 <c>github.com</c>，加速模板对它无效，因此这一档只有直连；
    /// 接口不可用时由仓库快照回退（两者的合并顺序在 <c>CreditsService</c> 里）。
    /// The attempt sequence for the contributors API. Its host is not <c>github.com</c>, the accelerator templates do not apply to it, so this tier
    /// is direct only; when the API is unavailable the repository snapshot takes over, and the merge order of the two lives in
    /// <c>CreditsService</c>.
    /// </summary>
    public static IReadOnlyList<string> BuildContributorsApiPlan() => [ContributorsApiUrl];

    /// <summary>
    /// 仅用于测试与人工验收的覆盖变量：赞助名单地址。
    /// Override variable used only by tests and manual acceptance: the sponsor-list address.
    /// </summary>
    public const string SponsorsUrlOverrideVariable = "AFMEDIABAR_CREDITS_SPONSORS_URL";

    /// <summary>
    /// 仅用于测试与人工验收的覆盖变量：贡献者快照地址。
    /// Override variable used only by tests and manual acceptance: the contributor-snapshot address.
    /// </summary>
    public const string ContributorsSnapshotUrlOverrideVariable = "AFMEDIABAR_CREDITS_CONTRIBUTORS_URL";

    /// <summary>
    /// 读取覆盖变量。
    ///
    /// 与更新清单的覆盖变量同一语义：设置后**替换**整份尝试序列（不追加），因此验收结果与线上顺序不会互相混淆。
    /// 它可以是一个本地文件的完整路径——维护者因此能在把名单推上仓库之前，先在界面上看到它的样子。
    /// Reads an override variable.
    ///
    /// The same meaning as the update manifest's override: when set it **replaces** the whole attempt sequence instead of appending, so an
    /// acceptance run can never be confused with production order. It may be a full path to a local file, which lets a maintainer see how a list looks
    /// on the interface before pushing it to the repository.
    /// </summary>
    /// <param name="variableName">变量名。/ The variable name.</param>
    /// <returns>覆盖地址；未设置时为空字符串。/ The override address, or an empty string when it is not set.</returns>
    public static string ResolveOverride(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    /// <summary>
    /// 当前是否有任何覆盖变量生效。
    ///
    /// 它被用来**跳过缓存**：维护者一旦明确指向某个地址（本地文件或某个分支的 raw），那一次取数就不该被"24 小时内的缓存"挡住——
    /// 否则表现是"我把环境变量设了、程序却连一次网络请求都没发"，而这正是本功能第一次验收时踩到的坑。
    /// Whether any override variable is in effect.
    ///
    /// It is used to **skip the cache**: once a maintainer has explicitly pointed at an address — a local file or a branch's raw URL — that fetch must not be held
    /// back by "the cache is less than 24 hours old". Otherwise the symptom is "I set the environment variable and the program never even made a request", which
    /// is exactly what the first acceptance run of this feature hit.
    /// </summary>
    public static bool HasAnyOverride =>
        ResolveOverride(SponsorsUrlOverrideVariable).Length > 0 ||
        ResolveOverride(ContributorsSnapshotUrlOverrideVariable).Length > 0;

    /// <summary>
    /// 赞助名单的完整尝试序列，已包含覆盖变量。
    /// The full attempt sequence for the sponsor list, including the override variable.
    /// </summary>
    /// <param name="accelerators">加速模板。/ Accelerator templates.</param>
    /// <returns>尝试序列。/ The attempt sequence.</returns>
    public static IReadOnlyList<string> BuildSponsorsPlan(IReadOnlyList<string>? accelerators) =>
        BuildPlanWithOverride(SponsorsUrlOverrideVariable, SponsorsUrl, accelerators);

    /// <summary>
    /// 贡献者快照的完整尝试序列，已包含覆盖变量。
    /// The full attempt sequence for the contributor snapshot, including the override variable.
    /// </summary>
    /// <param name="accelerators">加速模板。/ Accelerator templates.</param>
    /// <returns>尝试序列。/ The attempt sequence.</returns>
    public static IReadOnlyList<string> BuildContributorsSnapshotPlan(IReadOnlyList<string>? accelerators) =>
        BuildPlanWithOverride(ContributorsSnapshotUrlOverrideVariable, ContributorsSnapshotUrl, accelerators);

    private static IReadOnlyList<string> BuildPlanWithOverride(
        string overrideVariable,
        string rawUrl,
        IReadOnlyList<string>? accelerators)
    {
        var overridden = ResolveOverride(overrideVariable);
        return overridden.Length > 0 ? [overridden] : BuildListPlan(rawUrl, accelerators);
    }
}
