using AFMediaBar.Classes.Models.Updates;

namespace AFMediaBar.Classes.Services.Updates;

/// <summary>
/// 清单端点与下载来源的编排策略。
///
/// 清单端点只有两个，且都不是代理：安装包有清单里的 SHA-256 兜底，清单本身没有，所以一旦清单也经第三方代理获取，
/// "哈希保护"就退化成"信任代理"。两个端点都不可达时只能人工下载，这也是有意接受的结果。
/// Ordering policy for manifest endpoints and download sources.
///
/// There are exactly two manifest endpoints and neither is a proxy: an installer is backstopped by the SHA-256 in
/// the manifest, the manifest is not, so fetching the manifest through a third-party proxy would degrade hash
/// protection into trusting that proxy. When neither endpoint is reachable, manual download is the intended answer.
/// </summary>
public static class UpdateSourcePlanPolicy
{
    /// <summary>
    /// 清单端点，按顺序尝试：raw 优先，jsDelivr 备用。
    ///
    /// jsDelivr 会把分支文件缓存最长 12 小时，因此它只能作为备用：刚发布的版本通过它可能延迟出现。
    /// Manifest endpoints, tried in order: raw first, jsDelivr as the fallback.
    ///
    /// jsDelivr caches branch files for up to 12 hours, so it is a fallback only: a freshly published version can
    /// appear there with a delay.
    /// </summary>
    public static IReadOnlyList<string> ManifestEndpoints { get; } =
    [
        "https://raw.githubusercontent.com/Fervent-Tempo/AF-Media-Bar/release-metadata/release/latest.json",
        "https://cdn.jsdelivr.net/gh/Fervent-Tempo/AF-Media-Bar@release-metadata/release/latest.json"
    ];

    /// <summary>清单不可用时的兜底人工下载页。/ Fallback manual download page used when no manifest is available.</summary>
    public static string DefaultManualReleasePage { get; } = "https://github.com/Fervent-Tempo/AF-Media-Bar/releases/latest";

    /// <summary>
    /// 按清单顺序展开完整尝试序列：每条直链后面紧跟它自己的加速候选。
    ///
    /// 顺序即优先级，发布侧把国内可用的镜像放前面就生效，不需要改代码。
    /// Expands the full attempt sequence in manifest order, with each direct link followed by its own accelerator
    /// candidates.
    ///
    /// Order is priority: putting a China-reachable mirror first is enough, with no code change.
    /// </summary>
    /// <param name="manifest">清单；为 null 时没有可尝试来源。/ Manifest; null means nothing can be tried.</param>
    /// <returns>去重后的尝试序列。/ Deduplicated attempt sequence.</returns>
    public static IReadOnlyList<UpdateDownloadSource> BuildDownloadPlan(UpdateManifest? manifest)
    {
        if (manifest is null || manifest.Packages.Count == 0)
        {
            return [];
        }

        var plan = new List<UpdateDownloadSource>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in manifest.Packages)
        {
            AddSource(plan, seen, package.Url, isAccelerated: false);

            foreach (var accelerated in GitHubAcceleratorPolicy.Expand(package.Url, manifest.Accelerators))
            {
                AddSource(plan, seen, accelerated, isAccelerated: true);
            }
        }

        return plan;
    }

    /// <summary>
    /// 选择下一个尚未尝试的来源。
    /// Selects the next source that has not been attempted yet.
    /// </summary>
    /// <param name="plan">完整尝试序列。/ Full attempt sequence.</param>
    /// <param name="attemptedUrls">本次运行中已经失败过的地址。/ Addresses that already failed during this run.</param>
    /// <returns>下一个来源；没有剩余来源时为 null。/ The next source, or null when none is left.</returns>
    public static UpdateDownloadSource? SelectNext(
        IReadOnlyList<UpdateDownloadSource> plan,
        IReadOnlyCollection<string>? attemptedUrls)
    {
        foreach (var source in plan)
        {
            if (attemptedUrls is null || !attemptedUrls.Contains(source.Url))
            {
                return source;
            }
        }

        return null;
    }

    /// <summary>
    /// 是否已经没有可尝试的自动下载来源（清单没给安装包，或所有来源都已失败）。
    /// Whether no automatic download source is left, either because the manifest offered none or because every
    /// source failed.
    /// </summary>
    /// <param name="plan">完整尝试序列。/ Full attempt sequence.</param>
    /// <param name="attemptedUrls">已经失败过的地址。/ Addresses that already failed.</param>
    public static bool IsManualOnly(
        IReadOnlyList<UpdateDownloadSource> plan,
        IReadOnlyCollection<string>? attemptedUrls) =>
        SelectNext(plan, attemptedUrls) is null;

    private static void AddSource(
        List<UpdateDownloadSource> plan,
        HashSet<string> seen,
        string url,
        bool isAccelerated)
    {
        if (!seen.Add(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return;
        }

        plan.Add(new UpdateDownloadSource(url, uri.Host.ToLowerInvariant(), isAccelerated));
    }
}
