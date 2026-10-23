using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 为单个播放器宿主串行化背景采样，并淘汰移动、重建或关闭后的旧结果。
/// Serializes background sampling for one player host and rejects results made stale by movement, recreation, or closure.
/// </summary>
internal sealed class AdaptiveForegroundSamplingSession : IDisposable
{
    private readonly ScreenBackgroundSampler _sampler;
    private readonly Dispatcher _dispatcher;
    private readonly Func<IReadOnlyList<Int32Rect>> _regionsProvider;
    private readonly Action<PlayerForegroundDecision?> _apply;
    private readonly ForegroundDecisionConfirmation _confirmation = new();
    private readonly DispatcherTimer _confirmationTimer;
    private CancellationTokenSource _cancellation = new();
    private PlayerForegroundDecision? _lastDecision;
    private Int32Rect[]? _lastRegions;
    private int _generation;
    private bool _sampling;
    private bool _refreshPending;
    private bool _disposed;

    internal AdaptiveForegroundSamplingSession(
        ScreenBackgroundSampler sampler,
        Dispatcher dispatcher,
        Func<Int32Rect?> boundsProvider,
        Action<PlayerForegroundDecision?> apply)
        : this(sampler, dispatcher, (Func<IReadOnlyList<Int32Rect>>)(() => boundsProvider() is { } bounds ? [bounds] : []), apply)
    {
    }

    internal AdaptiveForegroundSamplingSession(
        ScreenBackgroundSampler sampler,
        Dispatcher dispatcher,
        Func<IReadOnlyList<Int32Rect>> regionsProvider,
        Action<PlayerForegroundDecision?> apply)
    {
        _sampler = sampler;
        _dispatcher = dispatcher;
        _regionsProvider = regionsProvider;
        _apply = apply;
        _confirmationTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(150) };
        _confirmationTimer.Tick += ConfirmPending;
    }

    internal void RequestRefresh()
    {
        if (_disposed)
            return;
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(RequestRefresh, DispatcherPriority.Background);
            return;
        }

        var regions = _regionsProvider().Where(bounds => bounds.Width > 0 && bounds.Height > 0).ToArray();
        if (regions.Length == 0)
        {
            AdvanceGeneration();
            _lastRegions = null;
            _refreshPending = false;
            return;
        }

        if (_lastRegions is null || !_lastRegions.SequenceEqual(regions))
        {
            _lastRegions = regions;
            AdvanceGeneration();
        }

        if (_sampling)
        {
            _refreshPending = true;
            return;
        }

        _sampling = true;
        _ = SampleAndApplyAsync(regions, _generation, _lastDecision, _cancellation.Token);
    }

    internal void Invalidate(bool clearDecision)
    {
        if (_disposed)
            return;
        AdvanceGeneration();
        _lastRegions = null;
        _refreshPending = false;
        if (!clearDecision)
            return;

        _lastDecision = null;
        _apply(null);
    }

    private async Task SampleAndApplyAsync(
        IReadOnlyList<Int32Rect> regions,
        int generation,
        PlayerForegroundDecision? previousDecision,
        CancellationToken cancellationToken)
    {
        try
        {
            var samples = await _sampler.SampleAsync(regions, cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
                return;
            var decision = PlayerForegroundPolicy.Resolve(samples, previousDecision);
            // 代际取消只丢弃结果；不要用已取消的令牌创建 DispatcherOperation，
            // 否则正常移动/隐藏会连续制造首次机会 TaskCanceledException。
            await _dispatcher.InvokeAsync(() =>
            {
                if (!PlayerForegroundPolicy.IsCurrent(_disposed, generation, _generation) ||
                    cancellationToken.IsCancellationRequested)
                    return;
                var confirmed = _confirmation.Observe(decision, _lastDecision);
                if (_confirmation.HasPending) _confirmationTimer.Start();
                else _confirmationTimer.Stop();
                if (confirmed is null || _lastDecision == confirmed)
                    return;
                _lastDecision = confirmed;
                _apply(confirmed);
            }, DispatcherPriority.Background);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[AdaptiveForeground] Screen sampling failed: {exception}");
        }
        finally
        {
            if (!_dispatcher.HasShutdownStarted && !_dispatcher.HasShutdownFinished)
            {
                try
                {
                    await _dispatcher.InvokeAsync(() =>
                    {
                        _sampling = false;
                        if (_disposed || !_refreshPending)
                            return;
                        _refreshPending = false;
                        RequestRefresh();
                    }, DispatcherPriority.Background);
                }
                catch (TaskCanceledException)
                {
                }
            }
        }
    }

    private void AdvanceGeneration()
    {
        _confirmationTimer.Stop();
        _confirmation.Reset();
        _generation++;
        _cancellation.Cancel();
        _cancellation.Dispose();
        _cancellation = new CancellationTokenSource();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _confirmationTimer.Stop();
        _confirmationTimer.Tick -= ConfirmPending;
        _generation++;
        _cancellation.Cancel();
        _cancellation.Dispose();
    }

    private void ConfirmPending(object? sender, EventArgs args)
    {
        _confirmationTimer.Stop();
        RequestRefresh();
    }
}
