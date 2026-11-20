// 用户主动检查歌词 API 的 HTTP 可达性，使用实际取词所用的代理策略。
using System.Net.Http;

namespace AFMediaBar.Classes.Services;

/// <summary>歌词设置页的逐来源连通性测试。</summary>
public sealed class LyricsSourceTester
{
    private readonly HttpClient _client;

    internal LyricsSourceTester(HttpClient client) => _client = client;

    internal async Task<LyricsSourceTestResult> TestAsync(string sourceId, CancellationToken cancellationToken)
    {
        var endpoint = LyricsSourceHosts.TestEndpointFor(sourceId);
        if (endpoint is null)
            return new(false, string.Empty);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var response = await _client.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            return new(true, ((int)response.StatusCode).ToString());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, "timeout");
        }
        catch (HttpRequestException exception)
        {
            return new(false, exception.Message);
        }
    }
}

internal sealed record LyricsSourceTestResult(bool Reachable, string Detail);
