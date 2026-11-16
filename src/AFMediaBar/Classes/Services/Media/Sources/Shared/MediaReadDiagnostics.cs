// 媒体读取诊断：限频记录来源、进程、构建和失败阶段，不记录原始内存内容。
using AFMediaBar.Classes.Services;

namespace AFMediaBar.Classes.Services.Media.Sources.Shared;

/// <summary>共享的媒体读取日志入口；重复失败按阶段限频，恢复时补一条。</summary>
internal static class MediaReadDiagnostics
{
    private static readonly RepeatedFailureReporter Reporter = new();

    internal static void Report(string source, string stage, string reason, int processId = 0,
        string? executableName = null, Exception? exception = null)
    {
        var key = $"{source}:{stage}";
        Reporter.Report(AppLogService.Current, "Media", key,
            $"{source} 读取失败: stage={stage} pid={processId} exe={executableName ?? "?"} reason={reason}", exception);
    }

    internal static void Recover(string source, string stage, string message)
        => Reporter.Recover(AppLogService.Current, "Media", $"{source}:{stage}", $"{source} 读取已恢复: {message}");

    internal static void Info(string source, string message)
        => AppLogService.Current?.Info("Media", $"{source}: {message}");
}
