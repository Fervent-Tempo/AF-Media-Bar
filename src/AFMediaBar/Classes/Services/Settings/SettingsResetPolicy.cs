// 计算内容页重置值，保留外观页字号及组件样式；不发布事件或修改设置。
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services.Settings;

/// <summary>与内容页呈现范围一致的配置重置策略。</summary>
internal static class SettingsResetPolicy
{
    public static TaskbarExperienceSettings Content(TaskbarExperienceSettings current, TaskbarExperienceSettings defaults) =>
        Rest(defaults with
        {
            MediaFontSizePercent = current.MediaFontSizePercent,
            FullPanel = defaults.FullPanel with { PerformanceVisuals = current.FullPanel.PerformanceVisuals }
        }, current);

    /// <summary>仅恢复常驻页拥有的字段，保留布局、快捷控制和完整面板。</summary>
    public static TaskbarExperienceSettings Rest(TaskbarExperienceSettings current, TaskbarExperienceSettings defaults) =>
        current with
        {
            ArtworkVisible = defaults.ArtworkVisible,
            SmallArtwork = defaults.SmallArtwork,
            ArtworkProgressVisible = defaults.ArtworkProgressVisible,
            RestProgressVisible = defaults.RestProgressVisible,
            OutputDeviceVisible = defaults.OutputDeviceVisible,
            InputDeviceVisible = defaults.InputDeviceVisible,
            VolumeVisible = defaults.VolumeVisible,
            SpectrumVisible = defaults.SpectrumVisible,
            PerformanceVisible = defaults.PerformanceVisible,
            RestComponentOrder = defaults.RestComponentOrder,
            IdleComponents = defaults.IdleComponents,
            ComponentLayouts = current.ComponentLayouts is null && defaults.ComponentLayouts?.Resident is null && defaults.ComponentLayouts?.Idle is null
                ? null : (current.ComponentLayouts ?? new()) with
            {
                Resident = defaults.ComponentLayouts?.Resident,
                Idle = defaults.ComponentLayouts?.Idle
            }
        };
}
