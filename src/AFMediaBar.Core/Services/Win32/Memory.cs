// Modified from https://github.com/Kxnrl/NetEase-Cloud-Music-DiscordRPC


using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace AFMediaBar.Services.Win32Api;

internal static class Memory
{
    public static bool FindPattern(string pattern, int processId, nint address, out nint pointer)
    {
        var memory = new ProcessMemory(processId);

        var ntOffset = memory.ReadInt32(address, 0x3C);
        var ntHeader = address + ntOffset;

        // IMAGE
        var fileHeader  = ntHeader + 4;
        var sections    = memory.ReadInt16(ntHeader,   6);
        var sectionSize = memory.ReadInt16(fileHeader, 16);

        // OPT HEADER
        var optHeader     = fileHeader + 20;
        var sectionHeader = optHeader  + sectionSize;

        var cursor = sectionHeader;

        var pStart      = nint.Zero;
        var memoryBlock = new List<byte>();

        for (var i = 0; i < sections; i++)
        {
            var name = memory.ReadInt64(cursor);

            if (name == 0x747865742E)
            {
                var offset = memory.ReadInt32(cursor, 12);

                pStart = address + offset;

                var size   = memory.ReadInt32(cursor, 8);
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

        var bytes  = ParseSignature(pattern);
        var first  = bytes[0];
        var result = nint.Zero;

        var range = memoryBlock.Count - bytes.Length;

        for (var i = 0; i < range; i++)
        {
            if (first != 0xFFFF)
            {
                i = memoryBlock.IndexOf((byte) first, i);

                if (i == -1)
                {
                    break;
                }
            }

            var found = true;

            for (var j = 1; j < bytes.Length; j++)
            {
                var wildcard = bytes[j] == 0xFFFF;
                var equals   = bytes[j] == memoryBlock[i + j];

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
        var bytesStr = signature.Split(' ')
                                .AsSpan();

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

internal sealed class ProcessMemory
{
    private readonly nint _process;

    public ProcessMemory(nint process)
        => _process = process;

    public ProcessMemory(int processId)
        => _process = OpenProcess(0x0010, IntPtr.Zero, processId);

    public byte[] ReadBytes(IntPtr offset, int length)
    {
        var bytes = new byte[length];
        ReadProcessMemory(_process, offset, bytes, length, IntPtr.Zero);

        return bytes;
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
    private static extern IntPtr OpenProcess(int dwDesiredAccess, IntPtr bInheritHandle, int dwProcessId);
}
