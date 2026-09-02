using AFMediaBar.Classes.Models.Layout;

namespace AFMediaBar.Classes.Services.Layout;

/// <summary>
/// 悬浮主题 - 横向布局（独立桌面窗口，横向显示）。
/// Floating theme - horizontal layout (independent desktop window, horizontal display).
///
/// 特点 Features:
/// - 尺寸：300×48（比任务栏模式略大）
///   Size: 300×48 (slightly larger than taskbar mode)
/// - 更大的圆角（8）和更明显的边框
///   Larger corner radius (8) and more prominent border
/// - 适合桌面悬浮显示，视觉独立性更强
///   Suitable for desktop floating display, stronger visual independence
///
/// 组件布局 Component Layout:
/// ┌──────────────────────────────────────────┐
/// │  [封面]  歌曲标题 - 艺术家  [控制按钮]    │
/// │   36px     190px               46px      │
/// └──────────────────────────────────────────┘
///    300px × 48px
/// </summary>
public static class FloatingHorizontalLayout
{
    public static LayoutSchema Create() => new()
    {
        Orientation = LayoutOrientation.Horizontal,
        Description = "悬浮主题（横向）：独立桌面窗口，横向显示",
        Canvas = new CanvasConfig
        {
            Width = 300,
            Height = 48,
            Background = "#CC000000",
            CornerRadius = 8,
            Border = new BorderConfig
            {
                Thickness = 1.5,
                Color = "#60FFFFFF",
                TopOnly = false
            },
            Effects = new EffectsConfig
            {
                Blur = 100,
                BackgroundOpacity = 0.5
            }
        },
        Components = new List<ComponentConfig>
        {
            // 封面组件（略大）Artwork Component (Slightly Larger)
            new ComponentConfig
            {
                Id = "artwork",
                Type = "Artwork",
                Bounds = new ComponentBounds(6, 6, 36, 36),
                Properties = new Dictionary<string, object>
                {
                    ["cornerRadius"] = 6.0,
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
                Bounds = new ComponentBounds(50, 6, 190, 36),
                Properties = new Dictionary<string, object>
                {
                    ["showTitle"] = true,
                    ["showArtist"] = true,
                    ["titleFontSize"] = 14.0,
                    ["artistFontSize"] = 12.0,
                    ["artistOpacity"] = 0.6,
                    ["layout"] = "vertical",
                    ["verticalAlignment"] = "center"
                }
            },

            // 播放控制组件 PlaybackControls Component
            new ComponentConfig
            {
                Id = "controls",
                Type = "PlaybackControls",
                Bounds = new ComponentBounds(248, 6, 46, 36),
                Properties = new Dictionary<string, object>
                {
                    ["buttonSize"] = 22.0,
                    ["spacing"] = 6.0,
                    ["orientation"] = "horizontal"
                }
            }
        }
    };
}
