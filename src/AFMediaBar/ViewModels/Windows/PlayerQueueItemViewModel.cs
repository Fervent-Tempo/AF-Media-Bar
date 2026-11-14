using System.Windows.Media;
using AFMediaBar.Classes.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AFMediaBar.ViewModels.Windows;

/// <summary>队列面板的一行；只包装真实快照数据，不自行计算播放顺序。</summary>
public partial class PlayerQueueItemViewModel : ObservableObject
{
    public PlayerQueueItemViewModel(int number, PlayerCompanionTrack track, bool isCurrent)
    {
        Number = number;
        Track = track;
        _isCurrent = isCurrent;
    }

    /// <summary>在真实队列中的 1-based 序号，仅用于展示。</summary>
    public int Number { get; }

    public PlayerCompanionTrack Track { get; }

    public string TrackId => Track.TrackId;

    public string Title => Track.Title;

    public string Subtitle
    {
        get
        {
            var artists = string.Join(" / ", Track.Artists);
            if (string.IsNullOrWhiteSpace(Track.Album))
                return artists;
            return string.IsNullOrWhiteSpace(artists) ? Track.Album : $"{artists} · {Track.Album}";
        }
    }

    public string DurationText => FormatDuration(Track.DurationMs);

    public string AutomationName => $"{Number}. {Title}, {Subtitle}, {DurationText}";

    /// <summary>是否为 Nons 当前播放项；只由快照索引驱动。</summary>
    [ObservableProperty]
    private bool _isCurrent;

    /// <summary>低分辨率封面缩略图；未加载或失败时为 null，界面显示主题占位图。</summary>
    [ObservableProperty]
    private ImageSource? _artwork;

    /// <summary>当前是否有可见容器需要封面；行被虚拟化回收时阻止迟到结果继续占用内存。</summary>
    public bool IsArtworkRequested { get; set; }

    internal static string FormatDuration(long durationMs)
    {
        var duration = TimeSpan.FromMilliseconds(Math.Max(0, durationMs));
        return duration.TotalHours >= 1 ? duration.ToString(@"h\:mm\:ss") : duration.ToString(@"m\:ss");
    }
}
