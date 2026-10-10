// 开发者入口与会话取消契约；不拥有测试窗口或生产恢复状态。
namespace AFMediaBar.Classes.Abstractions;

/// <summary>显式开启的开发者工具入口，关闭时使旧请求失效。</summary>
public interface IDeveloperModeService
{
    bool IsEnabled { get; }
    int Generation { get; }
    CancellationToken SessionToken { get; }
    event EventHandler? EnabledChanged;
    event EventHandler? OpenRequested;
    void SetEnabled(bool enabled);
    void OpenTools();
}
