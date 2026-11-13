// 播放器进程身份：先按名称筛选，再核对真实映像名，最后才申请只读内存权限。
using System.Diagnostics;
using System.IO;

namespace AFMediaBar.Classes.Services.Media.Readers;

/// <summary>
/// 只读播放器进程发现与身份核对；不写进程、不注入，也不信任 PID 名称。
/// Read-only player process discovery and identity checks; no writes, injection, or PID-name trust.
/// </summary>
internal static class ProcessIdentity
{
    internal readonly record struct Candidate(int ProcessId, string ExecutableName, string ExecutablePath);

    /// <summary>按 exe 名筛选候选；PID 复用由 <see cref="IsExpectedImage"/> 在附加前复核。</summary>
    internal static IReadOnlyList<Candidate> Find(string executableName, int maximum = 16)
    {
        var name = Path.GetFileNameWithoutExtension(executableName);
        var result = new List<Candidate>();
        foreach (var process in Process.GetProcessesByName(name))
        {
            if (result.Count >= maximum)
            {
                process.Dispose();
                break;
            }

            try
            {
                var path = process.MainModule?.FileName;
                if (path is null || !string.Equals(Path.GetFileName(path), executableName, StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(new(process.Id, executableName, path));
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // 进程可能已退出或模块枚举被拒绝；不作为可用候选。
            }
            finally
            {
                process.Dispose();
            }
        }

        return result;
    }

    /// <summary>复核 PID 当前是否仍是目标映像，避免 PID 复用后读到其他软件。</summary>
    internal static bool IsExpectedImage(int processId, string executableName, out string? executablePath)
    {
        executablePath = null;
        try
        {
            using var process = Process.GetProcessById(processId);
            var path = process.MainModule?.FileName;
            if (path is null || !string.Equals(Path.GetFileName(path), executableName, StringComparison.OrdinalIgnoreCase))
                return false;
            executablePath = path;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
            System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return false;
        }
    }
}
