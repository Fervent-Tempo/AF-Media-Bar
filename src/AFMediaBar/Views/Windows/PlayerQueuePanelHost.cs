// 播放队列浮层的唯一宿主：按需创建、复用/替换并释放窗口；窗口本身不持有业务服务生命周期。
using System.Diagnostics;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;

namespace AFMediaBar.Views.Windows;

/// <summary>同一时间只保留一个播放队列浮层；Esc、外部点击或再次点击入口关闭。</summary>
public sealed class PlayerQueuePanelHost(Func<PlayerQueuePanelWindow> factory, AppLogService? log = null) : IDisposable
{
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(250);
    private readonly Func<PlayerQueuePanelWindow> _factory = factory;
    private readonly AppLogService? _log = log;
    private PlayerQueuePanelWindow? _window;
    private long _lastOutsideDismissedAt;
    private bool _disposed;

    /// <summary>在队列按钮锚点处显示；已显示时切换为关闭，刚被外部点击关闭时不立即重开。</summary>
    public void Show(TrayIconBounds anchor)
    {
        if (_disposed)
            return;

        if (_window is { } current)
        {
            if (!current.IsClosing)
                current.RequestClose();
            return;
        }

        if (_lastOutsideDismissedAt != 0 &&
            Stopwatch.GetElapsedTime(_lastOutsideDismissedAt) < ReopenGuard)
            return;

        PlayerQueuePanelWindow panel;
        try
        {
            panel = _factory();
        }
        catch (Exception exception)
        {
            _log?.Warn("Nons", "创建播放队列面板失败", exception);
            return;
        }

        _window = panel;
        panel.Closed += OnPanelClosed;
        panel.ShowNear(anchor);
    }

    /// <summary>关闭当前浮层；退出或 Explorer 重建时调用，重复调用安全。</summary>
    public void CloseAll()
    {
        _window?.RequestClose();
    }

    private void OnPanelClosed(object? sender, EventArgs e)
    {
        if (sender is not PlayerQueuePanelWindow panel)
            return;

        panel.Closed -= OnPanelClosed;
        if (panel.LastOutsideDismissedAt != 0)
            _lastOutsideDismissedAt = panel.LastOutsideDismissedAt;
        if (ReferenceEquals(_window, panel))
            _window = null;
        panel.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        var window = _window;
        _window = null;
        if (window is null)
            return;

        window.Closed -= OnPanelClosed;
        window.Dispose();
    }
}
