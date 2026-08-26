using System.Text;

namespace AFMediaBar.Abstractions;

/// <summary>
/// Loads bounded HTTP content without coupling callers to a specific HTTP client.
/// </summary>
public interface IHttpContentLoader
{
    Task<HttpContentResult> GetStreamAsync(Uri uri, CancellationToken cancellationToken);

    Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken);
}

public sealed class HttpContentResult(
    Stream content,
    long? contentLength,
    Encoding? encoding,
    IDisposable response) : IDisposable, IAsyncDisposable
{
    public Stream Content { get; } = content;

    public long? ContentLength { get; } = contentLength;

    public Encoding? Encoding { get; } = encoding;

    public void Dispose()
    {
        response.Dispose();
        Content.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        response.Dispose();
        await Content.DisposeAsync();
    }
}
