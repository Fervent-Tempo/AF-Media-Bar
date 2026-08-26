using AFMediaBar.Abstractions;
using AFMediaBar.Models;

namespace AFMediaBar.Services.Lyrics;

/// <summary>
/// 按顺序尝试多个歌词源，返回第一个命中；全部未命中返回 null。
/// Tries providers in order and returns the first hit, or null when none match.
/// </summary>
public sealed class LyricsService
{
    private readonly IReadOnlyList<ILyricsProvider> _providers;

    public LyricsService(params ILyricsProvider[] providers)
    {
        _providers = providers;
    }

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
