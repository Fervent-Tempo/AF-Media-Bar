// 随应用发布的固定插件目录；没有远程目录、插件执行或自动安装能力。
using AFMediaBar.Classes.Models.Plugins;

namespace AFMediaBar.Classes.Services.Plugins;

/// <summary>审核后的插件附件和说明入口；增加 AF 扩展时可沿用目标应用标识。</summary>
public static class PluginCatalog
{
    public static IReadOnlyList<PluginDescriptor> Entries { get; } = Array.AsReadOnly(new[]
    {
        Entry("foobar", "foo_mediacontrol", "foobar2000", "ungive", "v1.4", "ungive/foo_mediacontrol", false,
            Asset("x86", "foo_mediacontrol-x86.fb2k-component", 51495, "b3e5cef9987e344a3f829a70d613b0a6e139ff3c28b53dba02aa66e0fc24ad3e"),
            Asset("x64", "foo_mediacontrol-x64.fb2k-component", 55208, "3220cef9a6428435b7adae38f402a9b2c813a851bb485f76c8d8dc5827aa3c57")),
        Entry("musicbee", "mb_MediaControl", "MusicBee", "HenryPDT", "1.0", "HenryPDT/mb_MediaControl", false,
            Asset("AnyCPU", "mb_MediaControl.dll", 85504, "bd9b6eb5649c2432c65438e4cd5c78ef1b620acdd3fca5ba69e2ff8770b8d5a9")),
        Entry("vlc", "Windows SMTC integration", "VLC", "spmn", "v5.0.0", "spmn/vlc-win10smtc", false,
            Asset("x86", "libwin10smtc_plugin_v5.0.0_x86.zip", 1406755, "bb183b2c84b90a8e1ca867c7780340562b061dd8c748493b4d9687caab5d4ab4"),
            Asset("x64", "libwin10smtc_plugin_v5.0.0_x64.zip", 1387478, "2de0bc5d560635bb7be4dc7825461dcd8d1afe9fb9d865ea141ff145df163fa6")),
        Entry("itunes", "Media Controller for iTunes", "iTunes", "thewizrd", "v1.3_release", "thewizrd/iTunes-SMTC", false,
            Asset("x86", "Media.Controller.for.iTunes.v1.3.3-x86.zip", 12682477, "dbf5c25500d87825df59bb170d1faa932af8023236e873e4e7e18d868f694c40"),
            Asset("x64", "Media.Controller.for.iTunes.v1.3.3-x64.zip", 13121398, "375567160eb6ccc761b2d3aaf85e27bf4b582d485c44975e1b72be9dd94c14de")),
        Entry("qqmusic", "qqmusic-smtc-seek", "QQ Music", "zhuzh3378", "v0.1.0", "zhuzh3378/qqmusic-smtc-seek", true,
            Asset("x86", "payload.dll", 716800, "0565fe520cca33ae08959e34d841efd687e310074b2f5683142e5672fd0b4485"))
    });

    private static PluginAsset Asset(string architecture, string fileName, long size, string sha256) =>
        new(architecture, fileName, string.Empty, size, sha256);

    private static PluginDescriptor Entry(string id, string name, string target, string author, string version,
        string repository, bool experimental, params PluginAsset[] assets) =>
        new(id, name, target, author, version, $"https://github.com/{repository}", experimental,
            Array.AsReadOnly(assets.Select(asset => asset with
            {
                Url = $"https://github.com/{repository}/releases/download/{version}/{asset.FileName}"
            }).ToArray()));
}
