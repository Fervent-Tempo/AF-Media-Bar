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
    private static readonly HttpClient Client = CreateClient();
    private static readonly SemaphoreSlim SessionGate = new(1, 1);
    private static SessionState? _session;
    private static DateTimeOffset _sessionFetchedAt;

    private sealed record SessionState(string Uid, string Sid, string UserIp);

    /// <summary>返回解密后的 QRC 音译文本；没有罗马音、接口失败或取消时返回 null / 抛取消异常。</summary>
    public static async Task<string?> FetchAsync(
        string? songId,
        LyricsRequest request,
        CancellationToken cancellationToken)
    {
        if (!long.TryParse(songId, out var id) || id <= 0)
            return null;

        // 罗马音是附加信息：整个补取链路最多等 6 秒，超时按「没有音译」处理，绝不拖慢主歌词取词。
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(6));
        var token = timeout.Token;

        try
        {
            var session = await GetSessionAsync(token);
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

            var root = await PostAsync(payload, token);
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

    private static async Task<SessionState> GetSessionAsync(CancellationToken cancellationToken)
    {
        await SessionGate.WaitAsync(cancellationToken);
        try
        {
            if (_session is not null && DateTimeOffset.UtcNow - _sessionFetchedAt < SessionLifetime)
                return _session;

            var root = await PostAsync(new
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

    private static async Task<JsonElement> PostAsync(object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await Client.PostAsync(Endpoint, content, cancellationToken);
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

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", "tmeLoginType=-1;");
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "okhttp/3.14.9");
        return client;
    }

    private static string Base64(string? value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
}
