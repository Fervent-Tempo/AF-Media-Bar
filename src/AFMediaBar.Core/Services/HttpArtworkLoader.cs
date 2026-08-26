using AFMediaBar.Abstractions;

namespace AFMediaBar.Services;

/// <summary>
/// Downloads a bounded artwork stream over HTTP and delegates decoding to the UI shell.
/// </summary>
public sealed class HttpArtworkLoader : IArtworkUriLoader
{
    private readonly IArtworkDecoder _artworkDecoder;
    private readonly IHttpContentLoader _httpContentLoader;

    public HttpArtworkLoader(IArtworkDecoder artworkDecoder)
        : this(artworkDecoder, new HttpContentLoader())
    {
    }

    public HttpArtworkLoader(
        IArtworkDecoder artworkDecoder,
        IHttpContentLoader httpContentLoader)
    {
        _artworkDecoder = artworkDecoder;
        _httpContentLoader = httpContentLoader;
    }

    public async Task<ArtworkDecodeResult> LoadAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        await using var content = await _httpContentLoader.GetStreamAsync(
            uri,
            cancellationToken);
        return await _artworkDecoder.DecodeAsync(
            content.Content,
            content.ContentLength,
            cancellationToken);
    }
}
