namespace AFMediaBar.Classes.Services.Startup;

/// <summary>
/// 在当前 Windows 会话中保持命名互斥体句柄，阻止第二个 AF Media Bar 进程启动。
/// Keeps a named mutex handle in the current Windows session so a second AF Media Bar process cannot start.
/// </summary>
internal sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\AFMediaBar.SingleInstance";
    private readonly Mutex _mutex;

    private SingleInstanceGuard(Mutex mutex) => _mutex = mutex;

    /// <summary>
    /// 在启动副作用之前尝试取得进程级门禁。/ Tries to acquire the process gate before startup has side effects.
    /// </summary>
    /// <param name="guard">首次启动时持有的句柄；重复启动时为 null。/ The handle held by the first instance, or null for a duplicate.</param>
    /// <returns>当前进程是否是首个实例。/ Whether this process is the first instance.</returns>
    internal static bool TryAcquire(out SingleInstanceGuard? guard) => TryAcquire(MutexName, out guard);

    internal static bool TryAcquire(string name, out SingleInstanceGuard? guard)
    {
        // 只保持内核对象存在，不取得线程所有权；退出时可在任意线程安全地释放句柄。
        // Keeping the kernel object alive is enough; no thread ownership is needed during shutdown.
        var mutex = new Mutex(initiallyOwned: false, name, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            guard = null;
            return false;
        }

        guard = new SingleInstanceGuard(mutex);
        return true;
    }

    /// <summary>释放进程门禁。/ Releases the process gate.</summary>
    public void Dispose() => _mutex.Dispose();
}
