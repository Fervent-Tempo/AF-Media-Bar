// Modified from https://github.com/Kxnrl/NetEase-Cloud-Music-DiscordRPC

using System.Runtime.InteropServices;

namespace AFMediaBar.Classes.Services.Win32;

/// <summary>
/// 读取外部进程内存并执行签名扫描的内部工具。
/// Internal helper for reading external process memory and scanning signatures.
/// </summary>
internal static class Memory
{
    public static bool FindPattern(string pattern, int processId, nint address, out nint pointer)
    {
        var memory = new ProcessMemory(processId);

        var ntOffset = memory.ReadInt32(address, 0x3C);
        var ntHeader = address + ntOffset;

        // IMAGE
        var fileHeader = ntHeader + 4;
        var sections = memory.ReadInt16(ntHeader, 6);
        var sectionSize = memory.ReadInt16(fileHeader, 16);

        // OPT HEADER
        var optHeader = fileHeader + 20;
        var sectionHeader = optHeader + sectionSize;

        var cursor = sectionHeader;

        var pStart = nint.Zero;
        var memoryBlock = new List<byte>();

        for (var i = 0; i < sections; i++)
        {
            var name = memory.ReadInt64(cursor);

            if (name == 0x747865742E)
            {
                var offset = memory.ReadInt32(cursor, 12);

                pStart = address + offset;

                var size = memory.ReadInt32(cursor, 8);
                var buffer = memory.ReadBytes(pStart, size);
                memoryBlock.AddRange(buffer);

                break;
            }

            cursor += 40;
        }

        pointer = FindPattern(pattern, pStart, memoryBlock);

        return pointer != nint.Zero;
    }

    private static nint FindPattern(string pattern, nint pStart, List<byte> memoryBlock)
    {
        if (pattern.Length == 0 || pStart == nint.Zero || memoryBlock.Count == 0)
        {
            return nint.Zero;
        }

        var bytes = ParseSignature(pattern);
        var first = bytes[0];
        var result = nint.Zero;

        var range = memoryBlock.Count - bytes.Length;

        for (var i = 0; i < range; i++)
        {
            if (first != 0xFFFF)
            {
                i = memoryBlock.IndexOf((byte)first, i);

                if (i == -1)
                {
                    break;
                }
            }

            var found = true;

            for (var j = 1; j < bytes.Length; j++)
            {
                var wildcard = bytes[j] == 0xFFFF;
                var equals = bytes[j] == memoryBlock[i + j];

                if (wildcard || equals)
                {
                    continue;
                }

                found = false;

                break;
            }

            if (!found)
            {
                continue;
            }

            result = nint.Add(pStart, i);

            break;
        }

        return result;
    }

    private static ushort[] ParseSignature(string signature)
    {
        var bytesStr = signature.Split(' ').AsSpan();

        var bytes = new ushort[bytesStr.Length];

        for (var i = 0; i < bytes.Length; i++)
        {
            var str = bytesStr[i];

            if (str.Contains('?'))
            {
                bytes[i] = 0xFFFF;

                continue;
            }

            bytes[i] = Convert.ToByte(str, 16);
        }

        return bytes;
    }
}

/// <summary>
/// 封装进程句柄和基础类型读取操作。
/// Encapsulates a process handle and primitive typed reads.
/// </summary>
internal sealed class ProcessMemory : IDisposable
{
    private readonly nint _process;
    private bool _disposed;

    public ProcessMemory(nint process)
        => _process = process;

    public ProcessMemory(int processId)
        => _process = OpenProcess(0x0010, IntPtr.Zero, processId);

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
