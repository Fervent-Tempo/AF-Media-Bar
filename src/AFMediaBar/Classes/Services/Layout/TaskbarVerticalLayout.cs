using AFMediaBar.Classes.Models.Layout;

namespace AFMediaBar.Classes.Services.Layout;

/// <summary>
/// 任务栏主题 - 竖向布局（适配任务栏在屏幕左侧或右侧）。
/// Taskbar theme - vertical layout (for taskbar at left or right).
///
/// 特点 Features:
/// - 尺寸：80×168（适配竖向任务栏宽度）
///   Size: 80×168 (adapted for vertical taskbar width)
/// - 组件竖向排列：封面 ↓ 歌曲信息
///   Components arranged vertically: artwork ↓ song info
/// - 封面更大（58×58），文字居中对齐
///   Larger artwork (58×58), center-aligned text
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
///   80px × 168px
/// </summary>
public static class TaskbarVerticalLayout
{
    public static LayoutSchema Create() => new()
    {
        Orientation = LayoutOrientation.Vertical,
        Description = "任务栏主题（竖向）：适配任务栏在屏幕左侧或右侧",
        Canvas = new CanvasConfig
        {
            Width = 80,
            Height = 168,
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
                Bounds = new ComponentBounds(11, 8, 58, 58),
                Properties = new Dictionary<string, object>
                {
                    ["cornerRadius"] = 6.5,
                    ["showPlaceholder"] = true,
                    ["placeholderIcon"] = "MusicNote220",
                    ["placeholderIconSize"] = 35.0
                }
            },

            // 歌曲信息组件（居中对齐，支持换行）MediaText Component (Center-aligned, wrap enabled)
            new ComponentConfig
            {
                Id = "song-info",
                Type = "MediaText",
                Bounds = new ComponentBounds(4, 74, 72, 90),
                Properties = new Dictionary<string, object>
                {
                    ["showTitle"] = true,
                    ["showArtist"] = true,
                    ["titleFontSize"] = 12.0,
                    ["artistFontSize"] = 11.0,
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
