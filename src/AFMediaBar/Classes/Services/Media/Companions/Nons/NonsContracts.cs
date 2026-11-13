// Nons 桌面开放 API 的私有 JSON 契约；字段名跟随 Nons 的 camelCase，不暴露给界面。
using System.Net;

namespace AFMediaBar.Classes.Services.Media.Companions.Nons;

internal sealed record NonsHealth(string Version, int ProtocolVersion, string[] Capabilities);

internal sealed record NonsReference(string Source, string Kind, string? Id);

internal sealed record NonsTrack(
    string TrackId,
    string Title,
    string[] Artists,
    string Album,
    long DurationMs,
    string Source,
    NonsReference? Reference,
    string? CoverUrl);

internal sealed record NonsPlayer(
    long Sequence,
    long QueueRevision,
    long Revision,
    string Status,
    int? Index,
    int QueueLength,
    NonsTrack? Current,
    long PositionMs,
    long DurationMs,
    double Volume,
    bool Shuffle,
    string RepeatMode,
    string? ActualQuality,
    string? PlaybackMode,
    string? Error);

/// <summary>player.state 事件没有 current/queueLength；单独建模，合并时保留播放器快照的曲目。</summary>
internal sealed record NonsState(
    long Sequence,
    long QueueRevision,
    long Revision,
    string Status,
    int? Index,
    double Volume,
    bool Shuffle,
    string RepeatMode,
    string? ActualQuality,
    string? PlaybackMode,
    string? Error);

internal sealed record NonsQueue(long Sequence, long QueueRevision, NonsTrack[] Items, int[] ShuffleOrder);

internal sealed record NonsProgress(long Sequence, long Revision, long PositionMs, long DurationMs, string Status);

internal sealed record NonsErrorBody(string Code, string Message);

/// <summary>Nons 返回的稳定错误；调用方按 code/status 映射，不依赖 message 文本。</summary>
internal sealed class NonsApiException(HttpStatusCode statusCode, string code, string externalMessage) : Exception(externalMessage)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public string ExternalMessage { get; } = externalMessage;
}
