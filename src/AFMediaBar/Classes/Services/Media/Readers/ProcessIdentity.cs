// 播放器进程身份：先按名称筛选，再核对真实映像名，最后才申请只读内存权限。
using System.ComponentModel;
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

    /// <summary>按 exe 名筛选候选，同时保留无法检查映像的进程 ID，供调用方区分进程缺席与检查失败。</summary>
    internal static (IReadOnlyList<Candidate> Candidates, int? UnreadableProcessId) Find(string executableName, int maximum)
    {
        var name = Path.GetFileNameWithoutExtension(executableName);
        var result = new List<Candidate>();
        int? unreadableProcessId = null;
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
                if (path is null)
                {
                    unreadableProcessId ??= process.Id;
                    continue;
                }
                if (string.Equals(Path.GetFileName(path), executableName, StringComparison.OrdinalIgnoreCase))
                    result.Add(new(process.Id, executableName, path));
            }
            catch (InvalidOperationException)
            {
                // 进程在检查映像前退出。
            }
            catch (Exception exception) when (exception is Win32Exception or NotSupportedException)
            {
                unreadableProcessId ??= process.Id;
            }
            finally
            {
                process.Dispose();
            }
        }

        return (result, unreadableProcessId);
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
            Win32Exception or NotSupportedException)
        {
            return false;
        }
    }
}
