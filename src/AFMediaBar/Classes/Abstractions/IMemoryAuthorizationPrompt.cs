// 授权交互契约；服务不依赖窗口，取消不等同于取得系统权限。
namespace AFMediaBar.Classes.Abstractions;

/// <summary>用户本次选择的系统清理授权方式。</summary>
public enum MemoryAuthorizationChoice { Cancel, Once, Remember }

/// <summary>设置窗口和性能菜单共用的授权选择与手动操作结果提示。</summary>
public interface IMemoryAuthorizationPrompt
{
    Task<MemoryAuthorizationChoice> ChooseAsync(bool persistentRequired, CancellationToken token = default);
    Task ShowResultAsync(string title, string message, string? explanation = null, CancellationToken token = default);
}
