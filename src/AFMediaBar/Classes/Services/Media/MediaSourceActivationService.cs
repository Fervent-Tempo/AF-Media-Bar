using System.Diagnostics;
using System.IO;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Resources;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 将媒体来源标识解析为可激活的窗口、应用或可执行文件。
/// Resolves a media source identifier to an activatable window, app, or executable.
/// </summary>
public sealed class MediaSourceActivationService
{
    private readonly MediaSourceProcessResolver _processResolver;

    /// <summary>
    /// 创建来源激活器，并复用音频来源解析规则定位候选进程。
    /// Creates the source activator and reuses audio-source resolution rules to locate candidate processes.
    /// </summary>
    public MediaSourceActivationService(MediaSourceProcessResolver processResolver)
    {
        _processResolver = processResolver;
    }

    /// <summary>
    /// 尝试恢复并前置来源进程的主窗口；找不到安全窗口时保持无操作。
    /// Attempts to restore and foreground the source process main window, becoming a no-op when no safe window is found.
    /// </summary>
    public void Activate(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return;
        }

        var executablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var processName in ResolveActivationProcessNames(sourceId))
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        var handle = process.MainWindowHandle;
                        if (handle == IntPtr.Zero)
                        {
                            var executablePath = process.MainModule?.FileName;
                            if (!string.IsNullOrWhiteSpace(executablePath))
                            {
                                executablePaths.Add(executablePath);
                            }

                            continue;
                        }

                        NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);
                        NativeMethods.SetForegroundWindow(handle);
                        return;
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or
                                                   System.ComponentModel.Win32Exception or
                                                   NotSupportedException)
                    {
                        Debug.WriteLine($"[MediaSourceActivationService] Process exited while activating: {ex.Message}");
                    }
                }
            }
        }

        try
        {
            if (sourceId.Contains('!'))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"shell:AppsFolder\\{sourceId}",
                    UseShellExecute = true
                });
                return;
            }

            var executablePath = executablePaths.FirstOrDefault(File.Exists);
            if (executablePath is not null)
            {
                Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = true });
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Debug.WriteLine($"[MediaSourceActivationService] Activate source failed: {ex}");
        }
    }

    /// <summary>解析当前已发现来源的安全启动描述。 / Resolves a safe launch descriptor for a currently discovered source.</summary>
    public MediaSourceDescriptor Describe(string sourceId)
    {
        var displayName = MediaSourceNameFormatter.GetDisplayName(sourceId, Translations.Get("Service.MediaSource.Unknown"));
        if (string.IsNullOrWhiteSpace(sourceId))
            return new MediaSourceDescriptor(string.Empty, displayName, null, null);
        if (sourceId.Contains('!'))
            return new MediaSourceDescriptor(sourceId, displayName, QuickLaunchTargetKind.AppUserModelId, sourceId);

        foreach (var processName in ResolveActivationProcessNames(sourceId))
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        var path = process.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                            return new MediaSourceDescriptor(sourceId, displayName, QuickLaunchTargetKind.Executable, Path.GetFullPath(path));
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                    {
                        Debug.WriteLine($"[MediaSourceActivationService] Could not resolve launch path: {ex.Message}");
                    }
                }
            }
        }

        return new MediaSourceDescriptor(sourceId, displayName, null, null);
    }

    /// <summary>激活或启动一个经过设置归一化的快速启动项。 / Activates or starts a normalized quick-launch entry.</summary>
    public Task<QuickLaunchResult> LaunchAsync(QuickLaunchEntry entry, CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return LaunchCore(entry);
        }, cancellationToken);

    private QuickLaunchResult LaunchCore(QuickLaunchEntry entry)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(entry.SourceId) && TryActivateRunning(entry.SourceId))
                return QuickLaunchResult.Success;
            var target = entry.Target.Trim();
            switch (entry.Kind)
            {
                case QuickLaunchTargetKind.AppUserModelId when target.Contains('!'):
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"shell:AppsFolder\\{target}",
                        UseShellExecute = true
                    });
                    return QuickLaunchResult.Success;
                case QuickLaunchTargetKind.Executable
                    when File.Exists(target) && string.Equals(Path.GetExtension(target), ".exe", StringComparison.OrdinalIgnoreCase):
                case QuickLaunchTargetKind.Shortcut
                    when File.Exists(target) && string.Equals(Path.GetExtension(target), ".lnk", StringComparison.OrdinalIgnoreCase):
                    Process.Start(new ProcessStartInfo(Path.GetFullPath(target)) { UseShellExecute = true });
                    return QuickLaunchResult.Success;
                default:
                    return QuickLaunchResult.InvalidTarget;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Debug.WriteLine($"[MediaSourceActivationService] Quick launch failed: {ex}");
            return QuickLaunchResult.Failed;
        }
    }

    private bool TryActivateRunning(string sourceId)
    {
        foreach (var processName in ResolveActivationProcessNames(sourceId))
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        if (process.MainWindowHandle == IntPtr.Zero)
                            continue;
                        NativeMethods.ShowWindow(process.MainWindowHandle, NativeMethods.SW_RESTORE);
                        NativeMethods.SetForegroundWindow(process.MainWindowHandle);
                        return true;
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                    {
                        Debug.WriteLine($"[MediaSourceActivationService] Process exited while activating: {ex.Message}");
                    }
                }
            }
        }
        return false;
    }

    private IEnumerable<string> ResolveActivationProcessNames(string sourceId)
    {
        var names = _processResolver.ResolveProcessNames(sourceId);
        // 桌面应用也可用不带 .exe 的进程名注册 SMTC；只补充精确名称，不猜测相似应用。
        if (!string.IsNullOrWhiteSpace(sourceId) && !sourceId.Contains('!') &&
            sourceId.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
            !sourceId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return names.Append(sourceId).Distinct(StringComparer.OrdinalIgnoreCase);
        return names;
    }

}
