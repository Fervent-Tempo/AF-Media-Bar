// 歌词缓存键里的来源身份：SMTC 给的是可执行文件名，合并后的快照给的是规范名，两者必须落到同一个键。
namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 把来源标识归一成缓存键用的身份。
/// </summary>
/// <remarks>
/// 同一个会话在不同路径上会拿到两种写法：SMTC 的 AppUserModelId 是 <c>cloudmusic.exe</c>，来源策略合并后是
/// <c>cloudmusic</c>。缓存键直接用它时，同一首歌会被当成两首分别取词；这里只去掉可执行文件后缀，不改大小写，
/// 以免影响按来源判断的既有逻辑。
/// </remarks>
internal static class LyricsSourceIdentity
{
    /// <summary>去掉末尾的 .exe，空值折成空字符串。</summary>
    internal static string Normalize(string? sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return string.Empty;
        }

        var id = sourceId.Trim();
        return id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? id[..^4] : id;
    }
}
