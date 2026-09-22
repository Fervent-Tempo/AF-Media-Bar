// Modified from https://github.com/Kxnrl/NetEase-Cloud-Music-DiscordRPC

using System.Diagnostics;
using System.IO;
using System.Text;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Win32;

namespace AFMediaBar.Classes.Services.Players;

/// <summary>
/// 从网易云音乐进程内存读取播放状态（进度、曲目 id），元数据由正在播放列表与私人FM 队列解析。
/// Reads playback state (position, song id) from the NetEase CloudMusic process memory;
/// metadata is resolved from the playing list and the private-FM queue.
/// </summary>
public sealed class NetEase : IDisposable
{
    private readonly string _playlistPath;
    private readonly string _fmQueuePath;

    private readonly int _pid;
    private readonly ProcessMemory _process;
    private readonly nint _audioPlayerPointer;
    private readonly nint _schedulePointer;

    // Metadata is immutable per song id: cache it so the playingList file is
    // only read (and json-parsed) when the song actually changes, instead of
    // on every poll loop.
    private string? _cachedIdentity;
    private string _cachedTitle = string.Empty;
    private string _cachedArtists = string.Empty;
    private string _cachedAlbum = string.Empty;
    private string _cachedCover = string.Empty;

    /// <summary>
    /// 已经判定为"不是当前曲目"的 id。私人FM 的 fmPlay 队列在按 id 查不到当前曲目时只作为兜底，一旦那条兜底被
    /// 曲名校验拒绝，就没有必要在 233 毫秒后的下一次轮询里再解析一遍同一份队列文件。
    /// Identity already judged not to be the current track. The private-FM queue in fmPlay only serves as a fallback when the current track
    /// cannot be found by id, and once the title check rejects it there is no point parsing the same queue file again on the next poll (233 ms
    /// later).
    /// </summary>
    private string? _rejectedIdentity;

    private const string AudioPlayerPattern
        = "48 8D 0D ? ? ? ? E8 ? ? ? ? 48 8D 0D ? ? ? ? E8 ? ? ? ? 90 48 8D 0D ? ? ? ? E8 ? ? ? ? 48 8D 05 ? ? ? ? 48 8D A5 ? ? ? ? 5F 5D C3 CC CC CC CC CC 48 89 4C 24 ? 55 57 48 81 EC ? ? ? ? 48 8D 6C 24 ? 48 8D 7C 24";

    private const string AudioSchedulePattern = "66 0F 2E 0D ? ? ? ? 7A ? 75 ? 66 0F 2E 15";

    public NetEase(int pid)
    {
        _pid = pid;

        var fileDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                         "NetEase",
                                         "CloudMusic",
                                         "WebData",
                                         "file");
        _playlistPath = Path.Combine(fileDirectory, "playingList");
        _fmQueuePath = Path.Combine(fileDirectory, "fmPlay");

        using var p = Process.GetProcessById(pid);

        foreach (ProcessModule module in p.Modules)
        {
            if (!"cloudmusic.dll".Equals(module.ModuleName))
            {
                continue;
            }

            var process = new ProcessMemory(pid);
            var address = module.BaseAddress;

            if (Memory.FindPattern(AudioPlayerPattern, pid, address, out var app))
            {
                var textAddress = nint.Add(app, 3);
                var displacement = process.ReadInt32(textAddress);

                _audioPlayerPointer = textAddress + displacement + sizeof(int);
            }

            if (Memory.FindPattern(AudioSchedulePattern, pid, address, out var asp))
            {
                var textAddress = nint.Add(asp, 4);
                var displacement = process.ReadInt32(textAddress);
                _schedulePointer = textAddress + displacement + sizeof(int);
            }

            _process = process;

            break;
        }

        if (_audioPlayerPointer == nint.Zero)
        {
            throw new EntryPointNotFoundException("Failed to find AudioPlayer");
        }

        if (_schedulePointer == nint.Zero)
        {
            throw new EntryPointNotFoundException("Failed to find Scheduler");
        }

        if (_process is null)
        {
            throw new EntryPointNotFoundException("Failed to find process");
        }
    }

    public void Dispose() => _process.Dispose();

    public bool Validate(int pid)
        => pid == _pid;

    /// <summary>
    /// 读取当前曲目信息；元数据先查正在播放列表，未命中再回退到私人FM 队列。
    /// Reads the current track; metadata comes from the playing list first and falls back to the private-FM queue.
    /// </summary>
    /// <param name="expectedTitle">
    /// SMTC 报出的曲名，仅用于校验"按 currentIndex 取出的私人FM 曲目"确实是当前曲目；为空时不做该校验。
    /// The title SMTC reports, used only to verify that a private-FM track taken by <c>currentIndex</c> really is the current one; an empty value
    /// skips the check.
    /// </param>
    public PlayerInfo? GetPlayerInfo(string? expectedTitle = null)
    {
        var status = GetPlayerStatus();

        if (status == PlayStatus.Waiting)
        {
            return null;
        }

        var identity = GetCurrentSongId();

        if (string.IsNullOrEmpty(identity))
        {
            return null;
        }

        if (!string.Equals(identity, _cachedIdentity, StringComparison.Ordinal) &&
            (string.Equals(identity, _rejectedIdentity, StringComparison.Ordinal) ||
             !TryLoadTrackMetadata(identity, expectedTitle)))
        {
            // the player rewrites playingList asynchronously, so right after a song
            // switch the new id may not be in the file yet; the caller's debounce
            // absorbs this and we retry on the next loop.
            return null;
        }

        return new PlayerInfo
        {
            Identity = identity,
            Title = _cachedTitle,
            Artists = _cachedArtists,
            Album = _cachedAlbum,
            Cover = _cachedCover,
            Duration = GetSongDuration(),
            Schedule = GetSchedule(),
            Pause = status == PlayStatus.Paused,

            // lock
            Url = $"https://music.163.com/#/song?id={identity}",
        };
    }

    /// <summary>
    /// 解析某个曲目 id 的元数据：先在正在播放列表里按 id 精确查找，未命中再回退到私人FM 队列。
    /// 两个文件的解析规则都在 <see cref="NetEaseTrackMetadataReader"/> 里（纯策略，可单元测试），这里只负责读文件与填缓存。
    /// Resolves the metadata of one track id: an exact id lookup in the playing list first, then the private-FM queue.
    /// Both files' parsing rules live in <see cref="NetEaseTrackMetadataReader"/> — a pure, unit-testable policy — and this method only reads
    /// the files and fills the cache.
    /// </summary>
    /// <param name="identity">内存里读到的曲目 id。/ Track id read from memory.</param>
    /// <param name="expectedTitle">SMTC 报出的曲名，用于校验回退结果；为空时不做校验。/ Title SMTC reports, used to verify the fallback result; empty skips it.</param>
    /// <returns>是否取到元数据。/ Whether metadata was resolved.</returns>
    private bool TryLoadTrackMetadata(string identity, string? expectedTitle)
    {
        if (TryLoadFromPlayingList(identity))
        {
            return true;
        }

        if (TryLoadFromFmQueue(identity, expectedTitle))
        {
            return true;
        }

        _rejectedIdentity = identity;
        return false;
    }

    /// <summary>
    /// 从正在播放列表（playingList）里按 id 取元数据。
    /// Reads metadata for one id out of the playing list (playingList).
    /// </summary>
    /// <param name="identity">曲目 id。/ Track id.</param>
    /// <returns>是否命中。/ Whether the id was found.</returns>
    private bool TryLoadFromPlayingList(string identity)
    {
        string? json;
        try
        {
            json = File.Exists(_playlistPath) ? File.ReadAllText(_playlistPath) : null;
        }
        catch (IOException)
        {
            // 客户端正在写这个文件（共享冲突）：临时的，下一次轮询再试。
            // The client is writing the file right now (a sharing violation): transient, retried on the next poll.
            return false;
        }

        if (!NetEaseTrackMetadataReader.TryReadFromPlayingList(json, identity, out var metadata))
        {
            return false;
        }

        ApplyMetadata(identity, metadata);
        return true;
    }

    /// <summary>
    /// 从私人FM 队列（fmPlay）里取元数据 —— 播放私人FM 时客户端不写正在播放列表，只有这个队列知道当前曲目。
    /// Reads metadata out of the private-FM queue (fmPlay): the client does not write the playing list while private FM plays, so this queue is
    /// the only place that knows the current track.
    /// </summary>
    /// <param name="identity">内存里读到的曲目 id。/ Track id read from memory.</param>
    /// <param name="expectedTitle">SMTC 报出的曲名；为空时不做校验。/ Title SMTC reports; empty skips the check.</param>
    /// <returns>是否命中。/ Whether metadata was resolved.</returns>
    private bool TryLoadFromFmQueue(string identity, string? expectedTitle)
    {
        string? json;
        try
        {
            json = File.Exists(_fmQueuePath) ? File.ReadAllText(_fmQueuePath) : null;
        }
        catch (IOException)
        {
            return false;
        }

        if (!NetEaseTrackMetadataReader.TryReadFromFmQueue(json, identity, expectedTitle, out var metadata))
        {
            return false;
        }

        ApplyMetadata(identity, metadata);

        // 命中来源写进日志：排查"私人FM 有没有被认出来"时，这一行就是唯一证据（内存 id 与 fmPlay 各自长什么样）。
        // The hit path goes to the log: when "was private FM recognized" is the question, this line is the only evidence
        // (it shows what the memory id and fmPlay each look like).
        AppLogService.Current?.Info(
            "NetEase",
            $"私人FM 曲目元数据 / private-FM track metadata: id={identity} name=\"{metadata.Title}\" artists=\"{metadata.Artists}\"");
        return true;
    }

    /// <summary>
    /// 写入元数据缓存；只有在这一步，一次成功解析才真正生效。
    /// Fills the metadata cache; a successful lookup only takes effect here.
    /// </summary>
    private void ApplyMetadata(string identity, in NetEaseTrackMetadata metadata)
    {
        _cachedIdentity = identity;
        _rejectedIdentity = null;
        _cachedTitle = metadata.Title;
        _cachedArtists = metadata.Artists;
        _cachedAlbum = metadata.Album;
        _cachedCover = metadata.Cover;
    }

    #region Unsafe

    private enum PlayStatus
    {
        Waiting,
        Playing,
        Paused,
        Unknown3,
        Unknown4,
    }

    private double GetSchedule()
        => _process.ReadDouble(_schedulePointer);

    private PlayStatus GetPlayerStatus()
        => (PlayStatus)_process.ReadInt32(_audioPlayerPointer, 0x60);

    private float GetPlayerVolume()
        => _process.ReadFloat(_audioPlayerPointer, 0x64);

    private float GetCurrentVolume()
        => _process.ReadFloat(_audioPlayerPointer, 0x68);

    private double GetSongDuration()
        => _process.ReadDouble(_audioPlayerPointer, 0xa8);

    private string GetCurrentSongId()
    {
        var audioPlayInfo = _process.ReadInt64(_audioPlayerPointer, 0x50);

        if (audioPlayInfo == 0)
        {
            return string.Empty;
        }

        var strPtr = audioPlayInfo + 0x10;

        var strLength = _process.ReadInt64((nint)strPtr, 0x10);

        // small string optimization
        byte[] strBuffer;

        if (strLength <= 15)
        {
            strBuffer = _process.ReadBytes((nint)strPtr, (int)strLength);
        }
        else
        {
            var strAddress = _process.ReadInt64((nint)strPtr);
            strBuffer = _process.ReadBytes((nint)strAddress, (int)strLength);
        }

        var str = Encoding.UTF8.GetString(strBuffer);

        if (string.IsNullOrEmpty(str))
        {
            return string.Empty;
        }

        // 内存里的字符串形如 "<songid>_…"，id 就是下划线之前的那一段。没有下划线时整段就是 id：
        // 越界切片会抛 ArgumentOutOfRangeException，而它会被轮询循环当成"内存读取失败"吞掉并重建读取器——
        // 表现是这一首歌永远没有来源补充，且没有任何一条日志指向真正的原因。
        // The string in memory looks like "<songid>_…", so the id is what precedes the underscore. Without an underscore the whole
        // string is the id: slicing out of range throws ArgumentOutOfRangeException, which the poll loop swallows as "the memory read
        // failed" and rebuilds the reader for — the symptom being a track that never gets source enrichment, with no log line
        // pointing at the real cause.
        var separator = str.IndexOf('_');
        return (separator > 0 ? str[..separator] : str).TrimEnd('\0').Trim();
    }

    #endregion
}
