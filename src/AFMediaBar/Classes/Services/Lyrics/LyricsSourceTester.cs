// 歌词来源的连通性测试：一次轻量 GET 报告网络可达性，不解析内容。
using System.Net.Http;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 对单个歌词来源发起一次轻量请求，报告网络层面是否可达。
/// 只服务设置页的「测试」按钮：拿到任何 HTTP 响应都算网络通了（状态码原样报告给用户），
/// DNS、连接与超时才按失败处理；取词功能本身由真实取词验证，这里不校验。
/// Sends one lightweight request to a lyric source and reports network reachability, for the settings page's per-source
/// test button. Any HTTP response counts as reachable and its status code is reported as-is; DNS, connection, and timeout
/// failures count as unreachable. Retrieval itself is validated by real lookups, not here.
/// </summary>
internal static class LyricsSourceTester
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(5) };

    /// <summary>测试一个来源；未知来源返回不可达。/ Tests one source; an unknown source reports unreachable.</summary>
    public static async Task<LyricsSourceTestResult> TestAsync(string sourceId, CancellationToken cancellationToken)
    {
        var host = LyricsSourceHosts.ProbeHostFor(sourceId);
        if (host is null)
            return new(false, string.Empty);
        try
        {
            using var response = await Client.GetAsync($"https://{host}/", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return new(true, ((int)response.StatusCode).ToString());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, "timeout");
        }
        catch (Exception exception)
        {
            return new(false, exception.Message);
        }
    }
}

/// <summary>测试结果：网络是否可达与细节（HTTP 状态码或错误摘要）。/ Test verdict: reachability plus detail (status code or error summary).</summary>
internal sealed record LyricsSourceTestResult(bool Reachable, string Detail);
