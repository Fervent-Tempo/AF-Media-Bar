// 拥有各显示器完整面板及关闭冷却时间；应用退出时同步关闭，业务服务由组合根持有。
using System.Windows;
using System.Diagnostics.CodeAnalysis;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Views.Windows;

/// <summary>管理完整面板的创建、复用、呈现上下文与最终释放。</summary>
public sealed class FullPanelWindowHost(Func<TaskbarFullPanelWindow> factory, MemoryPruneCoordinator prune) : IDisposable
{
    private readonly Dictionary<string, TaskbarFullPanelWindow> _windows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _closedAt = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;
    public event Action<string>? PanelClosed;

    public bool TryGet(string target, [NotNullWhen(true)] out TaskbarFullPanelWindow? window) => _windows.TryGetValue(target, out window);

    public TaskbarFullPanelWindow? Open(string target, Rect anchor, bool vertical, bool ensureOpen, AppSettings? settings, FullPanelPlacementSide? side = null)
    {
        if (_disposed) return null;
        if (!_windows.TryGetValue(target, out var panel))
        {
            if (_closedAt.TryGetValue(target, out var closed) && DateTime.UtcNow - closed < TimeSpan.FromMilliseconds(350)) return null;
            panel = factory();
            _windows[target] = panel;
            panel.Closed += Closed;
        }
        panel.ApplyPresentationSettings(settings);
        if (ensureOpen) panel.ShowNear(anchor, target, vertical, side);
        else panel.ToggleNear(anchor, target, vertical, side);
        return panel;
    }

    public void ApplyPresentationSettings(AppSettings? settings)
    {
        foreach (var window in _windows.Values) window.ApplyPresentationSettings(settings);
    }

    private void Closed(object? sender, EventArgs args)
    {
        if (sender is not TaskbarFullPanelWindow window) return;
        window.Closed -= Closed;
        var target = _windows.FirstOrDefault(entry => ReferenceEquals(entry.Value, window)).Key;
        if (string.IsNullOrWhiteSpace(target)) return;
        _windows.Remove(target);
        _closedAt[target] = DateTime.UtcNow;
        PanelClosed?.Invoke(target);
        prune.RequestTrim(MemoryTrimTrigger.PanelClosed);
    }

    public void CloseAll()
    {
        foreach (var window in _windows.Values.Distinct().ToArray()) window.RequestClose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var window in _windows.Values.Distinct().ToArray())
        {
            window.Closed -= Closed;
            window.Close();
        }
        _windows.Clear();
        _closedAt.Clear();
        PanelClosed = null;
    }
}
