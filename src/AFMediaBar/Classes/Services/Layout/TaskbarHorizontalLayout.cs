using AFMediaBar.Classes.Models.Layout;

namespace AFMediaBar.Classes.Services.Layout;

/// <summary>
/// 任务栏主题 - 横向布局（适配任务栏在屏幕顶部或底部）。
/// Taskbar theme - horizontal layout (for taskbar at top or bottom).
///
/// 特点 Features:
/// - 尺寸：310×40（匹配 TaskbarWindow 尺寸）
///   Size: 310×40 (matches TaskbarWindow size)
/// - 组件横向排列：封面 | 歌曲信息 | 控制按钮
///   Components arranged horizontally: artwork | song info | controls
/// - 适合宽度有限的任务栏环境
///   Suitable for taskbar environment with limited width
///
/// 组件布局 Component Layout:
/// ┌────────────────────────────────────────┐
/// │  [封面]  歌曲标题 - 艺术家  [控制按钮]  │
/// │   36px     210px               40px    │
/// └────────────────────────────────────────┘
///    310px × 40px
/// </summary>
public static class TaskbarHorizontalLayout
{
    public static LayoutSchema Create() => new()
    {
        Orientation = LayoutOrientation.Horizontal,
        Description = "任务栏主题（横向）：适配任务栏在屏幕顶部或底部",
        Canvas = new CanvasConfig
        {
            Width = 310,
            Height = 40,
            Background = "#00000000",
            CornerRadius = 6,
            Border = new BorderConfig
            {
                Thickness = 1.25,
                Color = "#00FFFFFF",
                TopOnly = true
            },
            Effects = new EffectsConfig
            {
                Blur = 80,
                BackgroundOpacity = 0.4
            }
        },
        Components = new List<ComponentConfig>
        {
            // 封面组件 Artwork Component
            new ComponentConfig
            {
                Id = "artwork",
                Type = "Artwork",
                Bounds = new ComponentBounds(4, 2, 36, 36),
                Properties = new Dictionary<string, object>
                {
                    ["cornerRadius"] = 5.0,
                    ["showPlaceholder"] = true,
                    ["placeholderIcon"] = "MusicNote220",
                    ["placeholderIconSize"] = 24.0
                }
            },

            // 歌曲信息组件 MediaText Component
            new ComponentConfig
            {
                Id = "song-info",
                Type = "MediaText",
                Bounds = new ComponentBounds(48, 2, 210, 36),
                Properties = new Dictionary<string, object>
                {
                    ["showTitle"] = true,
                    ["showArtist"] = true,
                    ["titleFontSize"] = 13.0,
                    ["artistFontSize"] = 11.0,
                    ["artistOpacity"] = 0.5,
                    ["layout"] = "vertical",
                    ["verticalAlignment"] = "center"
                }
            },

            // 播放控制组件 PlaybackControls Component
            new ComponentConfig
            {
                Id = "controls",
                Type = "PlaybackControls",
                Bounds = new ComponentBounds(266, 2, 40, 36),
                Properties = new Dictionary<string, object>
                {
                    ["buttonSize"] = 20.0,
                    ["spacing"] = 4.0,
                    ["orientation"] = "horizontal"
                }
            }
        }
    };
}
