// 时长链失效时，只读扫描酷狗私有内存中的歌曲 JSON，取回文件名与时长；不记录原始内容。
// 思路参考 Renly1994/xiaotang-daozai 的 kugou_metadata.rs（MIT）；扫描范围/退回策略按本项目收窄。
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using AFMediaBar.Classes.Services.Win32;

namespace AFMediaBar.Classes.Services.Media.Sources.KuGou;

/// <summary>
/// 酷狗时长链的兜底：扫描已提交的私有可读区域，查找 <c>type:"song"</c> 的 JSON 片段并校验文件名/哈希/时长。
/// Fallback for the KuGou duration chain: scans committed private readable regions for a <c>type:"song"</c> JSON fragment and validates
/// filename, hash, and duration.
/// </summary>
internal sealed class KuGouMetadataProbe(ProcessMemory memory)
{
    private const string CaptionSuffix = " - 酷狗音乐";
    private const int ChunkBytes = 1024 * 1024;
    private const int OverlapBytes = 8 * 1024;
    private const int ParseBytes = 8 * 1024;
    private const long MaxScanBytes = 128L * 1024 * 1024;
    private static readonly TimeSpan MaxScanTime = TimeSpan.FromSeconds(1.5);
    private static readonly byte[] DurationMarker = Encoding.UTF8.GetBytes("\"duration\":");
    private static readonly TimeSpan FirstRetry = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LaterRetry = TimeSpan.FromSeconds(30);

    private readonly ProcessMemory _memory = memory;
    private string? _cachedFilename;
    private double _cachedDuration;
    private DateTimeOffset _nextAttempt;
    private int _failures;

    /// <summary>为当前窗口标题尝试读取时长；失败时按 5 秒/30 秒退避，不阻塞轮询。</summary>
    internal bool TryGetDuration(string caption, out double duration)
    {
        duration = 0;
        var trimmed = caption.Trim();
        if (!trimmed.EndsWith(CaptionSuffix, StringComparison.Ordinal))
            return false;
        var filename = trimmed[..^CaptionSuffix.Length].Trim();
        if (filename.Length < 3)
            return false;

        if (string.Equals(_cachedFilename, filename, StringComparison.Ordinal) && _cachedDuration > 0)
        {
            duration = _cachedDuration;
            return true;
        }

        var now = DateTimeOffset.UtcNow;
        if (now < _nextAttempt)
            return false;
        _nextAttempt = now + (_failures == 0 ? FirstRetry : LaterRetry);
        if (!TryScan(filename, out duration))
        {
            _failures = Math.Min(_failures + 1, 2);
            return false;
        }

        _failures = 0;
        _cachedFilename = filename;
        _cachedDuration = duration;
        return true;
    }

    private bool TryScan(string expectedFilename, out double duration)
    {
        duration = 0;
        var stopwatch = Stopwatch.StartNew();
        long scanned = 0;
        var address = nint.Zero;
        byte[]? carry = null;
        while (scanned < MaxScanBytes && stopwatch.Elapsed < MaxScanTime)
        {
            if (VirtualQueryEx(_memory.Handle, address, out var region, (nuint)Marshal.SizeOf<MemoryBasicInformation>()) == 0)
                break;

            var regionBase = region.BaseAddress;
            var regionSize = (long)region.RegionSize;
            if (regionSize <= 0)
                break;
            var regionEnd = regionBase + (nint)regionSize;
            if (regionEnd <= address)
                break;

            if (region.State == 0x1000 && region.Type == 0x20000 && IsReadable(region.Protect) &&
                (region.Protect & 0x100) == 0)
            {
                var position = regionBase;
                while (position < regionEnd && scanned < MaxScanBytes && stopwatch.Elapsed < MaxScanTime)
                {
                    var remaining = regionEnd - position;
                    var chunkSize = (int)Math.Min(ChunkBytes, (long)remaining);
                    if (_memory.TryReadBytes(position, chunkSize, out var chunk))
                    {
                        var combined = carry is { Length: > 0 } ? new byte[carry.Length + chunk.Length] : chunk;
                        if (carry is { Length: > 0 })
                        {
                            Buffer.BlockCopy(carry, 0, combined, 0, carry.Length);
                            Buffer.BlockCopy(chunk, 0, combined, carry.Length, chunk.Length);
                        }

                        var baseAddress = position - (carry?.Length ?? 0);
                        if (TryParseChunk(combined, baseAddress, expectedFilename, out duration))
                            return true;

                        var keep = Math.Min(OverlapBytes, combined.Length);
                        carry ??= new byte[keep];
                        if (carry.Length != keep) carry = new byte[keep];
                        Buffer.BlockCopy(combined, combined.Length - keep, carry, 0, keep);
                    }

                    position += chunkSize;
                    scanned += chunkSize;
                }
            }

            address = regionEnd;
            carry = null;
        }

        return false;
    }

    private bool TryParseChunk(byte[] data, nint baseAddress, string expectedFilename, out double duration)
    {
        duration = 0;
        var search = 0;
        while (search < data.Length)
        {
            var marker = data.AsSpan(search).IndexOf(DurationMarker);
            if (marker < 0)
                return false;
            var markerIndex = search + marker;
            search = markerIndex + 1;
            var start = Math.Max(0, markerIndex - 4096);
            var objectStart = data.AsSpan(start, markerIndex - start).LastIndexOf((byte)'{');
            if (objectStart < 0)
                continue;
            objectStart += start;
            var objectEnd = FindObjectEnd(data, objectStart);
            if (objectEnd <= objectStart)
                continue;
            try
            {
                using var document = JsonDocument.Parse(data.AsMemory(objectStart, objectEnd - objectStart));
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("type", out var type) || type.GetString() != "song" ||
                    !root.TryGetProperty("filename", out var filename) ||
                    !string.Equals(filename.GetString()?.Trim(), expectedFilename, StringComparison.OrdinalIgnoreCase) ||
                    !root.TryGetProperty("hash", out var hash) || !IsHash(hash.GetString()) ||
                    !root.TryGetProperty("duration", out var value) ||
                    !TryDuration(value, out duration) || duration <= 0 || duration >= 86_400)
                {
                    duration = 0;
                    continue;
                }

                return true;
            }
            catch (JsonException)
            {
                duration = 0;
            }
        }

        return false;
    }

    private static int FindObjectEnd(byte[] data, int start)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;
        var limit = Math.Min(data.Length, start + ParseBytes);
        for (var index = start; index < limit; index++)
        {
            var value = data[index];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (value == (byte)'\\') escaped = true;
                else if (value == (byte)'"') inString = false;
                continue;
            }

            if (value == (byte)'"') inString = true;
            else if (value == (byte)'{') depth++;
            else if (value == (byte)'}' && --depth == 0) return index + 1;
        }

        return -1;
    }

    private static bool TryDuration(JsonElement value, out double duration)
    {
        duration = 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out duration))
            return true;
        if (value.ValueKind == JsonValueKind.String &&
            double.TryParse(value.GetString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out duration))
            return true;
        return false;
    }

    private static bool IsHash(string? hash) =>
        hash is { Length: 32 } && hash.All(character => Uri.IsHexDigit(character));

    private static bool IsReadable(uint protect)
    {
        var value = protect & 0xFF;
        return value is 0x02 or 0x04 or 0x08 or 0x20 or 0x40 or 0x80;
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
