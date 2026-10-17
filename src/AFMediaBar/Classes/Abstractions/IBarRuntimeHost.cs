// 宿主资源的事务边界；窗口实现拥有自己的 HWND、弹层、订阅和计时器。
namespace AFMediaBar.Classes.Abstractions;

/// <summary>可准备、挂起、恢复和永久释放的媒体栏宿主。</summary>
public interface IBarRuntimeHost : IDisposable
{
    Task ActivateAsync(CancellationToken cancellationToken);
    void Suspend();
    void Resume();
}
