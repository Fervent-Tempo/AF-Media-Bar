// 只读特征码扫描：优先扫描模块可读区域，按块读取并保留跨块重叠；不写进程、不执行代码。
using System.Runtime.InteropServices;
using AFMediaBar.Classes.Services.Win32;

namespace AFMediaBar.Classes.Services.Media.Readers;

/// <summary>在目标进程模块内查找带通配符的字节模式。 / Finds wildcard byte patterns inside a target module.</summary>
internal static class MemoryPatternScanner
{
    private const int ChunkBytes = 1024 * 1024;
    private const uint MemCommit = 0x1000;
    private const uint MemPrivate = 0x20000;
    private const uint PageGuard = 0x100;
    private const uint PageNoAccess = 0x01;
    private const uint ImageSizeLimit = 512u * 1024 * 1024;

    /// <summary>扫描模块可读区域，返回最多 maxMatches 个命中地址；取消或读取失败时返回已找到的结果。</summary>
    internal static IReadOnlyList<nint> Find(
        ProcessMemory memory,
        nint moduleBase,
        int moduleSize,
        string pattern,
        int maxMatches = 4,
        int maxBytes = 64 * 1024 * 1024,
        CancellationToken cancellationToken = default)
    {
        var matches = new List<nint>();
        var bytes = ParsePattern(pattern);
        if (bytes.Length == 0 || moduleBase == nint.Zero || moduleSize <= 0 || moduleSize > ImageSizeLimit)
            return matches;

        var moduleEnd = moduleBase + moduleSize;
        var cursor = moduleBase;
        var scanned = 0;
        while (cursor < moduleEnd && matches.Count < maxMatches && scanned < maxBytes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (VirtualQueryEx(memory.Handle, cursor, out var region, (nuint)Marshal.SizeOf<MemoryBasicInformation>()) == 0)
                break;

            var regionBase = region.BaseAddress;
            var regionEnd = regionBase + (nint)(long)region.RegionSize;
            if (regionEnd <= cursor)
                break;
            var start = (nint)Math.Max((long)cursor, (long)regionBase);
            var end = (nint)Math.Min((long)moduleEnd, (long)regionEnd);

            if (region.State == MemCommit && (region.Protect & PageGuard) == 0 &&
                (region.Protect & 0xFF) != PageNoAccess && IsReadable(region.Protect))
            {
                ScanRange(memory, start, end, bytes, matches, maxMatches, maxBytes, ref scanned, cancellationToken);
            }

            cursor = regionEnd;
        }

        return matches;
    }

    private static void ScanRange(
        ProcessMemory memory,
        nint start,
        nint end,
        byte[] pattern,
        List<nint> matches,
        int maxMatches,
        int maxBytes,
        ref int scanned,
        CancellationToken cancellationToken)
    {
        if (start >= end)
            return;

        var overlap = pattern.Length - 1;
        var position = start;
        byte[]? carry = null;
        while (position < end && matches.Count < maxMatches && scanned < maxBytes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = (long)(end - position);
            var chunkSize = (int)Math.Min(ChunkBytes, remaining);
            if (!memory.TryReadBytes(position, chunkSize, out var chunk))
            {
                // 页在读取时被释放或保护变化：跳过这一块，下一次发现会重新定位。
                position += chunkSize;
                scanned += chunkSize;
                continue;
            }

            var combined = carry is { Length: > 0 } ? new byte[carry.Length + chunk.Length] : chunk;
            if (carry is { Length: > 0 })
            {
                Buffer.BlockCopy(carry, 0, combined, 0, carry.Length);
                Buffer.BlockCopy(chunk, 0, combined, carry.Length, chunk.Length);
            }

            var combinedBase = position - (carry?.Length ?? 0);
            for (var index = 0; index + pattern.Length <= combined.Length && matches.Count < maxMatches; index++)
            {
                if (combined[index] != pattern[0])
                    continue;
                var found = true;
                for (var offset = 1; offset < pattern.Length; offset++)
                {
                    if (pattern[offset] != 0xFF && combined[index + offset] != pattern[offset])
                    {
                        found = false;
                        break;
                    }
                }

                if (found)
                    matches.Add(combinedBase + index);
            }

            if (combined.Length > overlap)
            {
                carry ??= new byte[overlap];
                Buffer.BlockCopy(combined, combined.Length - overlap, carry, 0, overlap);
            }
            else
            {
                carry = combined;
            }

            position += chunkSize;
            scanned += chunkSize;
        }
    }

    private static bool IsReadable(uint protect)
    {
        var value = protect & 0xFF;
        return value is 0x02 or 0x04 or 0x08 or 0x20 or 0x40 or 0x80;
    }

    private static byte[] ParsePattern(string pattern)
    {
        var parts = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = new byte[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            var part = parts[index];
            result[index] = part is "?" or "??" ? (byte)0xFF : Convert.ToByte(part, 16);
        }

        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryBasicInformation
    {
        public nint BaseAddress;
        public nint AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public nuint RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nuint VirtualQueryEx(nint process, nint address, out MemoryBasicInformation information, nuint length);
}
