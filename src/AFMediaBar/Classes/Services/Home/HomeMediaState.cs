// 应用持有只读转接器，释放时解除对现有媒体服务的订阅。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
namespace AFMediaBar.Classes.Services.Home;

/// <summary>转发现有媒体事件，不创建或停止来源。</summary>
public sealed class HomeMediaState : IHomeMediaState, IDisposable
{
    private readonly MediaSessionService _media;
    public MediaSnapshot? Snapshot => _media.CurrentSnapshot;
    public IReadOnlyList<MediaSessionOption> Sessions => _media.CurrentSessionOptions;
    public event EventHandler? Changed;
    public HomeMediaState(MediaSessionService media)
    {
        _media = media;
        _media.SnapshotChanged += OnSnapshot;
        _media.SessionsChanged += OnSessions;
    }
    private void OnSnapshot(object? sender, MediaSnapshot snapshot) => Changed?.Invoke(this, EventArgs.Empty);
    private void OnSessions(IReadOnlyList<MediaSessionOption> sessions) => Changed?.Invoke(this, EventArgs.Empty);
    public void Dispose() { _media.SnapshotChanged -= OnSnapshot; _media.SessionsChanged -= OnSessions; }
}
