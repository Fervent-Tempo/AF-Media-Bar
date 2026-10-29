// 插件目录与下载快照；只描述数据，不加载插件或执行安装步骤。
namespace AFMediaBar.Classes.Models.Plugins;

/// <summary>固定版本的上游下载附件；架构指播放器架构。</summary>
public sealed record PluginAsset(string Architecture, string FileName, string Url, long Size, string Sha256);

/// <summary>插件或原生配置指引，说明文字通过资源键本地化。</summary>
public sealed record PluginDescriptor(string Id, string Name, string TargetApplication, string Author, string Version,
    string ProjectUrl, bool IsExperimental, IReadOnlyList<PluginAsset> Assets)
{
    public string ResourcePrefix => $"Plugins.Catalog.{Id}";
}

/// <summary>下载阶段不表示插件已安装。</summary>
public enum PluginDownloadPhase { Available, Downloading, Verifying, Completed, Canceled, Failed }

/// <summary>应用级下载状态；失败原因使用资源键，便于切换语言。</summary>
public sealed record PluginDownloadState(PluginDownloadPhase Phase = PluginDownloadPhase.Available,
    double Progress = 0, string? SourceHost = null, string? FilePath = null, string? FailureKey = null);
