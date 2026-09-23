using System.Text.Json;
using System.Text.Json.Serialization;

namespace AFMediaBar.Classes.Services.Players;

/// <summary>
/// 从网易云客户端的元数据文件里解析出的一条曲目信息。
/// One track read out of the NetEase client's metadata files.
/// </summary>
/// <param name="Title">曲名。/ Title.</param>
/// <param name="Artists">歌手，已用逗号连接。/ Artists, already joined with commas.</param>
/// <param name="Album">专辑名。/ Album name.</param>
/// <param name="Cover">封面地址。/ Cover URL.</param>
public readonly record struct NetEaseTrackMetadata(string Title, string Artists, string Album, string Cover);

/// <summary>
/// 解析网易云客户端两个元数据文件的纯策略：正在播放列表（playingList）与私人FM 队列（fmPlay）。
///
/// 为什么私人FM 必须是单独一条路径：播放私人FM 时客户端**不写**正在播放列表，当前曲目只出现在 fmPlay 的
/// <c>currentIndex</c> 上。只查 playingList 时，私人FM 永远取不到元数据，界面于是只剩 SMTC 给的标题、歌手与封面，
/// 进度与时长恒为 0，歌词也完全没有兜底。
/// Pure policy that reads the NetEase client's two metadata files: the playing list (playingList) and the private-FM queue (fmPlay).
///
/// Private FM needs a path of its own because the client does **not** write the playing list while private FM plays: the current track
/// appears only at fmPlay's <c>currentIndex</c>. Looking at playingList alone therefore never resolves private-FM metadata, and the
/// interface keeps just the title, artist, and artwork SMTC reports while position and duration stay at zero with no lyric fallback at all.
/// </summary>
public static class NetEaseTrackMetadataReader
{
    /// <summary>
    /// 从正在播放列表里按曲目 id 取元数据。
    /// Reads one track out of the playing list by its id.
    /// </summary>
    /// <param name="json">playingList 的内容；文件不存在时为 <see langword="null"/>。/ Contents of playingList, or null when the file is missing.</param>
    /// <param name="identity">曲目 id。/ Track id.</param>
    /// <param name="metadata">解析结果。/ The parsed metadata.</param>
    /// <returns>是否命中。/ Whether the id was found.</returns>
    public static bool TryReadFromPlayingList(string? json, string identity, out NetEaseTrackMetadata metadata)
    {
        metadata = default;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        NetEasePlaylist? playlist;
        try
        {
            playlist = JsonSerializer.Deserialize<NetEasePlaylist>(json);
        }
        catch (JsonException)
        {
            // 客户端正在写这个文件：内容被截断，下一次轮询再试。
            // The client is writing the file right now, so the content is truncated; the next poll retries.
            return false;
        }

        if (playlist?.List?.Find(item => item.Identity == identity) is not { Track: { } track })
        {
            return false;
        }

        metadata = new NetEaseTrackMetadata(
            track.Name,
            string.Join(',', track.Artists.Select(artist => artist.Singer)),
            track.Album.Name,
            track.Album.Cover);
        return true;
    }

    /// <summary>
    /// 从私人FM 队列里取当前曲目的元数据。
    ///
    /// 先用曲目 id 在整个队列里查（与 <c>currentIndex</c> 是否已经刷新无关，因此最可靠）；队列里没有这个 id 时才按
    /// <c>currentIndex</c> 取那一条，并且要求它的曲名与 SMTC 报出的曲名对得上。
    /// Reads the current track's metadata out of the private-FM queue.
    ///
    /// The whole queue is searched by id first, which is reliable regardless of whether <c>currentIndex</c> has been refreshed yet; only when
    /// the id is absent is the entry at <c>currentIndex</c> used, and its title has to match the one SMTC reports.
    /// </summary>
    /// <param name="json">fmPlay 的内容；文件不存在时为 <see langword="null"/>。/ Contents of fmPlay, or null when the file is missing.</param>
    /// <param name="identity">内存里读到的曲目 id。/ Track id read from memory.</param>
    /// <param name="expectedTitle">SMTC 报出的曲名，用于校验按 <c>currentIndex</c> 取出的那一条；为空时不校验。/ Title SMTC reports, used to verify the entry taken by currentIndex; an empty value skips the check.</param>
    /// <param name="metadata">解析结果。/ The parsed metadata.</param>
    /// <returns>是否命中。/ Whether the current track was resolved.</returns>
    public static bool TryReadFromFmQueue(
        string? json,
        string identity,
        string? expectedTitle,
        out NetEaseTrackMetadata metadata)
    {
        metadata = default;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        NetEaseFmQueue? queue;
        try
        {
            queue = JsonSerializer.Deserialize<NetEaseFmQueue>(json);
        }
        catch (JsonException)
        {
            return false;
        }

        if (queue?.Queue is not { Count: > 0 } tracks)
        {
            return false;
        }

        var track = tracks.Find(candidate => string.Equals(candidate.Identity, identity, StringComparison.Ordinal));
        if (track is null)
        {
            // fmPlay 可能是上一次私人FM 会话留下的旧队列（当前曲目是播客、本地文件或云盘曲目，本来就不在里面），
            // 因此按 currentIndex 取出的那一条 MUST 与 SMTC 的曲名对得上；否则宁可少一次补充，也不要把另一首歌的
            // 标题、封面与歌词挂到当前曲目上。
            // fmPlay may be the queue a previous private-FM session left behind (the current track being a podcast, a local file, or a cloud
            // track was never in it), so the entry taken by currentIndex MUST match the title SMTC reports. Otherwise one enrichment fewer is
            // better than attaching another song's title, cover, and lyrics to the current track.
            if (queue.CurrentIndex < 0 || queue.CurrentIndex >= tracks.Count)
            {
                return false;
            }

            track = tracks[queue.CurrentIndex];
            if (!TitlesDescribeTheSameTrack(expectedTitle, track.Name))
            {
                return false;
            }
        }

        metadata = new NetEaseTrackMetadata(
            track.Name,
            string.Join(',', (track.Artists ?? []).Select(artist => artist.Name).Where(name => !string.IsNullOrWhiteSpace(name))),
            track.Album?.Name ?? string.Empty,
            track.Album?.ResolvedCover ?? string.Empty);
        return true;
    }

    /// <summary>
    /// 判断 SMTC 报出的曲名与队列里的曲名是不是同一首歌。允许一端是另一端的子串：播放器会在曲名后补
    /// 「(Live)」「(feat. …)」这类修饰，而这条判定只用来排除"整首歌都不一样"的旧队列。
    /// Reports whether the title SMTC gives and the one in the queue describe the same track. One being a substring of the other is accepted:
    /// players append decorations such as "(Live)" or "(feat. …)", and this test only exists to rule out a stale queue holding an entirely
    /// different song.
    /// </summary>
    /// <param name="expectedTitle">SMTC 报出的曲名。/ Title SMTC reports.</param>
    /// <param name="queueTitle">队列里的曲名。/ Title inside the queue.</param>
    public static bool TitlesDescribeTheSameTrack(string? expectedTitle, string? queueTitle)
    {
        if (string.IsNullOrWhiteSpace(expectedTitle) || string.IsNullOrWhiteSpace(queueTitle))
        {
            return false;
        }

        var expected = expectedTitle.Trim();
        var actual = queueTitle.Trim();
        return expected.Contains(actual, StringComparison.OrdinalIgnoreCase) ||
               actual.Contains(expected, StringComparison.OrdinalIgnoreCase);
    }
}

file record NetEasePlaylistTrackArtist([property: JsonPropertyName("name")] string Singer);

file record NetEasePlaylistTrackAlbum(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("cover")] string Cover);

file record NetEasePlaylistTrack(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("artists")]
    NetEasePlaylistTrackArtist[] Artists,
    [property: JsonPropertyName("album")] NetEasePlaylistTrackAlbum Album);

file record NetEasePlaylistItem(
    [property: JsonPropertyName("id")] string Identity,
    [property: JsonPropertyName("track")] NetEasePlaylistTrack Track);

file record NetEasePlaylist([property: JsonPropertyName("list")] List<NetEasePlaylistItem> List);

/// <summary>
/// 私人FM 队列（fmPlay）里的一位歌手：与 playingList 里的 track 同构，只是歌手字段叫 <c>name</c>。
/// One artist inside the private-FM queue (fmPlay). It mirrors the artist inside playingList except that the field is called <c>name</c>.
/// </summary>
file record NetEaseFmTrackArtist([property: JsonPropertyName("name")] string Name);

/// <summary>
/// 私人FM 队列里的专辑：封面可能写在 <c>cover</c> 上，旧版客户端只写 <c>picUrl</c>。
/// The album inside the private-FM queue: the cover may live in <c>cover</c> while older clients only write <c>picUrl</c>.
/// </summary>
file record NetEaseFmTrackAlbum(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("cover")] string? Cover,
    [property: JsonPropertyName("picUrl")] string? PicUrl)
{
    /// <summary>封面地址：优先 <c>cover</c>，缺失时用 <c>picUrl</c>。/ Cover URL: <c>cover</c> first, falling back to <c>picUrl</c>.</summary>
    public string ResolvedCover => !string.IsNullOrWhiteSpace(Cover) ? Cover : PicUrl ?? string.Empty;
}

file record NetEaseFmTrack(
    [property: JsonPropertyName("id")] string Identity,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("artists")]
    NetEaseFmTrackArtist[]? Artists,
    [property: JsonPropertyName("album")] NetEaseFmTrackAlbum? Album);

/// <summary>私人FM 队列：<c>currentIndex</c> 指向当前曲目。/ The private-FM queue, whose <c>currentIndex</c> points at the current track.</summary>
file record NetEaseFmQueue(
    [property: JsonPropertyName("currentIndex")] int CurrentIndex,
    [property: JsonPropertyName("queue")] List<NetEaseFmTrack>? Queue);
