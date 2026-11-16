// 把 HTTP/JSON/连接异常收敛为稳定错误码；原始 message 只用于日志。
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Media.Companions.Nons;

internal static class NonsErrorMapper
{
    public static PlayerCompanionError Map(Exception exception) => exception switch
    {
        NonsApiException api => MapApi(api),
        HttpRequestException => PlayerCompanionError.ServiceUnavailable,
        TaskCanceledException => PlayerCompanionError.Timeout,
        JsonException => PlayerCompanionError.Internal,
        IOException => PlayerCompanionError.ConnectionLost,
        _ => PlayerCompanionError.Internal
    };

    private static PlayerCompanionError MapApi(NonsApiException exception) => exception.Code switch
    {
        "stale_queue" => PlayerCompanionError.StaleQueue,
        "busy" or "too_many_connections" => PlayerCompanionError.Busy,
        "unavailable" => PlayerCompanionError.ServiceUnavailable,
        "timeout" => PlayerCompanionError.Timeout,
        "not_found" => PlayerCompanionError.TrackNotFound,
        "invalid_request" => PlayerCompanionError.InvalidRequest,
        _ => exception.StatusCode switch
        {
            HttpStatusCode.BadRequest => PlayerCompanionError.InvalidRequest,
            HttpStatusCode.NotFound => PlayerCompanionError.TrackNotFound,
            HttpStatusCode.Conflict => PlayerCompanionError.StaleQueue,
            HttpStatusCode.TooManyRequests => PlayerCompanionError.Busy,
            HttpStatusCode.ServiceUnavailable => PlayerCompanionError.ServiceUnavailable,
            HttpStatusCode.GatewayTimeout => PlayerCompanionError.Timeout,
            _ => PlayerCompanionError.Internal
        }
    };
}
