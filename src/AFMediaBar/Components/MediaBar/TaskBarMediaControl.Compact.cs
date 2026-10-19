// 小任务栏只投影当前画面；原始媒体、歌词及用户偏好始终保留。
using System.Windows.Controls;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Components;

public partial class TaskBarMediaControl
{
    private TaskbarTextBudget _textBudget = new(1, 2, false);
    private double _naturalTitleSize = 14;
    private double _naturalArtistSize = 12;
    private double _naturalLyricSize = 12;
    private bool _hasAutomaticallyHiddenInformation;
    /// <summary>当前是否实际收起了已存在的副信息，供宿主决定是否提示。</summary>
    public bool HasAutomaticallyHiddenInformation => _hasAutomaticallyHiddenInformation;
    /// <summary>首次进入一次真实收起状态时发出，不代表应重复显示通知。</summary>
    public event EventHandler? InformationAutomaticallyHidden;
    private bool UsesAdaptiveTaskbarText => _currentMode == WindowMode.Taskbar;
    private double EffectiveLyricFontSize => _isVertical ? VerticalPrimaryFontSize :
        _naturalLyricSize * (UsesAdaptiveTaskbarText ? _textBudget.FontScale : 1);

    private string VisibleMetadataTitle(TaskbarExperienceSettings experience) =>
        _isSmallTaskbar ? _actualTitle :
            TaskbarExperiencePolicy.FormatMetadataTitle(_actualTitle, _actualArtist, experience.ContentLayout);

    private LyricsPresentationFrame VisibleLyricsFrame(LyricsPresentationFrame raw) =>
        SmallTaskbarPresentationPolicy.Project(raw, UsesAdaptiveTaskbarText && _textBudget.HideSecondary);

    private void RefreshTaskbarTextBudget(LyricsPresentationFrame raw)
    {
        if (!IsBarPresentation) return;
        var scale = PresentationSettings.TaskbarExperience.Normalize().MediaFontSizePercent / 100d;
        var primary = _isVertical ? VerticalTaskbarPresentationPolicy.PrimaryFontSizeDip * scale :
            raw.IsVisible ? _naturalLyricSize : _naturalTitleSize;
        var secondary = _isVertical ? VerticalTaskbarPresentationPolicy.SecondaryFontSizeDip * scale :
            raw.IsVisible ? _naturalLyricSize : _naturalArtistSize;
        var gap = raw.IsVisible ? 2 + primary * PresentationSettings.LyricsLineGapPercent / 100 : 2;
        var cross = _isVertical ? MainBorder.Width : MainBorder.Height;
        var budget = UsesAdaptiveTaskbarText ? SmallTaskbarPresentationPolicy.Resolve(cross, _isVertical, primary, secondary, gap)
            : new TaskbarTextBudget(1, 2, false);
        var changed = _textBudget != budget;
        _textBudget = budget;
        _isSmallTaskbar = UsesAdaptiveTaskbarText && budget.HideSecondary;

        if (!_isVertical)
        {
            SongTitle.FontSize = Math.Max(1, _naturalTitleSize * budget.FontScale);
            SongArtist.FontSize = Math.Max(1, _naturalArtistSize * budget.FontScale);
            if (IsBarPresentation && !_restTransitionActive && !_restTransitionPreparing)
            {
                var available = Math.Max(1, cross - budget.PaddingDip * 2);
                Canvas.SetTop(SongInfoStackPanel, budget.PaddingDip);
                SongInfoStackPanel.Height = available;
                SongLyricsPanel.Height = available;
                SongMetadataPanel.VerticalAlignment = VerticalAlignment.Center;
                SongTitleContainer.Height = Math.Min(available, Math.Ceiling(SongTitle.FontSize * 1.25));
                SongArtistContainer.Height = Math.Min(available, Math.Ceiling(SongArtist.FontSize * 1.25));
                SongArtistContainer.Margin = new Thickness(0, _isSmallTaskbar ? 0 : -1.5, 0, 0);
                var buttonSize = Math.Min(TaskbarDensityMetrics.From(PresentationSettings.TaskbarExperience.Density).ButtonSize, available);
                foreach (var surface in new[] { TaskbarOutputDeviceSurface, TaskbarInputDeviceSurface, TaskbarVolumeSurface })
                    surface.Width = surface.Height = buttonSize;
            }
        }
        if (!_restTransitionKeepsOutgoingText)
        {
            var fallbackLyrics = raw.IsVisible && (SongLyricsPanel.Opacity <= 0 || _lyrics._webLyricsGraphicsDisabled);
            SongTitle.Text = fallbackLyrics ? raw.Current : VisibleMetadataTitle(PresentationSettings.TaskbarExperience);
            if (fallbackLyrics) SongTitle.FontSize = EffectiveLyricFontSize;
            SongArtistContainer.Visibility = !fallbackLyrics && !_isSmallTaskbar && !string.IsNullOrEmpty(_actualArtist) &&
                (_isVertical || PresentationSettings.TaskbarExperience.ContentLayout != TaskbarContentLayout.CompactInline)
                ? Visibility.Visible : Visibility.Collapsed;
        }
        var hasSecondary = raw.IsVisible ? !string.IsNullOrWhiteSpace(raw.Next) || !string.IsNullOrWhiteSpace(raw.CurrentTranslation) :
            !string.IsNullOrWhiteSpace(_snapshot.Artist) || !string.IsNullOrWhiteSpace(_snapshot.SourceName);
        var suppressed = UsesAdaptiveTaskbarText && _snapshot.IsConnected && _textBudget.HideSecondary && hasSecondary &&
            SongInfoStackPanel.Visibility == Visibility.Visible && !_isHostVisibilitySuspended;
        if (suppressed != _hasAutomaticallyHiddenInformation)
        {
            _hasAutomaticallyHiddenInformation = suppressed;
            if (suppressed) InformationAutomaticallyHidden?.Invoke(this, EventArgs.Empty);
        }
        if (changed) _lyrics.ApplyWebLyricsStyle();
    }
}
