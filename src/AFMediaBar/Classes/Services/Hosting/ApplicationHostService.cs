// 执行组合根提供的 UI 激活动作，不解析窗口或承担业务资源生命周期。
using Microsoft.Extensions.Hosting;

namespace AFMediaBar.Classes.Services;

/// <summary>在通用宿主启动时激活应用外壳。</summary>
public sealed class ApplicationHostService(Action activate) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        activate();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
