// 单个后台写入器拥有文件和轮转；调用方负责队列、刷新及停止。
using System.IO;
using System.Text;

namespace AFMediaBar.Classes.Services.Diagnostics;

/// <summary>按 UTF-8 字节数限制当前文件和历史文件，不读取或重写已有内容。</summary>
internal sealed class RollingLogFile(string path, long maximumBytes, int maximumFiles) : IDisposable
{
    private FileStream? _stream;
    private long _length;

    internal void Append(string line) => AppendBatch([line]);

    /// <summary>整批记录留在同一文件；新文件的前缀用于保存可独立解析的格式及身份表。</summary>
    internal void AppendBatch(IReadOnlyList<string> lines, Func<IReadOnlyList<string>>? filePrefix = null)
    {
        var records = lines.Select(EncodeLine).ToArray();
        var bytesRequired = records.Sum(item => (long)item.Length);
        if (bytesRequired > maximumBytes) throw new InvalidOperationException("Log batch exceeds the file size limit.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _stream ??= Open();
        if (_length > 0 && _length + bytesRequired > maximumBytes)
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
        if (_length == 0 && filePrefix is not null)
        {
            var prefix = filePrefix().Select(EncodeLine).ToArray();
            if (prefix.Sum(item => (long)item.Length) + bytesRequired > maximumBytes)
                throw new InvalidOperationException("Log context and batch exceed the file size limit.");
            foreach (var record in prefix) Write(record);
        }
        foreach (var record in records) Write(record);
    }

    private byte[] EncodeLine(string line)
    {
        // 一条异常也必须有界；截断发生在字符边界，不产生损坏的 UTF-8。
        var limit = (int)Math.Min(maximumBytes, 256 * 1024);
        if (Encoding.UTF8.GetByteCount(line) + 2 > limit)
        {
            var characters = Math.Max(0, (limit - 48) / 4);
            line = line[..Math.Min(line.Length, characters)] + " [entry truncated]";
        }
        return Encoding.UTF8.GetBytes(line + "\r\n");
    }

    private void Write(byte[] bytes)
    {
        _stream!.Write(bytes);
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
