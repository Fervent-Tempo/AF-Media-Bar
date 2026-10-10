// 可复用的重启确认呈现；关闭和取消均不构成重启授权。
namespace AFMediaBar.Classes.Abstractions;

/// <summary>在 UI 线程以当前语言确认普通重启，原因由资源键表达。</summary>
public interface IRestartConfirmationService
{
    Task<bool> ConfirmAsync(string reasonResourceKey, CancellationToken cancellationToken = default);
}
