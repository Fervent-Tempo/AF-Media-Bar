using AFMediaBar.Classes.Models.Layout;

namespace AFMediaBar.Classes.Services.Layout;

/// <summary>
/// 任务栏主题 - 竖向布局（适配任务栏在屏幕左侧或右侧）。
/// Taskbar theme - vertical layout (for taskbar at left or right).
///
/// 特点 Features:
/// - 尺寸：72×152（适配竖向任务栏宽度）
///   Size: 72×152 (adapted for vertical taskbar width)
/// - 组件竖向排列：封面 ↓ 歌曲信息
///   Components arranged vertically: artwork ↓ song info
/// - 封面更大（52×52），文字居中对齐
///   Larger artwork (52×52), center-aligned text
///
/// 组件布局 Component Layout:
/// ┌──────────┐
/// │  [封面]  │  52×52
/// │          │
/// ├──────────┤
/// │  歌曲名  │
/// │          │  80px
/// │  艺术家  │
/// ├──────────┤
/// │ [控制]   │  24px
/// └──────────┘
///   72px × 152px
/// </summary>
public static class TaskbarVerticalLayout
{
    public static LayoutSchema Create() => new()
    {
        Orientation = LayoutOrientation.Vertical,
        Description = "任务栏主题（竖向）：适配任务栏在屏幕左侧或右侧",
        Canvas = new CanvasConfig
        {
            Width = 72,
            Height = 152,
            Background = "#00000000",
            CornerRadius = 6,
            Border = new BorderConfig
            {
                Thickness = 1.25,
                Color = "#00FFFFFF",
                TopOnly = false
            },
            Effects = new EffectsConfig
            {
                Blur = 80,
                BackgroundOpacity = 0.4
            }
        },
        Components = new List<ComponentConfig>
        {
            // 封面组件（更大）Artwork Component (Larger)
            new ComponentConfig
            {
                Id = "artwork",
                Type = "Artwork",
                Bounds = new ComponentBounds(10, 8, 52, 52),
                Properties = new Dictionary<string, object>
                {
                    ["cornerRadius"] = 6.0,
                    ["showPlaceholder"] = true,
                    ["placeholderIcon"] = "MusicNote220",
                    ["placeholderIconSize"] = 32.0
                }
            },

            // 歌曲信息组件（居中对齐，支持换行）MediaText Component (Center-aligned, wrap enabled)
            new ComponentConfig
            {
                Id = "song-info",
                Type = "MediaText",
                Bounds = new ComponentBounds(4, 68, 64, 80),
                Properties = new Dictionary<string, object>
                {
                    ["showTitle"] = true,
                    ["showArtist"] = true,
                    ["titleFontSize"] = 11.0,
                    ["artistFontSize"] = 10.0,
                    ["artistOpacity"] = 0.5,
                    ["layout"] = "vertical",
                    ["textAlignment"] = "center",
                    ["maxLines"] = 4,
                    ["textWrapping"] = "wrap"
                }
            }
        }
    };
}
