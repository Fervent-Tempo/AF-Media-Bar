namespace AFMediaBar.Classes.Abstractions;

/// <summary>
/// 任务栏宿主窗口可调用的基础设施动作，不包含媒体业务逻辑。
/// Infrastructure actions exposed to the taskbar host window without media business logic.
/// </summary>
public interface ITaskbarWindowHostActions
{
    /// <summary>指示任务栏环境是否正在恢复。/ Indicates whether taskbar environment recovery is active.</summary>
    bool IsEnvironmentRecovering { get; }

    /// <summary>请求重建任务栏宿主。/ Requests taskbar host recreation.</summary>
    void RecreateTaskbarWindow();

    /// <summary>请求安全重载任务栏宿主。/ Requests a safe taskbar host reload.</summary>
    void RequestTaskbarHostReload();
}
