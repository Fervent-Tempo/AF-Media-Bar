// Modified from https://github.com/Kxnrl/NetEase-Cloud-Music-DiscordRPC

using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Media.Readers;
using AFMediaBar.Classes.Services.Win32;

namespace AFMediaBar.Classes.Services.Media.Sources.NetEase;

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
    private double _cachedDuration;

    // 元数据文件可能晚于内存曲目更新；失败只短暂退避，不能永久拒绝同一首歌。
    // Metadata files can lag behind memory; retry a missing track after a bounded backoff.
    private string? _rejectedIdentity;
    private string? _rejectedTitle;
    private long _rejectedAt;

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

        ProcessMemory? process = null;
        try
        {
            using var p = Process.GetProcessById(pid);
            var module = p.Modules.OfType<ProcessModule>()
                .FirstOrDefault(candidate => "cloudmusic.dll".Equals(candidate.ModuleName, StringComparison.OrdinalIgnoreCase))
                ?? throw new EntryPointNotFoundException("Failed to find cloudmusic.dll");
            var error = 0;
            process = ProcessMemory.TryOpen(pid, out error)
                ?? throw new Win32Exception(error, "Failed to open NetEase process for reading");
            if (PeBuildFingerprint.TryRead(process, module.BaseAddress, out var loaded) &&
                PeBuildFingerprint.TryReadFile(module.FileName, out var installed) && loaded != installed)
            {
                throw new InvalidOperationException(
                    $"cloudmusic.dll 正在更新: loaded={loaded} installed={installed}");
            }

            var playerMatch = FindUniquePattern(process, module, AudioPlayerPattern, "AudioPlayer");
            var scheduleMatch = FindUniquePattern(process, module, AudioSchedulePattern, "AudioSchedule");
            _audioPlayerPointer = ResolveRipPointer(process, playerMatch, 3);
            _schedulePointer = ResolveRipPointer(process, scheduleMatch, 4);
            _process = process;
            process = null;
        }
        catch
        {
            process?.Dispose();
            throw;
        }

        if (_audioPlayerPointer == nint.Zero)
            throw new EntryPointNotFoundException("Failed to resolve AudioPlayer");
        if (_schedulePointer == nint.Zero)
            throw new EntryPointNotFoundException("Failed to resolve Scheduler");
    }

    private static nint FindUniquePattern(ProcessMemory process, ProcessModule module, string pattern, string name)
    {
        var matches = MemoryPatternScanner.Find(process, module.BaseAddress, module.ModuleMemorySize, pattern,
            maxMatches: 2, maxBytes: 64 * 1024 * 1024);
        if (matches.Count != 1)
            throw new EntryPointNotFoundException($"{name} pattern matches={matches.Count}");
        return matches[0];
    }

    private static nint ResolveRipPointer(ProcessMemory process, nint match, int displacementOffset)
    {
        var textAddress = match + displacementOffset;
        if (!process.TryReadInt32(textAddress, out var displacement))
            throw new EntryPointNotFoundException("Failed to read RIP displacement");
        return textAddress + displacement + sizeof(int);
    }

    public void Dispose() => _process.Dispose();

    public bool Validate(int pid)
        => pid == _pid;

    /// <summary>
    /// 读取当前曲目信息；元数据先查正在播放列表，未命中再回退到私人FM 队列。
    /// Reads the current track; metadata comes from the playing list first and falls back to the private-FM queue.
    /// </summary>
    /// <param name="expectedTitle">
    /// SMTC 报出的曲名，仅用于校验"按 currentIndex 取出的私人FM 曲目"确实是当前曲目；为空时拒绝按索引兜底。
    /// The title SMTC reports, used only to verify that a private-FM track taken by <c>currentIndex</c> really is the current one; an empty value
    /// rejects the index fallback.
    /// </param>
    public PlayerInfo? GetPlayerInfo(string? expectedTitle = null, string? expectedArtist = null,
        double? expectedDuration = null)
    {
        if (GetPlayerInfoOnce(expectedTitle) is not { } first)
            return null;

        Thread.Sleep(30);
        if (GetPlayerInfoOnce(expectedTitle) is not { } second)
            return null;
        if (!string.Equals(first.Identity, second.Identity, StringComparison.Ordinal) ||
            Math.Abs(first.Duration - second.Duration) > 1 ||
            Math.Abs(first.Schedule - second.Schedule) > 0.3 ||
            first.Pause != second.Pause)
            return null;

        if (!string.IsNullOrWhiteSpace(expectedTitle) &&
            !NetEaseTrackMetadataReader.TitlesDescribeTheSameTrack(expectedTitle, second.Title))
            return null;
        if (!string.IsNullOrWhiteSpace(expectedArtist) &&
            !ArtistsOverlap(expectedArtist, second.Artists))
            return null;
        if (expectedDuration is > 0 && Math.Abs(second.Duration - expectedDuration.Value) > 2)
            return null;
        return second;
    }

    private static bool ArtistsOverlap(string expected, string actual)
    {
        var separators = new[] { ',', '、', '/', '&', ';' };
        var wanted = expected.Split(separators, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var reported = actual.Split(separators, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return wanted.Length > 0 && reported.Length > 0 &&
               wanted.Any(name => reported.Any(other => string.Equals(name, other, StringComparison.OrdinalIgnoreCase)));
    }

    private PlayerInfo? GetPlayerInfoOnce(string? expectedTitle)
    {
        var status = GetPlayerStatus();
        if (status is not (PlayStatus.Playing or PlayStatus.Paused))
            return null;

        var identity = GetCurrentSongId();
        if (string.IsNullOrEmpty(identity))
            return null;

        if (!string.Equals(identity, _cachedIdentity, StringComparison.Ordinal) &&
            ((string.Equals(identity, _rejectedIdentity, StringComparison.Ordinal) &&
              string.Equals(expectedTitle, _rejectedTitle, StringComparison.Ordinal) &&
              Stopwatch.GetElapsedTime(_rejectedAt) < TimeSpan.FromSeconds(1)) ||
             !TryLoadTrackMetadata(identity, expectedTitle)))
        {
            // the player rewrites playingList asynchronously, so right after a song
            // switch the new id may not be in the file yet; the caller's debounce
            // absorbs this and we retry on the next loop.
            return null;
        }

        if (GetSchedule() is not { } schedule || GetSongDuration() is not { } duration ||
            !double.IsFinite(schedule) || !double.IsFinite(duration) ||
            schedule < 0 || duration <= 0 || duration > 86_400 || schedule > duration + 2)
            return null;

        return new PlayerInfo
        {
            Identity = identity,
            Title = _cachedTitle,
            Artists = _cachedArtists,
            Album = _cachedAlbum,
            Cover = _cachedCover,
            Duration = duration,
            Schedule = Math.Clamp(schedule, 0, duration),
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
    /// <param name="expectedTitle">SMTC 报出的曲名，用于校验回退结果；为空时拒绝按索引兜底。/ Title SMTC reports, used to verify the fallback result; empty rejects index fallback.</param>
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
        _rejectedTitle = expectedTitle;
        _rejectedAt = Stopwatch.GetTimestamp();
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
    /// <param name="expectedTitle">SMTC 报出的曲名；为空时拒绝按索引兜底。/ Title SMTC reports; empty rejects the index fallback.</param>
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
            $"私人FM 曲目元数据: id={identity} name=\"{metadata.Title}\" artists=\"{metadata.Artists}\"");
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
        _cachedDuration = metadata.DurationSeconds;
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

    private double? GetSchedule()
        => _process.TryReadDouble(_schedulePointer, out var value) && double.IsFinite(value) && value >= 0
            ? value : null;

    private PlayStatus? GetPlayerStatus()
        => _process.TryReadInt32(_audioPlayerPointer + 0x60, out var value) && Enum.IsDefined(typeof(PlayStatus), value)
            ? (PlayStatus)value : null;

    private float GetPlayerVolume()
        => _process.ReadFloat(_audioPlayerPointer, 0x64);

    private float GetCurrentVolume()
        => _process.ReadFloat(_audioPlayerPointer, 0x68);

    private double? GetSongDuration()
    {
        var value = _process.TryReadDouble(_audioPlayerPointer + 0xa8, out var memory) &&
                    double.IsFinite(memory) && memory > 0 && memory < 86_400
            ? memory
            : 0;
        if (value > 0)
            return value;
        return _cachedDuration > 0 ? _cachedDuration : null;
    }

    private string GetCurrentSongId()
    {
        if (!_process.TryReadInt64(_audioPlayerPointer + 0x50, out var audioPlayInfo) || audioPlayInfo == 0)
            return string.Empty;
        if (!_process.TryReadStdString((nint)audioPlayInfo + 0x10, 256, out var str) ||
            string.IsNullOrEmpty(str))
            return string.Empty;

        // 内存里的字符串形如 "<songid>_…"，id 就是下划线之前的那一段。
        var separator = str.IndexOf('_');
        var identity = (separator > 0 ? str[..separator] : str).Trim();
        return identity.Length is > 0 and <= 32 && identity.All(char.IsAsciiDigit) ? identity : string.Empty;
    }

    #endregion
}
