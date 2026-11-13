// 只访问本机 Nons 开放 API；拥有默认 HttpClient，也允许测试注入受控 handler。
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AFMediaBar.Classes.Services.Media.Companions.Nons;

internal sealed class NonsApiClient : IDisposable
{
    internal static readonly Uri DefaultBaseAddress = new("http://127.0.0.1:46321/");
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private bool _disposed;

    public NonsApiClient() : this(CreateDefaultClient(), ownsClient: true)
    {
    }

    internal NonsApiClient(HttpClient httpClient, bool ownsClient = false)
    {
        _httpClient = httpClient;
        _ownsClient = ownsClient;
    }

    internal static string SerializeJumpCommand(long queueRevision, string trackId) =>
        JsonSerializer.Serialize(new { action = "jump", queueRevision, trackId }, JsonOptions);

    internal static string SerializeShuffleCommand(long queueRevision, bool shuffle) =>
        JsonSerializer.Serialize(new { action = "shuffle", queueRevision, shuffle }, JsonOptions);

    internal Task<NonsHealth> GetHealthAsync(CancellationToken cancellationToken) =>
        GetAsync<NonsHealth>("v1/health", cancellationToken);

    internal Task<NonsPlayer> GetPlayerAsync(CancellationToken cancellationToken) =>
        GetAsync<NonsPlayer>("v1/player", cancellationToken);

    internal Task<NonsQueue> GetQueueAsync(CancellationToken cancellationToken) =>
        GetAsync<NonsQueue>("v1/queue", cancellationToken);

    internal Task SendJumpAsync(long queueRevision, string trackId, CancellationToken cancellationToken) =>
        SendCommandAsync(SerializeJumpCommand(queueRevision, trackId), cancellationToken);

    internal Task SendShuffleAsync(long queueRevision, bool shuffle, CancellationToken cancellationToken) =>
        SendCommandAsync(SerializeShuffleCommand(queueRevision, shuffle), cancellationToken);

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateException(response.StatusCode, body);

        return JsonSerializer.Deserialize<T>(body, JsonOptions)
            ?? throw new JsonException($"Nons 返回了空的 {path} 响应。");
    }

    private async Task SendCommandAsync(string json, CancellationToken cancellationToken)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("v1/player/commands", content, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateException(response.StatusCode, body);
    }

    private static NonsApiException CreateException(HttpStatusCode statusCode, string body)
    {
        var fallbackCode = statusCode switch
        {
            HttpStatusCode.BadRequest => "invalid_request",
            HttpStatusCode.NotFound => "not_found",
            HttpStatusCode.Conflict => "stale_queue",
            HttpStatusCode.TooManyRequests => "busy",
            HttpStatusCode.ServiceUnavailable => "unavailable",
            HttpStatusCode.GatewayTimeout => "timeout",
            _ => "internal"
        };
        var code = fallbackCode;
        var message = string.Empty;
        try
        {
            var error = JsonSerializer.Deserialize<NonsErrorBody>(body, JsonOptions);
            if (!string.IsNullOrWhiteSpace(error?.Code))
                code = error.Code;
            message = error?.Message ?? string.Empty;
        }
        catch (JsonException)
        {
            // 非 JSON 错误体仍按状态码映射；正文不进入界面。
        }

        return new NonsApiException(statusCode, code, message);
    }

    private static HttpClient CreateDefaultClient()
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(2)
        };
        return new HttpClient(handler)
        {
            BaseAddress = DefaultBaseAddress,
            Timeout = TimeSpan.FromSeconds(8)
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_ownsClient)
            _httpClient.Dispose();
    }
}
