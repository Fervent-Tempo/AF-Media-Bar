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
    private readonly Func<Int32Rect?> _boundsProvider;
    private readonly Action<PlayerForegroundDecision?> _apply;
    private CancellationTokenSource _cancellation = new();
    private PlayerForegroundDecision? _lastDecision;
    private Int32Rect? _lastBounds;
    private int _generation;
    private bool _sampling;
    private bool _refreshPending;
    private bool _disposed;

    internal AdaptiveForegroundSamplingSession(
        ScreenBackgroundSampler sampler,
        Dispatcher dispatcher,
        Func<Int32Rect?> boundsProvider,
        Action<PlayerForegroundDecision?> apply)
    {
        _sampler = sampler;
        _dispatcher = dispatcher;
        _boundsProvider = boundsProvider;
        _apply = apply;
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

        if (_boundsProvider() is not { } bounds || bounds.Width <= 0 || bounds.Height <= 0)
            return;

        if (_lastBounds is not { } previousBounds || !previousBounds.Equals(bounds))
        {
            _lastBounds = bounds;
            AdvanceGeneration();
        }

        if (_sampling)
        {
            _refreshPending = true;
            return;
        }

        _sampling = true;
        _ = SampleAndApplyAsync(bounds, _generation, _lastDecision, _cancellation.Token);
    }

    internal void Invalidate(bool clearDecision)
    {
        if (_disposed)
            return;
        AdvanceGeneration();
        _lastBounds = null;
        _refreshPending = false;
        if (!clearDecision)
            return;

        _lastDecision = null;
        _apply(null);
    }

    private async Task SampleAndApplyAsync(
        Int32Rect bounds,
        int generation,
        PlayerForegroundDecision? previousDecision,
        CancellationToken cancellationToken)
    {
        try
        {
            var samples = await _sampler.SampleAsync(bounds, cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
                return;
            var decision = PlayerForegroundPolicy.Resolve(samples, previousDecision);
            // 代际取消只丢弃结果；不要用已取消的令牌创建 DispatcherOperation，
            // 否则正常移动/隐藏会连续制造首次机会 TaskCanceledException。
            await _dispatcher.InvokeAsync(() =>
            {
                if (!PlayerForegroundPolicy.IsCurrent(_disposed, generation, _generation) ||
                    cancellationToken.IsCancellationRequested || decision is null)
                    return;
                if (_lastDecision == decision)
                    return;
                _lastDecision = decision;
                _apply(decision);
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
        _generation++;
        _cancellation.Cancel();
        _cancellation.Dispose();
    }
}
