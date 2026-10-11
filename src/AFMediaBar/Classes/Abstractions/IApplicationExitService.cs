// 窗口和命令只表达退出意图；组合根执行关闭和服务收尾。
namespace AFMediaBar.Classes.Abstractions;

/// <summary>统一退出请求及关窗前的超时保护。</summary>
public interface IApplicationExitService
{
    void RequestExit(int exitCode = 0);
    void ArmExitProtection();
}
