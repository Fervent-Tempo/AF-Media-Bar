// 首页兼容提醒的本地检测快照；安装或运行记录不代表已启用任务栏修改。
namespace AFMediaBar.Classes.Models.Home;

/// <summary>已知工具的安装或当前会话运行记录。</summary>
public sealed record CustomizationToolMatch(string Name, bool IsInstalled, bool IsRunning);

/// <summary>部分来源不可读时保留命中，同时明确检测不完整。</summary>
public sealed record CustomizationDetectionResult(IReadOnlyList<CustomizationToolMatch> Tools, bool IsComplete);
