using System.Diagnostics;
using System.IO;
using AFMediaBar.Interop;

namespace AFMediaBar.Services;

/// <summary>
/// 激活当前媒体来源的窗口，必要时尝试启动对应应用。
/// Activates the current media source window, or launches the matching app when needed.
/// </summary>
internal static class MediaSourceLauncherService
{
    internal static bool ShowOrLaunch(string sourceId, string sourceName)
    {
        var candidates = GetProcessNames(sourceId, sourceName).ToHashSet(
            StringComparer.OrdinalIgnoreCase);
        var processIds = new HashSet<uint>();
        var executablePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (!candidates.Contains(process.ProcessName))
                    {
                        continue;
                    }

                    processIds.Add(checked((uint)process.Id));
                    if (TryActivateWindow(process.MainWindowHandle))
                    {
                        return true;
                    }

                    var executable = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(executable) && File.Exists(executable))
                    {
                        executablePaths.Add(executable);
                    }
                }
                catch
                {
                    // 枚举后进程可能立即退出；单个进程失效不应中断后续查找。
                    // A process can exit after enumeration; one stale entry must not stop the search.
                }
            }
        }

        // 托盘应用的 MainWindowHandle 常为零，但隐藏的顶层窗口通常仍然存在。
        // Tray applications often report a zero MainWindowHandle while retaining a hidden top-level window.
        if (TryActivateWindow(FindBestTopLevelWindow(processIds)))
        {
            return true;
        }

        // 如果应用隐藏时销毁了窗口，重新运行其实际 EXE 可触发单实例唤醒逻辑。
        // If the app destroys its window while hidden, starting its actual EXE can trigger single-instance activation.
        foreach (var executable in executablePaths)
        {
            if (TryStartExecutable(executable))
            {
                return true;
            }
        }

        return TryLaunch(sourceId, sourceName);
    }

    private static nint FindBestTopLevelWindow(IReadOnlySet<uint> processIds)
    {
        if (processIds.Count == 0)
        {
            return nint.Zero;
        }

        var bestWindow = nint.Zero;
        var bestScore = long.MinValue;
        NativeMethods.EnumWindows((window, _) =>
        {
            if (NativeMethods.GetWindowThreadProcessId(window, out var processId) == 0 ||
                !processIds.Contains(processId) ||
                !NativeMethods.GetWindowRect(window, out var rect) ||
                rect.Width <= 0 ||
                rect.Height <= 0)
            {
                return true;
            }

            var area = (long)rect.Width * rect.Height;
            var extendedStyle = NativeMethods.GetWindowLongPtr(
                window,
                NativeMethods.GwlExStyle).ToInt64();
            var isToolWindow = (extendedStyle & NativeMethods.WsExToolWindow) != 0;
            var score = area +
                (isToolWindow ? 0 : 1L << 50) +
                (NativeMethods.IsWindowVisible(window) ? 1L << 60 : 0);
            if (score > bestScore)
            {
                bestScore = score;
                bestWindow = window;
            }

            return true;
        }, nint.Zero);
        return bestWindow;
    }

    private static bool TryActivateWindow(nint window)
    {
        if (window == nint.Zero || !NativeMethods.IsWindow(window))
        {
            return false;
        }

        NativeMethods.ShowWindow(window, NativeMethods.SwRestore);
        NativeMethods.SetForegroundWindow(window);
        return true;
    }

    private static bool TryStartExecutable(string executable)
    {
        try
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = true
            };
            var workingDirectory = Path.GetDirectoryName(executable);
            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                startInfo.WorkingDirectory = workingDirectory;
            }

            Process.Start(startInfo);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<string> GetProcessNames(string sourceId, string sourceName)
    {
        var fileName = Path.GetFileNameWithoutExtension(sourceId);
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            yield return fileName;
        }

        var mappings = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["网易云音乐"] = ["cloudmusic"],
            ["QQ音乐"] = ["qqmusic"],
            ["酷狗音乐"] = ["kugou", "KuGou"],
            ["Spotify"] = ["spotify"],
            ["Google Chrome"] = ["chrome"],
            ["Microsoft Edge"] = ["msedge"],
            ["Firefox"] = ["firefox"],
            ["VLC"] = ["vlc"],
            ["PotPlayer"] = ["PotPlayerMini64", "PotPlayerMini"],
            ["Windows Media Player"] = ["Microsoft.Media.Player", "wmplayer"],
            ["mpv"] = ["mpv"],
            ["foobar2000"] = ["foobar2000"]
        };

        if (mappings.TryGetValue(sourceName, out var names))
        {
            foreach (var name in names)
            {
                yield return name;
            }
        }
    }

    private static bool TryLaunch(string sourceId, string sourceName)
    {
        try
        {
            if (sourceName == "网易云音乐")
            {
                var knownPaths = new[]
                {
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                        "NetEase",
                        "CloudMusic",
                        "cloudmusic.exe"),
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                        "NetEase",
                        "CloudMusic",
                        "cloudmusic.exe"),
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "NetEase",
                        "CloudMusic",
                        "cloudmusic.exe")
                };
                var executable = knownPaths.FirstOrDefault(File.Exists);
                if (executable is not null && TryStartExecutable(executable))
                {
                    return true;
                }
            }

            if (sourceId.Contains('!'))
            {
                Process.Start(new ProcessStartInfo($"shell:AppsFolder\\{sourceId}")
                {
                    UseShellExecute = true
                });
                return true;
            }
        }
        catch
        {
            // 打开来源只是便捷操作；失败时保留已有媒体控制功能。
            // Opening the source is optional; existing media controls remain usable on failure.
        }

        return false;
    }
}
