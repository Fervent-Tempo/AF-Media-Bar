// 识别有真实堆栈证据的 WebView2 图形故障；不拦截普通托管内存不足。
using System.Runtime.InteropServices;

namespace AFMediaBar.Classes.Services.Lyrics;

internal enum WebLyricsRecoveryAction { None, Rebuild, DisableForSession }

/// <summary>WebView2 图形故障分类及每个媒体宿主的重建预算。</summary>
public static class WebView2GraphicsFaultPolicy
{
    /// <summary>每个媒体宿主最多重建三次；资源不足直接停用，不消耗重建预算。</summary>
    public const int MaximumRecoveries = 3;
    private const int OutOfMemory = unchecked((int)0x8007000E);
    private const int OutOfVideoMemory = unchecked((int)0x8876017C);
    private const string GraphicsFrame = "Microsoft.Web.WebView2.Wpf.GraphicsItemD3DImage.";

    public static bool IsGraphicsFault(Exception? exception) => Classify(exception) != WebLyricsRecoveryAction.None;

    /// <summary>仅识别已知图形路径；异常类型及 HRESULT 由异常重载进一步核对。</summary>
    public static bool IsGraphicsFault(string? stackTrace) => stackTrace is not null &&
        (stackTrace.Contains("Microsoft.Web.WebView2.Wpf.Direct3DHelper.CreateD3D11Texture", StringComparison.Ordinal) ||
         stackTrace.Contains(GraphicsFrame, StringComparison.Ordinal));

    internal static WebLyricsRecoveryAction Classify(Exception? exception)
    {
        var stack = exception?.StackTrace;
        if (stack is null) return WebLyricsRecoveryAction.None;
        var displayRecovery = stack.Contains("Microsoft.Web.WebView2.Wpf.Direct3DHelper.CreateD3D9Device(", StringComparison.Ordinal) &&
                              stack.Contains(GraphicsFrame + "RecoverAfterDisplayChange(", StringComparison.Ordinal);
        if (displayRecovery &&
            ((exception is OutOfMemoryException && exception.HResult == OutOfMemory) ||
             (exception is COMException && exception.HResult is OutOfMemory or OutOfVideoMemory)))
            return WebLyricsRecoveryAction.DisableForSession;

        return exception is NullReferenceException && IsGraphicsFault(stack)
            ? WebLyricsRecoveryAction.Rebuild : WebLyricsRecoveryAction.None;
    }

    public static bool CanRecover(int recoveriesAlreadyDone) => recoveriesAlreadyDone < MaximumRecoveries;
}
