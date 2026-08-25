using System.Net.Http;
using System.Buffers;
using System.IO;
using System.Text;
using AFMediaBar.Abstractions;

namespace AFMediaBar.Services;

/// <summary>
/// Shared HTTP transport used by remote media metadata and artwork features.
/// </summary>
public sealed class HttpContentLoader : IHttpContentLoader
{
    private const string UserAgent = "AF-Media-Bar";
    private const int DefaultMaximumTextBytes = 2 * 1024 * 1024;

    private static readonly HttpClient SharedHttpClient = CreateHttpClient();

    private readonly HttpClient _httpClient;
    private readonly int _maximumTextBytes;

    public HttpContentLoader()
        : this(SharedHttpClient)
    {
    }

    public HttpContentLoader(
        HttpClient httpClient,
        int maximumTextBytes = DefaultMaximumTextBytes)
    {
        _httpClient = httpClient;
        _maximumTextBytes = maximumTextBytes;
    }

    public async Task<HttpContentResult> GetStreamAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        ValidateUri(uri);

        HttpResponseMessage? response = null;
        try
        {
            response = await _httpClient.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return new HttpContentResult(
                stream,
                response.Content.Headers.ContentLength,
                GetEncoding(response),
                response);
        }
        catch
        {
            response?.Dispose();
            throw;
        }
    }

    public async Task<string> GetStringAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        await using var content = await GetStreamAsync(uri, cancellationToken);
        if (content.ContentLength is long contentLength && contentLength > _maximumTextBytes)
        {
            throw new InvalidDataException("Remote text content exceeds the size limit.");
        }

        using var memoryStream = new MemoryStream(
            content.ContentLength is long boundedLength && boundedLength > 0 && boundedLength <= _maximumTextBytes
                ? checked((int)boundedLength)
                : 0);
        await CopyBoundedAsync(
            content.Content,
            memoryStream,
            _maximumTextBytes,
            cancellationToken);
        memoryStream.Position = 0;
        using var reader = new StreamReader(
            memoryStream,
            content.Encoding ?? Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024,
            leaveOpen: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static async Task CopyBoundedAsync(
        Stream source,
        MemoryStream destination,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            var totalBytes = 0;
            while (true)
            {
                var bytesToRead = Math.Min(
                    buffer.Length,
                    maximumBytes - totalBytes + 1);
                var bytesRead = await source.ReadAsync(
                    buffer.AsMemory(0, bytesToRead),
                    cancellationToken);
                if (bytesRead == 0)
                {
                    return;
                }

                totalBytes += bytesRead;
                if (totalBytes > maximumBytes)
                {
                    throw new InvalidDataException("Remote text content exceeds the size limit.");
                }

                await destination.WriteAsync(
                    buffer.AsMemory(0, bytesRead),
                    cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void ValidateUri(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new NotSupportedException("Remote content URI must use HTTP or HTTPS.");
        }
    }

    private static Encoding? GetEncoding(HttpResponseMessage response)
    {
        var charset = response.Content.Headers.ContentType?.CharSet;
        if (string.IsNullOrWhiteSpace(charset))
        {
            return null;
        }

        try
        {
            return Encoding.GetEncoding(charset);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return httpClient;
    }
}
