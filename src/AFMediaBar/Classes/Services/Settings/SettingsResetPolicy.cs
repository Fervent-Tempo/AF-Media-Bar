// 计算内容页重置值，保留外观页字号及小组件显隐、样式；不发布事件或修改设置。
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services.Settings;

/// <summary>与内容页呈现范围一致的配置重置策略。</summary>
internal static class SettingsResetPolicy
{
    public static TaskbarExperienceSettings Content(TaskbarExperienceSettings current, TaskbarExperienceSettings defaults) =>
        defaults with
        {
            MediaFontSizePercent = current.MediaFontSizePercent,
            SpectrumVisible = current.SpectrumVisible,
            PerformanceVisible = current.PerformanceVisible,
            FullPanel = defaults.FullPanel with { PerformanceVisuals = current.FullPanel.PerformanceVisuals }
        };
}
