namespace AFMediaBar.Classes.Models;

/// <summary>
/// 会话列表中的一项：标识会话、来源与选择状态，供切换 UI 绑定。
/// One entry in the session list: identifies the session, its source, and selection state.
/// </summary>
public sealed record MediaSessionOption(
    string Key,
    string SourceId,
    string DisplayName,
    bool IsPlaying,
    bool IsSelected);
