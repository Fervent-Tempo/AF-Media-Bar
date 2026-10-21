// 应用级快速启动列表与选中目标；设置写入回 UI 线程，容器释放时取消订阅。
using System.IO;
using System.Windows.Threading;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;

namespace AFMediaBar.Classes.Services;

/// <summary>多个宿主共享的快速启动设置及选择；不持有窗口或图标资源。</summary>
public sealed class QuickLaunchService : IDisposable
{
    private readonly Dispatcher _dispatcher = DispatcherHelper.Current;
    private QuickLaunchSettings _observed = SettingsManager.Current.QuickLaunch;
    private bool _disposed;
    private int _wheelDelta;
    public QuickLaunchSettings Settings => SettingsManager.Current.QuickLaunch;
    public QuickLaunchEntry? Selected => ResolveSelected(Settings);
    public event EventHandler<QuickLaunchChangedEventArgs>? Changed;

    public QuickLaunchService() => SettingsManager.SettingsChanged += SettingsChanged;

    public static QuickLaunchEntry? ResolveSelected(QuickLaunchSettings settings) =>
        settings.Entries?.FirstOrDefault(entry => entry.Id == settings.SelectedEntryId) ?? settings.Entries?.FirstOrDefault();

    public void Select(string id, int wheelDelta = 0)
    {
        _dispatcher.VerifyAccess();
        if (_disposed || Settings.SelectedEntryId == id || !(Settings.Entries ?? []).Any(entry => entry.Id == id)) return;
        _wheelDelta = wheelDelta;
        try { SettingsManager.SetQuickLaunchSettings(Settings with { SelectedEntryId = id }); }
        finally { _wheelDelta = 0; }
    }

    public QuickLaunchEntry? MoveSelection(int delta)
    {
        var entries = Settings.Entries ?? [];
        if (entries.Count == 0) return null;
        var index = entries.ToList().FindIndex(entry => entry.Id == Selected?.Id);
        var next = DeferredCircularSelection.Move(Math.Max(0, index), delta, entries.Count);
        if (next < 0) return null;
        Select(entries[next].Id, delta);
        return entries[next];
    }

    public void SetEntries(IEnumerable<QuickLaunchEntry> entries)
    {
        _dispatcher.VerifyAccess();
        if (!_disposed) SettingsManager.SetQuickLaunchSettings(Settings with { Entries = entries.ToArray() });
    }

    public bool Add(QuickLaunchEntry entry)
    {
        var before = Settings.Entries?.Count ?? 0;
        SetEntries((Settings.Entries ?? []).Append(entry));
        return (Settings.Entries?.Count ?? 0) > before;
    }

    public async Task<string> AddFileAsync(string path, MediaSourceDescriptor? source, CancellationToken token)
    {
        var entry = await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            var extension = Path.GetExtension(path);
            var kind = extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ? QuickLaunchTargetKind.Executable :
                extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? QuickLaunchTargetKind.Shortcut : (QuickLaunchTargetKind?)null;
            return kind is null || !File.Exists(path) ? null : new QuickLaunchEntry(Guid.NewGuid().ToString("N"),
                source?.DisplayName ?? Path.GetFileNameWithoutExtension(path), kind.Value, Path.GetFullPath(path), source?.SourceId);
        }, token).ConfigureAwait(false);
        return await _dispatcher.InvokeAsync(() =>
        {
            token.ThrowIfCancellationRequested();
            if (_disposed) throw new OperationCanceledException();
            return entry is null ? "Media.Status.OnlyExistingFiles" : Add(entry) ?
                "Media.Status.AddedToQuickLaunch" : "Media.Status.AlreadyInQuickLaunch";
        });
    }

    private void SettingsChanged(object? sender, SettingsChangedEventArgs args)
    {
        if (args.PropertyName is not (null or nameof(AppSettings.QuickLaunch))) return;
        DispatcherHelper.Run(_dispatcher, () =>
        {
            if (_disposed) return;
            var next = Settings;
            if (_observed.InteractionMode == next.InteractionMode && _observed.ShowApplicationIconOnHover == next.ShowApplicationIconOnHover &&
                _observed.SelectedEntryId == next.SelectedEntryId && (_observed.Entries ?? []).SequenceEqual(next.Entries ?? [])) return;
            _observed = next;
            Changed?.Invoke(this, new(_wheelDelta));
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SettingsManager.SettingsChanged -= SettingsChanged;
    }
}

/// <summary>选择变化的方向仅用于呈现，不携带启动请求。</summary>
public sealed class QuickLaunchChangedEventArgs(int wheelDelta) : EventArgs
{
    public int WheelDelta { get; } = wheelDelta;
}
