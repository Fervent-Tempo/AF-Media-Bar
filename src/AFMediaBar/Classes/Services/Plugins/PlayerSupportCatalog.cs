// 快速开始与插件目录共享经过查证的播放器资料；不根据已安装软件推断能力。
using AFMediaBar.Classes.Models.Plugins;
using static AFMediaBar.Classes.Models.Plugins.PlayerCapability;

namespace AFMediaBar.Classes.Services.Plugins;

/// <summary>2026-10-24 核对的版本化参考；证据与实机验收见播放器兼容性文档。</summary>
public static class PlayerSupportCatalog
{
    public static IReadOnlyList<PlayerSupportProfile> Entries { get; } = Array.AsReadOnly(new[]
    {
        new PlayerSupportProfile("netease", "NetEase Cloud Music", "3.1.41", Unknown, Unknown, Unknown,
            "NetEase", "https://github.com/apoint123/inflink-rs"),
        new PlayerSupportProfile("qqmusic", "QQ Music", "22.71", Reported, Reported, Partial,
            "QQ", "https://github.com/zhuzh3378/qqmusic-smtc-seek", "qqmusic"),
        new PlayerSupportProfile("kugou", "KuGou Music", "20.1.51", Unknown, Unknown, Unknown,
            "KuGou", "https://www.kugou.com/"),
        new PlayerSupportProfile("soda", "Soda Music", "", Unknown, Unknown, Unknown,
            "Pending", "https://www.qishui.com/"),
        new PlayerSupportProfile("kuwo", "Kuwo Music", "", Unknown, Unknown, Unknown,
            "Pending", "https://www.kuwo.cn/"),
        new PlayerSupportProfile("migu", "Migu Music", "", Unknown, Unknown, Unknown,
            "Browser", "https://music.migu.cn/"),
        new PlayerSupportProfile("qianqian", "Qianqian Music", "", Unknown, Unknown, Unknown,
            "Pending", "https://91q.com/download"),
        new PlayerSupportProfile("bodian", "Bodian Music", "", Unknown, Unknown, Unknown,
            "Bodian", "https://github.com/HChenX/BodianSMTCPlugin"),
        new PlayerSupportProfile("lx", "LX Music", "v2.12.6", Implemented, Implemented, Implemented,
            "BuiltIn", "https://github.com/lyswhut/lx-music-desktop/blob/v2.12.6/src/renderer/core/useApp/usePlayer/useMediaSessionInfo.ts"),
        new PlayerSupportProfile("yesplay", "YesPlayMusic", "v0.4.11", Implemented, Implemented, Implemented,
            "BuiltIn", "https://github.com/qier222/YesPlayMusic/blob/v0.4.11/src/utils/Player.js"),
        new PlayerSupportProfile("moekoe", "MoeKoeMusic", "v1.7.1", Implemented, Implemented, Implemented,
            "BuiltIn", "https://github.com/MoeKoeMusic/MoeKoeMusic/blob/v1.7.1/src/components/player/MediaSession.js"),
        new PlayerSupportProfile("listen1", "Listen 1", "v2.33.0", Implemented, Implemented, Implemented,
            "Listen1", "https://github.com/listen1/listen1_chrome_extension/blob/3f24efa045125875a609dcb0f3f3f0be4edb8b37/js/player_thread.js"),
        new PlayerSupportProfile("splayer", "SPlayer", "v3.1.1", Implemented, Implemented, Implemented,
            "SPlayer", "https://github.com/SPlayer-Dev/SPlayer/blob/v3.1.1/src/core/player/MediaSessionManager.ts"),
        new PlayerSupportProfile("alger", "AlgerMusicPlayer", "v5.1.0", Implemented, Implemented, Implemented,
            "BuiltIn", "https://github.com/algerkong/AlgerMusicPlayer/blob/v5.1.0/src/renderer/services/audioService.ts"),
        new PlayerSupportProfile("musicfree", "MusicFree Desktop", "v0.0.8", Implemented, Unknown, Partial,
            "MusicFree", "https://github.com/maotoumao/MusicFreeDesktop/blob/v0.0.8/src/renderer/core/track-player/controller/audio-controller.ts"),
        new PlayerSupportProfile("foobar", "foobar2000", "", Reported, RequiresPlugin, Reported,
            "Foobar", "https://github.com/ungive/foo_mediacontrol", "foobar"),
        new PlayerSupportProfile("musicbee", "MusicBee", "", RequiresPlugin, RequiresPlugin, RequiresPlugin,
            "Plugin", "https://github.com/HenryPDT/mb_MediaControl", "musicbee"),
        new PlayerSupportProfile("vlc", "VLC", "3.0.x", RequiresPlugin, RequiresPlugin, RequiresPlugin,
            "Vlc", "https://github.com/spmn/vlc-win10smtc", "vlc"),
        new PlayerSupportProfile("itunes", "iTunes", "", RequiresPlugin, RequiresPlugin, RequiresPlugin,
            "ITunes", "https://github.com/thewizrd/iTunes-SMTC", "itunes"),
        new PlayerSupportProfile("mpv", "mpv", "v0.41.0", Implemented, Implemented, Implemented,
            "Mpv", "https://github.com/mpv-player/mpv/blob/v0.41.0/osdep/win32/smtc.cpp"),
        new PlayerSupportProfile("potplayer", "PotPlayer", "26.08.19", Partial, Reported, Reported,
            "PotPlayer", "https://github.com/Barrryy/potplayer-smtc-bridge"),
        new PlayerSupportProfile("spotify", "Spotify", "1.3.0.277", Unknown, Unknown, Unknown,
            "Pending", "https://www.spotify.com/"),
        new PlayerSupportProfile("applemusic", "Apple Music", "", Unknown, Unknown, Unknown,
            "Pending", "https://support.apple.com/guide/music-windows/welcome/windows"),
        new PlayerSupportProfile("aimp", "AIMP", "", Unknown, Unknown, Unknown,
            "Aimp", "https://github.com/NimiGames68/aimp-smtc"),
        new PlayerSupportProfile("mpchc", "MPC-HC", "", Unknown, Unknown, Unknown,
            "MpcHc", "https://github.com/clsid2/mpc-hc"),
        new PlayerSupportProfile("tidal", "TIDAL", "", Unknown, Unknown, Unknown,
            "Pending", "https://github.com/FireWall-code/TidaLuna-Plugins")
    });
}
