// 控件级呈现接线借用共享选择服务；卸载时停止呈现，最终释放时取消订阅，不释放共享服务。
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using System.Windows;

namespace AFMediaBar.Components;

/// <summary>把应用级选择转换为单个小音符的呈现状态。</summary>
internal sealed partial class QuickLaunchControlSession : IDisposable
{
    private readonly TaskBarMediaControl _owner;
    private readonly QuickLaunchService _selection;
    private readonly Action _invalidate;
    private QuickLaunchSettings _settings;
    private bool _disposed;
    public QuickLaunchControlSession(TaskBarMediaControl owner, QuickLaunchService selection, Action invalidate, QuickLaunchIconService? icons)
    {
        _owner = owner; _selection = selection; _invalidate = invalidate;
        _settings = selection.Settings;
        InitializeIcons(icons);
        selection.Changed += Changed;
        owner.Loaded += Loaded;
        Refresh(0);
    }
    private void Loaded(object sender, RoutedEventArgs args) => Refresh(0);
    private void Changed(object? sender, QuickLaunchChangedEventArgs args)
    {
        if (_disposed) return;
        var next = _selection.Settings;
        if (_settings.InteractionMode != next.InteractionMode ||
            _settings.ShowApplicationIconOnHover != next.ShowApplicationIconOnHover ||
            !(_settings.Entries ?? []).SequenceEqual(next.Entries ?? []))
        {
            _owner.CancelQuickLaunchInteraction();
            _owner.InvalidateQuickLaunchRequests();
            _invalidate();
        }
        _settings = next;
        Refresh(args.WheelDelta);
    }
    private void Refresh(int wheelDelta)
    {
        if (_disposed) return;
        _owner.ApplyQuickLaunchState(_selection.Settings, _selection.Selected, wheelDelta);
        RefreshIcon(wheelDelta);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeIcons();
        _selection.Changed -= Changed;
        _owner.Loaded -= Loaded;
        _owner.CancelQuickLaunchInteraction();
    }
}
