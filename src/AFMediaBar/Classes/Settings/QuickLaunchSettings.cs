// 按功能归组的设置契约；保持既有类型全名、枚举值及默认语义。
using AFMediaBar.Classes.Models;
using System.IO;

namespace AFMediaBar.Classes.Settings;

/// <summary>无媒体入口的快速启动交互。</summary>
public enum QuickLaunchInteractionMode { ListLaunch = 0, SelectThenLaunch = 1 }

/// <summary>快速启动列表设置。 / Quick-launch list settings.</summary>
public readonly record struct QuickLaunchSettings(IReadOnlyList<QuickLaunchEntry>? Entries)
{
    // JSON 缺少新属性时也执行属性初始化，避免结构体零值把默认开启读成关闭。
    public QuickLaunchSettings() : this([]) { }

    public QuickLaunchInteractionMode InteractionMode { get; init; }
    public bool ShowApplicationIconOnHover { get; init; } = true;
    public string? SelectedEntryId { get; init; }
    public static QuickLaunchSettings Default { get; } = new([]);

    public QuickLaunchSettings Normalize()
    {
        var entries = new List<QuickLaunchEntry>();
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Entries ?? [])
        {
            if (string.IsNullOrWhiteSpace(entry.Target) || !Enum.IsDefined(entry.Kind))
                continue;
            var target = entry.Target.Trim();
            if (!targets.Add($"{entry.Kind}:{target}"))
                continue;
            var id = string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString("N") : entry.Id.Trim();
            var displayName = string.IsNullOrWhiteSpace(entry.DisplayName)
                ? Path.GetFileNameWithoutExtension(target)
                : entry.DisplayName.Trim();
            entries.Add(entry with
            {
                Id = id,
                DisplayName = displayName,
                Target = target,
                SourceId = string.IsNullOrWhiteSpace(entry.SourceId) ? null : entry.SourceId.Trim()
            });
        }
        var selectedId = SelectedEntryId;
        return this with
        {
            Entries = entries,
            InteractionMode = Enum.IsDefined(InteractionMode) ? InteractionMode : QuickLaunchInteractionMode.ListLaunch,
            SelectedEntryId = entries.Any(entry => entry.Id == selectedId) ? selectedId : null
        };
    }
}
