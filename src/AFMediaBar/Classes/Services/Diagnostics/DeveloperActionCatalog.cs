// 固定动作目录和纯解析；帮助、命令建议与按钮共用这份定义。
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Diagnostics;

/// <summary>第一版开发者工具的动作白名单。</summary>
public static class DeveloperActionCatalog
{
    public static IReadOnlyList<DeveloperAction> Actions { get; } = Array.AsReadOnly<DeveloperAction>([
        new("notify update", "Update", DeveloperActionImpact.Preview),
        new("notify taskbar moved", "Moved", DeveloperActionImpact.Preview),
        new("notify taskbar hidden", "Hidden", DeveloperActionImpact.Preview),
        new("notify background", "Background", DeveloperActionImpact.Preview),
        new("notify lyrics", "Lyrics", DeveloperActionImpact.Preview),
        new("notify restart busy", "RestartBusy", DeveloperActionImpact.Preview),
        new("notify restart failed", "RestartFailed", DeveloperActionImpact.Preview),
        new("notify track", "Track", DeveloperActionImpact.Preview),
        new("restart confirm", "Confirm", DeveloperActionImpact.Preview),
        new("app restart", "Restart", DeveloperActionImpact.Session),
        new("webview rebuild", "Rebuild", DeveloperActionImpact.Session, true),
        new("webview fail renderer", "Renderer", DeveloperActionImpact.Session, true),
        new("webview fail unresponsive", "Unresponsive", DeveloperActionImpact.Session, true),
        new("webview fail gpu", "Gpu", DeveloperActionImpact.Session, true),
        new("webview disable", "Disable", DeveloperActionImpact.Session, true),
        new("taskbar reload", "Reload", DeveloperActionImpact.Session),
        new("state", "State", DeveloperActionImpact.Diagnostic),
        new("memory trim", "Trim", DeveloperActionImpact.Diagnostic),
        new("logs open", "Logs", DeveloperActionImpact.Diagnostic),
        new("help", "Help", DeveloperActionImpact.Diagnostic)
    ]);

    public static string Normalize(string? command) => string.Join(" ", (command ?? string.Empty)
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    public static DeveloperAction? Find(string? command)
    {
        var normalized = Normalize(command);
        return Actions.FirstOrDefault(action => action.Command == normalized);
    }

    public static IEnumerable<DeveloperAction> Suggest(string? input)
    {
        var normalized = Normalize(input);
        return Actions.Where(action => normalized.Length == 0 || action.Command.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
            action.Title.Contains(input?.Trim() ?? string.Empty, StringComparison.CurrentCultureIgnoreCase));
    }
}
