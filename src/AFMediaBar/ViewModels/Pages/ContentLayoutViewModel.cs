// 组合当前上下文的布局、三层内容、歌词文字区和组件编辑器；订阅与释放由各编辑器的 DI 作用域负责。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Models.Settings;

namespace AFMediaBar.ViewModels.Pages;

/// <summary>内容与布局页的编辑器组合，不持有媒体、音频或窗口资源。</summary>
public sealed class ContentLayoutViewModel(
    DisplayModesViewModel layers,
    TaskbarAppearanceViewModel layout,
    LyricsViewModel lyrics,
    ComponentsSettingsViewModel components,
    ISettingsConfiguration configuration)
{
    public DisplayModesViewModel Layers { get; } = layers;
    public TaskbarAppearanceViewModel Layout { get; } = layout;
    public LyricsViewModel Lyrics { get; } = lyrics;
    public ComponentsSettingsViewModel Components { get; } = components;
    public CancellationToken ContextCancellationToken => configuration.CancellationToken;

    public void ResetContentLayout()
    {
        if (configuration.IsActive && configuration.Context.Mode == SettingsMode.Taskbar) SettingsManager.ResetContentLayout();
    }
}
