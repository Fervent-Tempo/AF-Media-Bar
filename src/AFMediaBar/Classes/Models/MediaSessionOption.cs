namespace AFMediaBar.Classes.Models;

/// <summary>
/// 媒体会话选项：表示右键菜单"切换媒体源"子菜单中的一个会话条目。
/// Media session option: represents one session entry in the "Switch Media Source" context menu.
///
/// 职责 Responsibilities:
/// 1. 标识会话（Key）和来源（SourceId）
///    Identify session (Key) and source (SourceId)
/// 2. 提供显示名称（DisplayName），支持重复来源的编号
///    Provide display name (DisplayName), supports numbering for duplicate sources
/// 3. 指示播放状态（IsPlaying）和选中状态（IsSelected）
///    Indicate playback state (IsPlaying) and selection state (IsSelected)
///
/// 用途 Usage:
/// MediaSessionService 构建会话列表后，通过 SessionsChanged 事件发布给 TaskbarWindow，
/// TaskbarWindow 据此重建右键菜单的"切换媒体源"子菜单项。
/// After MediaSessionService builds the session list, it publishes via SessionsChanged event to TaskbarWindow,
/// which rebuilds the "Switch Media Source" submenu items accordingly.
/// </summary>
/// <param name="Key">会话唯一标识（SMTC Session ID）Unique session identifier (SMTC Session ID)</param>
/// <param name="SourceId">来源标识（AppUserModelId）Source identifier (AppUserModelId)</param>
/// <param name="DisplayName">显示名称（含重复来源编号）Display name (with duplicate source numbering)</param>
/// <param name="IsPlaying">是否正在播放 Whether currently playing</param>
/// <param name="IsSelected">是否为当前选中会话 Whether this is the currently selected session</param>
public sealed record MediaSessionOption(
    string Key,
    string SourceId,
    string DisplayName,
    bool IsPlaying,
    bool IsSelected);
