// QQ 音乐在线罗马音补取。
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using AFMediaBar.Classes.Models;
using QrcDecrypter = Lyricify.Lyrics.Decrypter.Qrc.Decrypter;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 补取 QQ 音乐在线歌词的音译（罗马音）字段。
/// Fetches the romanization field that QQ Music's online lyric endpoint exposes but the Lyricify response does not map.
///
/// <para>
/// <c>music.musichallSong.PlayLyricInfo</c> 在请求参数里带 <c>roma: 1</c> 时会返回 <c>roma</c>，
/// 内容是与正文同样加密的 QRC（十六进制文本）；解密后交给 <see cref="LyricsTextParser"/> 当独立音译文本按时间戳合并。
/// 参数和字段名参照 LDDC 的 <c>core/api/lyrics/qm.py</c>，解密复用项目已有的 QRC 解密器，
/// 不引入新的加密实现。会话信息缓存 20 分钟，失败时只让罗马音缺席，不影响主歌词的取词结果。
/// </para>
/// </summary>
internal static class QQMusicRomanizationProvider
{
    private const string Endpoint = "https://u.y.qq.com/cgi-bin/musicu.fcg";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(20);
    private static readonly HttpClient DefaultClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly SemaphoreSlim SessionGate = new(1, 1);
    private static SessionState? _session;
    private static DateTimeOffset _sessionFetchedAt;

    /// <summary>
    /// 补取链路的等待上限。实际用的是它与本轮剩余预算的较小者：正文已经拿到，
    /// 剩下的时间不够跑完这条链路时直接放弃，不让音译把正文一起拖过外层超时。
    /// </summary>
    /// <remarks>
    /// Wait limit for the supplementary request. What is actually used is the smaller of this and the budget left in the current
    /// round: once the main lyrics are in hand, a round with too little time left gives the romanization up instead of letting it
    /// push the result past the outer timeout.
    /// </remarks>
    private static readonly TimeSpan MaximumWait = TimeSpan.FromSeconds(6);

    /// <summary>
    /// 剩余时间低于此值就不再发起补取：一次请求的往返都用不完，拿到的是超时而不是音译。
    /// Below this much time the request is not started at all: a round trip cannot finish, so it would only ever time out.
    /// </summary>
    internal static readonly TimeSpan MinimumUsefulWait = TimeSpan.FromMilliseconds(800);

    /// <summary>
    /// 留给解析与返回的余量：补取必须在截止时刻之前结束，留出解密、解析与组装正文的时间。
    /// 外层等待与本轮截止时刻几乎同时到达，两者撞在一起时丢掉的会是已经拿到的正文。
    /// </summary>
    /// <remarks>
    /// Margin reserved for parsing and returning: the supplementary request has to end before the deadline so that decrypting,
    /// parsing and assembling the main lyrics still fit. The outer wait and this deadline expire at almost the same moment, and
    /// when they collide it is the main lyrics that get discarded.
    /// </remarks>
    internal static readonly TimeSpan ReturnReserve = TimeSpan.FromMilliseconds(250);

    private sealed record SessionState(string Uid, string Sid, string UserIp);

    /// <summary>
    /// 本轮还能用来补取的时间：取本轮剩余预算与上限的较小者，再扣掉解析与返回要用的余量。
    /// 剩余时间不足一次往返时返回零，调用方据此放弃补取；没有截止时刻时用上限。
    /// </summary>
    /// <remarks>
    /// Time still available for the supplementary request: the smaller of the budget left in this round and the upper limit, minus
    /// the margin reserved for parsing and returning. It comes back as zero when there is not enough time for one round trip, and
    /// the caller gives the request up; the upper limit applies when the round has no deadline.
    /// </remarks>
    internal static TimeSpan RemainingWait(LyricsRequest request)
    {
        var budget = MaximumWait;
        if (request.Deadline is { } deadline)
        {
            // GetElapsedTime 给出的是从该时刻到现在的已过时间，截止时刻在将来时它为负，取反才是剩余时间。
            // GetElapsedTime measures the time from that point to now, which is negative for a deadline in the future.
            var remaining = -Stopwatch.GetElapsedTime(deadline);
            if (remaining < budget)
                budget = remaining;
        }
        var usable = budget - ReturnReserve;
        return usable < TimeSpan.Zero ? TimeSpan.Zero : usable;
    }

    /// <summary>返回解密后的 QRC 音译文本；没有罗马音、接口失败或取消时返回 null / 抛取消异常。</summary>
    public static async Task<string?> FetchAsync(
        string? songId,
        LyricsRequest request,
        CancellationToken cancellationToken,
        HttpClient? client)
    {
        if (!long.TryParse(songId, out var id) || id <= 0)
            return null;

        client ??= DefaultClient;

        try
        {
            var session = await GetSessionAsync(client, cancellationToken);
            var payload = new
            {
                comm = BuildComm(session),
                request = new
                {
                    method = "GetPlayLyricInfo",
                    module = "music.musichallSong.PlayLyricInfo",
                    param = new Dictionary<string, object>
                    {
                        ["albumName"] = Base64(request.Album),
                        ["singerName"] = Base64(request.Artist),
                        ["songName"] = Base64(request.Title),
                        ["songID"] = id,
                        ["interval"] = (int)Math.Max(0, request.DurationSeconds ?? 0),
                        ["crypt"] = 1,
                        ["ct"] = 19,
                        ["cv"] = 2111,
                        ["qrc"] = 1,
                        ["qrc_t"] = 0,
                        ["roma"] = 1,
                        ["roma_t"] = 0,
                        ["trans"] = 1,
                        ["trans_t"] = 0,
                        ["lrc_t"] = 0,
                        ["type"] = 0
                    }
                }
            };

            var root = await PostAsync(client, payload, cancellationToken);
            var data = root.GetProperty("request").GetProperty("data");
            if (!data.TryGetProperty("roma", out var roma))
                return null;

            var hex = roma.ValueKind == JsonValueKind.String ? roma.GetString() : null;
            if (string.IsNullOrWhiteSpace(hex))
                return null;

            var qrc = QrcDecrypter.DecryptLyrics(hex.Trim());
            return string.IsNullOrWhiteSpace(qrc) ? null : qrc;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // 超时、接口变更、解密失败都走这里：只少了音译行，主歌词照常返回。
            Debug.WriteLine($"[QQMusic] romanization unavailable: {exception.Message}");
            return null;
        }
    }

    private static async Task<SessionState> GetSessionAsync(HttpClient client, CancellationToken cancellationToken)
    {
        await SessionGate.WaitAsync(cancellationToken);
        try
        {
            if (_session is not null && DateTimeOffset.UtcNow - _sessionFetchedAt < SessionLifetime)
                return _session;

            var root = await PostAsync(client, new
            {
                comm = BuildComm(null),
                request = new
                {
                    method = "GetSession",
                    module = "music.getSession.session",
                    param = new { caller = 0, uid = "0", vkey = 0 }
                }
            }, cancellationToken);

            var session = root.GetProperty("request").GetProperty("data").GetProperty("session");
            _session = new SessionState(
                session.GetProperty("uid").ToString(),
                session.GetProperty("sid").ToString(),
                session.GetProperty("userip").ToString());
            _sessionFetchedAt = DateTimeOffset.UtcNow;
            return _session;
        }
        finally
        {
            SessionGate.Release();
        }
    }

    private static async Task<JsonElement> PostAsync(HttpClient client, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Cookie", "tmeLoginType=-1;");
        request.Headers.TryAddWithoutValidation("User-Agent", "okhttp/3.14.9");
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement.Clone();
        if (root.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number && code.GetInt32() != 0)
            throw new HttpRequestException($"musicu.fcg code {code.GetInt32()}");
        if (root.GetProperty("request").TryGetProperty("code", out var requestCode) &&
            requestCode.ValueKind == JsonValueKind.Number && requestCode.GetInt32() != 0)
            throw new HttpRequestException($"musicu.fcg request code {requestCode.GetInt32()}");
        return root;
    }

    private static Dictionary<string, object> BuildComm(SessionState? session)
    {
        var comm = new Dictionary<string, object>
        {
            ["ct"] = 11,
            ["cv"] = "1003006",
            ["v"] = "1003006",
            ["os_ver"] = "15",
            ["phonetype"] = "24122RKC7C",
            ["tmeAppID"] = "qqmusiclight",
            ["nettype"] = "NETWORK_WIFI",
            ["udid"] = "0"
        };
        if (session is not null)
        {
            comm["uid"] = session.Uid;
            comm["sid"] = session.Sid;
            comm["userip"] = session.UserIp;
        }

        return comm;
    }

    private static string Base64(string? value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
}
