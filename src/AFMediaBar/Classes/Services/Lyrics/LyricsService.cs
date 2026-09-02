using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 歌词服务：按优先级顺序尝试多个歌词提供者，返回第一个命中的结果。
/// Lyrics service: tries multiple providers in priority order and returns the first hit.
///
/// 职责 Responsibilities:
/// 1. 管理多个歌词源（网易云、Lrclib 等）的调用顺序
///    Manage call order of multiple lyric sources (NetEase, Lrclib, etc.)
/// 2. 实现回退策略：第一个源失败时自动尝试下一个
///    Implement fallback strategy: automatically try next source when first fails
/// 3. 支持取消操作（通过 CancellationToken）
///    Support cancellation (via CancellationToken)
///
/// 算法 Algorithm:
/// 1. 遍历所有注册的 Provider，按构造函数传入的顺序
///    Iterate through all registered providers in constructor order
/// 2. 对每个 Provider 调用 GetLyricsAsync，命中则立即返回
///    Call GetLyricsAsync on each provider, return immediately on hit
/// 3. 全部未命中返回 null
///    Return null when all providers miss
///
/// ⚠️ 注意 Note:
/// Provider 顺序很重要：精确匹配源（如网易云 song id）应放在前面，模糊匹配源（如 Lrclib 标题/艺术家）放在后面。
/// Provider order matters: exact-match sources (like NetEase song id) should come first, fuzzy-match sources (like Lrclib title/artist) last.
/// </summary>
public sealed class LyricsService
{
    private readonly IReadOnlyList<ILyricsProvider> _providers;

    public LyricsService(params ILyricsProvider[] providers)
    {
        _providers = providers;
    }

    /// <summary>
    /// 获取歌词：按顺序尝试所有提供者，返回第一个命中的结果。
    /// Get lyrics: try all providers in order, return first hit.
    /// </summary>
    public async Task<LyricsResult?> GetLyricsAsync(
        LyricsRequest request,
        CancellationToken cancellationToken)
    {
        foreach (var provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await provider.GetLyricsAsync(request, cancellationToken);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }
}
