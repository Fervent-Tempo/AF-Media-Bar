// 开发者动作执行入口，窗口关闭时通过调用方取消令牌失效。
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Abstractions;

/// <summary>执行固定动作并读取有效宿主，不解释任意脚本。</summary>
public interface IDeveloperScenarioService
{
    event Action<string, DeveloperActionResult>? ResultObserved;
    IReadOnlyList<DeveloperLyricsHostState> GetLyricsHosts();
    Task<DeveloperActionResult> ExecuteAsync(string command, string? hostId, CancellationToken cancellationToken);
    void ClosePreviews();
}

/// <summary>平台宿主提供的诊断及 UI 意图，不向服务暴露具体窗口。</summary>
public interface IDeveloperHostActions
{
    IReadOnlyList<DeveloperLyricsHostState> GetLyricsHosts();
    DeveloperActionResult ExecuteLyricsAction(string command, string? hostId);
    DeveloperActionResult ReloadTaskbar();
    DeveloperActionResult ShowTrackPreview();
    string CaptureState();
    void ClosePreviews();
}

/// <summary>开发者确认与生产重启使用独立请求通道。</summary>
public interface IDeveloperConfirmationService
{
    bool IsPreviewActive { get; }
    Task<DeveloperConfirmationResult> ShowPreviewAsync(CancellationToken cancellationToken);
    Task<DeveloperConfirmationResult> ConfirmLyricsDisableAsync(CancellationToken cancellationToken);
}
