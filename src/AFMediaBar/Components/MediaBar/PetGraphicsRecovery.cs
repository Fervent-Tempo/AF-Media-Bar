// 仅广播已被现有策略识别的 WebView2 图形故障；视图在下一帧释放自身资源。
namespace AFMediaBar.Components;

internal static class PetGraphicsRecovery
{
    internal static event Func<bool>? Requested;

    internal static bool Request()
    {
        var accepted = false;
        if (Requested is not { } handlers) return false;
        foreach (Func<bool> handler in handlers.GetInvocationList()) accepted |= handler();
        return accepted;
    }
}
