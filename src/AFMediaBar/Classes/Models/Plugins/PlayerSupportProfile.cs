// 播放器兼容性是随应用发布的参考资料，不表示本机检测或安装状态。
namespace AFMediaBar.Classes.Models.Plugins;

/// <summary>单项能力的证据范围；源码实现与本机验证分别记录。</summary>
public enum PlayerCapability { Unknown, Implemented, Reported, RequiresPlugin, Partial }

/// <summary>播放器版本、能力参考及可选插件的关联，不执行 I/O。</summary>
public sealed record PlayerSupportProfile(string Id, string Name, string Version, PlayerCapability Information,
    PlayerCapability Timeline, PlayerCapability Controls, string NoteKey, string SourceUrl, string? PluginId = null)
{
    public string AfNoteKey => Id switch
    {
        "netease" => "QuickStart.Af.NetEase",
        "kugou" => "QuickStart.Af.KuGou",
        _ => "QuickStart.Af.Smtc"
    };
}
