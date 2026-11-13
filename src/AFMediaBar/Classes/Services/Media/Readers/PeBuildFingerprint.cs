// PE 构建指纹：用时间戳和镜像大小识别一次具体构建，不依赖可能相同的文件版本号。
// 思路参考 Renly1994/xiaotang-daozai（MIT）的 PE 头校验；此处按本项目只读/失败关闭约定重写。
using System.Buffers.Binary;
using System.IO;
using AFMediaBar.Classes.Services.Win32;

namespace AFMediaBar.Classes.Services.Media.Readers;

/// <summary>已加载模块或磁盘文件的 PE 构建标识。 / PE build identity of a loaded module or a file on disk.</summary>
internal readonly record struct PeBuildFingerprint(uint TimeDateStamp, uint SizeOfImage)
{
    private const int PebOffset = 0x3C;
    private const uint PeSignature = 0x0000_4550;
    private const int CoffHeaderSize = 20;
    private const int OptionalHeaderOffset = 4 + CoffHeaderSize;
    private const int TimeDateStampOffset = 8;
    private const int SizeOfImageOffset = 56;

    /// <summary>从外部进程的模块基址读取 PE 头。 / Reads the PE header from a module base in an external process.</summary>
    internal static bool TryRead(ProcessMemory memory, nint moduleBase, out PeBuildFingerprint fingerprint)
    {
        fingerprint = default;
        if (!memory.TryReadInt32(moduleBase + PebOffset, out var peOffset) || peOffset <= 0 || peOffset > 0x10000)
            return false;
        var pe = moduleBase + peOffset;
        if (!memory.TryReadInt32(pe, out var signature) || (uint)signature != PeSignature)
            return false;
        if (!memory.TryReadInt32(pe + TimeDateStampOffset, out var stamp) ||
            !memory.TryReadInt32(pe + OptionalHeaderOffset + SizeOfImageOffset, out var size))
            return false;
        fingerprint = new(unchecked((uint)stamp), unchecked((uint)size));
        return true;
    }

    /// <summary>从磁盘文件读取 PE 头，用于核对已加载模块与安装文件是否同一次构建。</summary>
    internal static bool TryReadFile(string path, out PeBuildFingerprint fingerprint)
    {
        fingerprint = default;
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[64];
            stream.ReadExactly(header[..(PebOffset + 4)]);
            var peOffset = BinaryPrimitives.ReadInt32LittleEndian(header[PebOffset..]);
            if (peOffset <= 0 || peOffset > 0x10000)
                return false;
            stream.Position = peOffset;
            Span<byte> pe = stackalloc byte[OptionalHeaderOffset + SizeOfImageOffset + 4];
            stream.ReadExactly(pe);
            if (BinaryPrimitives.ReadUInt32LittleEndian(pe) != PeSignature)
                return false;
            var stamp = BinaryPrimitives.ReadUInt32LittleEndian(pe[TimeDateStampOffset..]);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(pe[(OptionalHeaderOffset + SizeOfImageOffset)..]);
            fingerprint = new(stamp, size);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    public override string ToString() => $"0x{TimeDateStamp:X8}/{SizeOfImage}";
}
