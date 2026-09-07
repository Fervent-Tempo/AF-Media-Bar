using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace AFMediaBar.Classes.Services.Audio;

/// <summary>
/// 通过受限查询权限解析音频会话进程，避免 Process.MainModule 对受保护进程抛出异常。
/// Resolves audio-session processes with limited query access, avoiding Process.MainModule exceptions for protected processes.
/// </summary>
public sealed class AudioProcessInfoService
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    /// <summary>尝试读取进程可执行文件路径。 / Tries to read the process executable path.</summary>
    public string? GetExecutablePath(uint processId)
    {
        if (processId == 0 || processId > int.MaxValue)
        {
            return null;
        }

        using var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle.IsInvalid)
        {
            return null;
        }

        var capacity = 32768;
        var path = new StringBuilder(capacity);
        return QueryFullProcessImageName(handle, 0, path, ref capacity)
            ? path.ToString()
            : null;
    }

    /// <summary>尝试读取不含扩展名的进程名。 / Tries to read the process name without its extension.</summary>
    public string? GetProcessName(uint processId)
    {
        var path = GetExecutablePath(processId);
        return string.IsNullOrWhiteSpace(path) ? null : Path.GetFileNameWithoutExtension(path);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        SafeProcessHandle process,
        uint flags,
        StringBuilder executablePath,
        ref int size);
}
