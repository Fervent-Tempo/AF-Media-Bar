// Modified from https://github.com/Kxnrl/NetEase-Cloud-Music-DiscordRPC

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AFMediaBar.Abstractions;
using AFMediaBar.Models;
using AFMediaBar.Services.Win32Api;

namespace AFMediaBar.Services.Players;

public sealed class NetEase : IMusicPlayer
{
    private readonly string _path;

    private readonly int _pid;
    private readonly ProcessMemory _process;
    private readonly nint _audioPlayerPointer;
    private readonly nint _schedulePointer;

    // Metadata is immutable per song id: cache it so the playingList file is
    // only read (and json-parsed) when the song actually changes, instead of
    // on every 233ms loop.
    private string? _cachedIdentity;
    private string _cachedTitle = string.Empty;
    private string _cachedArtists = string.Empty;
    private string _cachedAlbum = string.Empty;
    private string _cachedCover = string.Empty;

    private const string AudioPlayerPattern
        = "48 8D 0D ? ? ? ? E8 ? ? ? ? 48 8D 0D ? ? ? ? E8 ? ? ? ? 90 48 8D 0D ? ? ? ? E8 ? ? ? ? 48 8D 05 ? ? ? ? 48 8D A5 ? ? ? ? 5F 5D C3 CC CC CC CC CC 48 89 4C 24 ? 55 57 48 81 EC ? ? ? ? 48 8D 6C 24 ? 48 8D 7C 24";

    private const string AudioSchedulePattern = "66 0F 2E 0D ? ? ? ? 7A ? 75 ? 66 0F 2E 15";

    public NetEase(int pid)
    {
        _pid = pid;

        _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                             "NetEase",
                             "CloudMusic",
                             "WebData",
                             "file",
                             "playingList");

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

    public bool Validate(int pid)
        => pid == _pid;

    public PlayerInfo? GetPlayerInfo()
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

        if (!string.Equals(identity, _cachedIdentity, StringComparison.Ordinal) && !TryLoadTrackMetadata(identity))
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

    private bool TryLoadTrackMetadata(string identity)
    {
        NetEasePlaylist? playlist;

        try
        {
            var jsonData = File.Exists(_path) ? File.ReadAllText(_path) : null;

            playlist = jsonData is { } json ? JsonSerializer.Deserialize<NetEasePlaylist>(json) : null;
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            // the player is writing the file right now (sharing violation or a
            // truncated json): transient, retry on the next loop.
            return false;
        }

        if (playlist?.List?.Find(x => x.Identity == identity) is not { Track: { } track })
        {
            return false;
        }

        _cachedIdentity = identity;
        _cachedTitle = track.Name;
        _cachedArtists = string.Join(',', track.Artists.Select(x => x.Singer));
        _cachedAlbum = track.Album.Name;
        _cachedCover = track.Album.Cover;

        return true;
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

        return string.IsNullOrEmpty(str) ? string.Empty : str[..str.IndexOf('_')];
    }

    #endregion
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
