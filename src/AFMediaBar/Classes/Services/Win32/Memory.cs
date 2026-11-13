// Modified from https://github.com/Kxnrl/NetEase-Cloud-Music-DiscordRPC

using System.Runtime.InteropServices;
using System.Text;

namespace AFMediaBar.Classes.Services.Win32;

/// <summary>
/// 封装进程句柄和基础类型读取操作。模式扫描已迁到 Media/Readers/MemoryPatternScanner。
/// Encapsulates a process handle and primitive typed reads. Pattern scanning now lives in Media/Readers/MemoryPatternScanner.
/// </summary>
internal sealed class ProcessMemory : IDisposable
{
    private readonly nint _process;
    private bool _disposed;

    public ProcessMemory(nint process)
        => _process = process;

    public ProcessMemory(int processId)
        => _process = OpenProcess(0x0010, IntPtr.Zero, processId);

    /// <summary>读取句柄是否成功打开；0 表示 OpenProcess 失败。 / Whether OpenProcess succeeded; zero means failure.</summary>
    internal bool IsOpen => _process != nint.Zero;

    /// <summary>原始句柄，仅供同进程的 VirtualQueryEx 等只读检查使用。 / Raw handle for read-only checks such as VirtualQueryEx.</summary>
    internal nint Handle => _process;

    /// <summary>尝试打开只读句柄并保留 Win32 错误码，供诊断使用。 / Opens a read-only handle and keeps the Win32 error for diagnostics.</summary>
    internal static ProcessMemory? TryOpen(int processId, out int error)
    {
        // VirtualQueryEx 需要 PROCESS_QUERY_INFORMATION；只申请 VM_READ 会让范围扫描静默返回 0 个区域。
        // VirtualQueryEx needs PROCESS_QUERY_INFORMATION; requesting only VM_READ makes range scans silently return zero regions.
        var handle = OpenProcess(0x0410, IntPtr.Zero, processId);
        error = handle == nint.Zero ? Marshal.GetLastWin32Error() : 0;
        if (handle == nint.Zero)
            return null;
        return new ProcessMemory(handle);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CloseHandle(_process);
    }

    public byte[] ReadBytes(IntPtr offset, int length)
    {
        var bytes = new byte[length];
        ReadProcessMemory(_process, offset, bytes, length, IntPtr.Zero);

        return bytes;
    }

    // 上面的读取不检查 ReadProcessMemory 的返回值：失败时静默返回全零。对"地址必然有效"的既有调用（网易云签名扫描）
    // 这没问题，但走多级指针链时任何一环都可能失效，调用方必须能区分"读到 0"和"根本没读到"，所以这里有带校验的版本。
    // The reads above do not check ReadProcessMemory's return: a failure silently yields zeros. That is fine for existing callers whose
    // addresses are known-valid (the NetEase signature scan), but when walking a multi-level pointer chain any link can fail, and the caller
    // must tell "read a zero" from "read nothing" — hence these checked variants.
    public bool TryReadInt32(IntPtr address, out int value)
    {
        value = 0;
        if (!TryReadProcessMemory(address, 4, out var bytes))
        {
            return false;
        }

        value = BitConverter.ToInt32(bytes, 0);
        return true;
    }

    public bool TryReadInt64(IntPtr address, out long value)
    {
        value = 0;
        if (!TryReadProcessMemory(address, 8, out var bytes))
        {
            return false;
        }

        value = BitConverter.ToInt64(bytes, 0);
        return true;
    }

    public bool TryReadDouble(IntPtr address, out double value)
    {
        value = 0;
        if (!TryReadProcessMemory(address, 8, out var bytes))
        {
            return false;
        }

        value = BitConverter.ToDouble(bytes, 0);
        return true;
    }

    /// <summary>
    /// 读取 MSVC std::string（32 字节布局：数据指针/容量/长度）；长度与容量必须自洽。
    /// Reads an MSVC std::string (32-byte layout: data pointer/capacity/length); length and capacity must agree.
    /// </summary>
    internal bool TryReadStdString(IntPtr address, int maximumLength, out string value)
    {
        value = string.Empty;
        if (maximumLength <= 0 || !TryReadProcessMemory(address, 0x20, out var header))
            return false;

        var length = BitConverter.ToInt64(header, 0x10);
        var capacity = BitConverter.ToInt64(header, 0x18);
        if (length <= 0 || length > maximumLength || capacity < length)
            return false;

        byte[] data;
        if (length <= 15)
        {
            data = header[..(int)length];
        }
        else
        {
            var pointer = BitConverter.ToInt64(header, 0);
            if (pointer == 0 || !TryReadProcessMemory((IntPtr)pointer, (int)length, out data))
                return false;
        }

        try
        {
            value = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(data);
            return value.Length > 0 && !value.Contains('\0');
        }
        catch (DecoderFallbackException)
        {
            value = string.Empty;
            return false;
        }
    }

    /// <summary>带校验的任意长度只读；短读返回 false。 / Checked arbitrary-length read; short reads return false.</summary>
    internal bool TryReadBytes(IntPtr address, int size, out byte[] bytes)
    {
        if (size <= 0 || size > 16 * 1024 * 1024)
        {
            bytes = [];
            return false;
        }

        return TryReadProcessMemory(address, size, out bytes);
    }

    private bool TryReadProcessMemory(IntPtr address, int size, out byte[] bytes)
    {
        bytes = new byte[size];
        return ReadProcessMemory(_process, address, bytes, size, out var read) && read == size;
    }

    public float ReadFloat(IntPtr address, int offset = 0)
        => BitConverter.ToSingle(ReadBytes(IntPtr.Add(address, offset), 4), 0);

    public double ReadDouble(IntPtr address, int offset = 0)
        => BitConverter.ToDouble(ReadBytes(IntPtr.Add(address, offset), 8), 0);

    public long ReadInt64(IntPtr address, int offset = 0)
        => BitConverter.ToInt64(ReadBytes(IntPtr.Add(address, offset), 8), 0);

    public ulong ReadUInt64(IntPtr address, int offset = 0)
        => BitConverter.ToUInt64(ReadBytes(IntPtr.Add(address, offset), 8), 0);

    public short ReadInt16(IntPtr address, int offset = 0)
        => BitConverter.ToInt16(ReadBytes(IntPtr.Add(address, offset), 2), 0);

    public int ReadInt32(IntPtr address, int offset = 0)
        => BitConverter.ToInt32(ReadBytes(IntPtr.Add(address, offset), 4), 0);

    public uint ReadUInt32(IntPtr address, int offset = 0)
        => BitConverter.ToUInt32(ReadBytes(IntPtr.Add(address, offset), 4), 0);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr pHandle, IntPtr address, byte[] buffer, int size, IntPtr bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr pHandle, IntPtr address, byte[] buffer, int size, out int bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int dwDesiredAccess, IntPtr bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}
