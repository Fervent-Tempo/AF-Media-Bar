namespace AFMediaBar.Classes.Services.Updates;

/// <summary>
/// 与安装程序共享的安装协调互斥体。
///
/// 程序创建它只是为了让 Inno Setup 的 <c>AppMutex</c> 能在交互式安装/卸载时发现"AF Media Bar 正在运行"，
/// 从而给出"请先关闭程序"的提示，而不是在文件占用上失败。单实例门禁由
/// <see cref="AFMediaBar.Classes.Services.Startup.SingleInstanceGuard"/> 独立持有，安装前只需释放本互斥体。
///
/// 自动更新路径必须在启动安装包之前调用 <see cref="Release"/>：Inno Setup 在启动阶段检查该互斥体，
/// 静默模式下若它仍然存在会直接退出，于是更新会静默失败。释放之后由调用方锁存一次性标记来保证
/// 不会重复启动安装包。
/// The install-coordination mutex shared with the installer.
///
/// The application creates it only so Inno Setup's <c>AppMutex</c> can notice a running AF Media Bar during an
/// interactive install or uninstall, which turns a file-in-use failure into a clear "close the application"
/// message. The single-instance gate is held separately by <see cref="AFMediaBar.Classes.Services.Startup.SingleInstanceGuard"/>;
/// only this installer-facing mutex has to be released before installation.
///
/// The automatic update path must call <see cref="Release"/> before starting the installer: Inno Setup checks
/// this mutex while starting up and exits immediately when it still exists in silent mode, which would make the
/// update fail silently. The caller latches a one-shot flag afterwards so a second installer can never be started.
/// </summary>
public sealed class InstallCoordinatorMutex : IDisposable
{
    /// <summary>安装程序 <c>AppMutex</c> 指令里声明的同名互斥体。/ The mutex name declared by the installer's <c>AppMutex</c> directive.</summary>
    public const string MutexName = "AFMediaBar.InstallCoordinator";

    private readonly object _gate = new();
    private Mutex? _mutex;
    private bool _disposed;

    /// <summary>
    /// 创建互斥体；真正的创建发生在 <see cref="Acquire"/>，以便失败只影响更新链路而不影响启动。
    /// Creates the coordinator; the actual mutex is created by <see cref="Acquire"/> so a failure only affects
    /// the update path instead of startup.
    /// </summary>
    public InstallCoordinatorMutex()
    {
    }

    /// <summary>当前是否持有互斥体（安装程序据此判断程序是否在运行）。/ Whether the mutex is currently held, which is how the installer sees a running instance.</summary>
    public bool IsHeld
    {
        get
        {
            lock (_gate)
            {
                return _mutex is not null;
            }
        }
    }

    /// <summary>
    /// 创建并持有命名互斥体；可重复调用，已持有时直接返回。创建失败不抛出，仅返回 false。
    /// Creates and holds the named mutex; calling it again while held is a no-op. A creation failure returns
    /// false instead of throwing.
    /// </summary>
    /// <returns>调用结束时互斥体是否处于持有状态。/ Whether the mutex is held when the call returns.</returns>
    public bool Acquire()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            if (_mutex is not null)
            {
                return true;
            }

            try
            {
                // 不请求所有权：安装程序只检查内核对象是否存在，而保持一个打开的句柄就是"存在"。
                // Ownership is not requested: the installer only checks that the kernel object exists, and an
                // open handle is what keeps it existing.
                _mutex = new Mutex(initiallyOwned: false, MutexName);
                return true;
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"[Update] Install coordinator mutex could not be created: {exception.Message}");
                _mutex = null;
                return false;
            }
        }
    }

    /// <summary>
    /// 释放互斥体，使随后启动的安装程序不会因为"程序仍在运行"而拒绝安装。可重复调用。
    /// Releases the mutex so the installer started right afterwards does not refuse to run because the
    /// application still looks alive. Calling it twice is harmless.
    /// </summary>
    public void Release()
    {
        Mutex? mutex;
        lock (_gate)
        {
            mutex = _mutex;
            _mutex = null;
        }

        try
        {
            mutex?.Dispose();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"[Update] Install coordinator mutex could not be released: {exception.Message}");
        }
    }

    /// <summary>释放互斥体并阻止后续获取。/ Releases the mutex and stops later acquisitions.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Release();
    }
}
