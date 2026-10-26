// 单个后台写入器拥有文件和轮转；调用方负责队列、刷新及停止。
using System.IO;
using System.Text;

namespace AFMediaBar.Classes.Services.Diagnostics;

/// <summary>按 UTF-8 字节数限制当前文件和历史文件，不读取或重写已有内容。</summary>
internal sealed class RollingLogFile(string path, long maximumBytes, int maximumFiles) : IDisposable
{
    private FileStream? _stream;
    private long _length;

    internal void Append(string line)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _stream ??= Open();
        // 一条异常也必须有界；截断发生在字符边界，不产生损坏的 UTF-8。
        var limit = (int)Math.Min(maximumBytes, 256 * 1024);
        if (Encoding.UTF8.GetByteCount(line) + 2 > limit)
        {
            var characters = Math.Max(0, (limit - 48) / 4);
            line = line[..Math.Min(line.Length, characters)] + " [entry truncated]";
        }
        var bytes = Encoding.UTF8.GetBytes(line + "\r\n");
        if (_length > 0 && _length + bytes.Length > maximumBytes)
        {
            _stream.Dispose();
            _stream = null;
            for (var index = maximumFiles - 1; index >= 1; index--)
            {
                var source = index == 1 ? path : Archive(index - 1);
                var destination = Archive(index);
                if (File.Exists(source)) File.Move(source, destination, overwrite: true);
            }
            _stream = Open();
        }
        _stream.Write(bytes);
        _length += bytes.Length;
    }

    // 刷新后释放写句柄，普通只读共享的查看器和诊断导出才能读取文件。
    internal void Flush() => Dispose();

    private string Archive(int index) => Path.Combine(Path.GetDirectoryName(path)!,
        Path.GetFileNameWithoutExtension(path) + "." + index + Path.GetExtension(path));

    private FileStream Open()
    {
        var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read, 16 * 1024);
        try
        {
            var length = stream.Length;
            if (length > 0)
            {
                stream.Position = length - 1;
                if (stream.ReadByte() != '\n') stream.Write("\r\n"u8);
            }
            stream.Position = stream.Length;
            _length = stream.Length;
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }

    public void Dispose()
    {
        var stream = _stream;
        _stream = null;
        stream?.Dispose();
    }
}
