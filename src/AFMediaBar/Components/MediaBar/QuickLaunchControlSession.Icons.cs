// 每个控件持有自己的请求令牌和代际；后台位图来自共享服务，移出/卸载后不应用旧结果。
using System.Windows;
using AFMediaBar.Classes.Services;

namespace AFMediaBar.Components;

internal sealed partial class QuickLaunchControlSession
{
    private QuickLaunchIconService? _icons;
    private CancellationTokenSource? _iconRequest;
    private int _iconVersion;
    private string? _requestedIcon;

    private void InitializeIcons(QuickLaunchIconService? icons)
    {
        _icons = icons;
        _owner.QuickLaunchHoverChanged += HoverChanged;
        _owner.Unloaded += Unloaded;
        _owner.IsVisibleChanged += VisibilityChanged;
    }
    private void HoverChanged(bool hovering)
    {
        if (hovering) RefreshIcon(0);
        else Suspend(immediate: false);
    }
    private void Unloaded(object sender, RoutedEventArgs args) => Suspend();
    private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (_owner.IsVisible) RefreshIcon(0);
        else Suspend();
    }
    internal void Suspend(bool immediate = true)
    {
        _iconVersion++;
        _iconRequest?.Cancel();
        _iconRequest?.Dispose();
        _iconRequest = null;
        _requestedIcon = null;
        if (immediate) _owner.ResetQuickLaunchVisuals();
        else _owner.ShowQuickLaunchIcon(null, 0);
    }
    private void RefreshIcon(int wheelDelta)
    {
        if (_disposed) return;
        if (!_owner.CanShowQuickLaunchIcon || _icons is null || _selection.Selected is not { } target)
        { Suspend(); return; }
        var key = $"{target.Id}:{target.Kind}:{target.Target}";
        if (key == _requestedIcon) return;
        _iconRequest?.Cancel();
        _iconRequest?.Dispose();
        var request = new CancellationTokenSource();
        _iconRequest = request;
        _requestedIcon = key;
        var version = ++_iconVersion;
        _ = LoadIconAsync(target, wheelDelta, version, request.Token);
    }
    private async Task LoadIconAsync(AFMediaBar.Classes.Models.QuickLaunchEntry target, int wheelDelta, int version, CancellationToken token)
    {
        System.Windows.Media.Imaging.BitmapSource? icon;
        try
        {
            var loading = _icons!.GetAsync(target, token);
            // 冷读取期间淡回音符，避免旧应用图标长时间代表已经切换的目标；缓存命中不打断滚动。
            if (!loading.IsCompletedSuccessfully) _owner.ShowQuickLaunchIcon(null, 0);
            icon = await loading.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
            icon = null;
        }
        if (token.IsCancellationRequested || _owner.Dispatcher.HasShutdownStarted || _owner.Dispatcher.HasShutdownFinished) return;
        try
        {
            await _owner.Dispatcher.InvokeAsync(() =>
            {
                if (_disposed || token.IsCancellationRequested || version != _iconVersion || !_owner.CanShowQuickLaunchIcon ||
                    _selection.Selected?.Id != target.Id) return;
                _owner.ShowQuickLaunchIcon(icon, wheelDelta);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { System.Diagnostics.Debug.WriteLine(exception); }
    }
    private void DisposeIcons()
    {
        Suspend();
        _owner.QuickLaunchHoverChanged -= HoverChanged;
        _owner.Unloaded -= Unloaded;
        _owner.IsVisibleChanged -= VisibilityChanged;
    }
}
