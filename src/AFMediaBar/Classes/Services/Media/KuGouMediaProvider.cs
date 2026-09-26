using System.Windows.Threading;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Players;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 读取酷狗客户端内存，把更精确的播放进度与时长合并进酷狗的 SMTC 快照；元数据、封面、歌词与播放状态仍由 SMTC 提供。
/// Reads KuGou client memory and merges a more precise playback position and duration into KuGou's SMTC snapshot; metadata, artwork,
/// lyrics, and playback state still come from SMTC.
/// </summary>
public sealed class KuGouMediaProvider : IMediaSourceProvider, IMemoryPrunable
{
    /// <summary>
    /// 内存里的进度只有整秒精度，界面按 <c>TimelineUpdatedAt</c> 外推（见 <c>TaskbarExperiencePolicy.GetPosition</c>），
    /// 因此轮询只需要把时间戳的新鲜度维持在半秒左右；更快的频率改变不了整秒跳变，只是在空转。
    /// The memory position is whole-second only and the UI extrapolates from <c>TimelineUpdatedAt</c> (see
    /// <c>TaskbarExperiencePolicy.GetPosition</c>), so polling only has to keep the timestamp fresher than about half a second; a faster
    /// cadence cannot change the one-second quantization, it would only spin.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(233);

    /// <summary>空闲档位的轮询周期，与网易云提供器一致：空闲时两秒一次足够在按下播放后很快跟上。/ The poll period at the idle level, matching the NetEase provider: once every two seconds still follows a play press closely.</summary>
    private const int IdlePollIntervalMilliseconds = 2_000;

    /// <summary>曲目时长的可信上限（一天）：酷狗更新移动偏移后读到的会是不相干的数据，用上限挡掉而不是展示。/ Plausible maximum of a track duration (one day): after a KuGou update shifts the offsets the bytes read are unrelated data, which this cap keeps off the UI.</summary>
    private const double MaximumPlausibleDurationSeconds = 86_400;

    /// <summary>进度允许超出时长的舍入余量（秒）：曲末进度与时长各自取整，先后切换一秒是正常的。/ Rounding slack, in seconds, by which the position may exceed the duration: both values are whole seconds and they do not flip in the same poll at a track's end.</summary>
    private const double PositionRoundingSlackSeconds = 2;

    private readonly Dispatcher _dispatcher;
    private CancellationTokenSource? _cancellation;
    private KuGou? _memoryPlayer;
    private MediaSnapshot _sessionSnapshot = MediaSnapshot.Disconnected;
    private bool _isDisposed;

    /// <summary>
    /// 轮询周期。剪枝会改写它，而轮询线程在另一个线程上读取，因此这是一个 volatile 字段而不是配置常量。
    /// The poll period. Pruning rewrites it and the polling thread reads it from another thread, so it is a volatile field rather than a constant.
    /// </summary>
    private volatile int _pollIntervalMilliseconds = (int)PollInterval.TotalMilliseconds;

    public event Action<IMediaSourceProvider, MediaSnapshot?>? SnapshotChanged;

    public KuGouMediaProvider()
        => _dispatcher = Application.Current.Dispatcher;

    /// <summary>
    /// 判断来源标识是否属于酷狗音乐（令牌与 MediaSourceProcessResolver 的映射一致）。
    /// Determines whether the source identifier belongs to KuGou Music (the tokens follow MediaSourceProcessResolver's mapping).
    /// </summary>
    public bool CanHandle(string sourceId) =>
        sourceId.Contains("kugou", StringComparison.OrdinalIgnoreCase) ||
        sourceId.Contains("kgmusic", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 更新 SMTC 基线快照；本提供器不从内存读元数据，基线就是合并的底稿。
    /// Updates the SMTC baseline snapshot; this provider reads no metadata from memory, so the baseline is the merge base.
    /// </summary>
    public void UpdateSessionSnapshot(MediaSnapshot snapshot)
        => _sessionSnapshot = snapshot;

    /// <summary>
    /// 幂等启动来源轮询；重复调用不会创建额外计时器。
    /// Idempotently starts source polling without creating additional timers on repeated calls.
    /// </summary>
    public void Start()
    {
        if (_isDisposed || _cancellation is not null)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        _ = PollAsync(cancellation, cancellation.Token);
    }

    /// <summary>
    /// 停止轮询并释放内存读取器，阻止释放后继续发布快照。
    /// Stops polling and disposes the active memory reader so no snapshots are published after disposal.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _cancellation?.Cancel();
        _memoryPlayer?.Dispose();
        _memoryPlayer = null;
    }

    /// <summary>参与者名称，只用于诊断。/ Participant name, used for diagnostics only.</summary>
    public string PruneParticipantName => "kugou-source";

    /// <summary>
    /// 放慢或停掉内存轮询。没有缓存可清（元数据由 SMTC 基线自带），档位只作用于轮询节奏。
    /// Slows down or stops the memory poll. There are no caches to drop (metadata travels with the SMTC baseline), so the level only
    /// drives the polling cadence.
    /// </summary>
    /// <param name="level">目标档位。/ The target level.</param>
    public void Prune(MemoryPruneLevel level)
    {
        if (_isDisposed)
        {
            return;
        }

        if (level >= MemoryPruneLevel.DisplayOff)
        {
            StopPolling();
            return;
        }

        _pollIntervalMilliseconds = level == MemoryPruneLevel.Idle
            ? IdlePollIntervalMilliseconds
            : (int)PollInterval.TotalMilliseconds;

        // 从 DisplayOff 回到低档位时轮询是停着的，这里按需重启（Start 自身幂等）。
        // Coming back from DisplayOff the poll is stopped, so it is restarted here on demand; Start is idempotent by itself.
        Start();
    }

    /// <summary>
    /// 停止轮询并释放内存读取器，保留提供器本身可再次启动。
    /// Stops polling and releases the memory reader while keeping the provider restartable.
    /// </summary>
    private void StopPolling()
    {
        var cancellation = _cancellation;
        _cancellation = null;
        cancellation?.Cancel();
        ResetMemoryPlayer();
    }

    private async Task PollAsync(CancellationTokenSource cancellation, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var baseline = _sessionSnapshot;
                MediaSnapshot? merged = null;
                if (baseline.IsConnected && CanHandle(baseline.SourceId))
                {
                    try
                    {
                        merged = ReadTimeline(baseline);
                    }
                    catch
                    {
                        ResetMemoryPlayer();
                    }
                }
                else
                {
                    // 酷狗不是当前来源时不保留它的进程句柄：没有快照可合并，白持有一个句柄只会拖住下一次附加。
                    // When KuGou is not the current source its process handle is not kept: there is nothing to merge, and holding the
                    // handle would only weigh on the next attach.
                    ResetMemoryPlayer();
                }

                if (_isDisposed)
                {
                    return;
                }

                Publish(merged);
                await Task.Delay(_pollIntervalMilliseconds, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_cancellation, cancellation))
            {
                _cancellation = null;
            }

            cancellation.Dispose();
        }
    }

    /// <summary>
    /// 从内存读出进度与时长，除时间轴外原样保留 SMTC 基线。读取失败或数值不可信时返回 null，
    /// <see cref="MediaSessionService"/> 随即退回纯 SMTC 快照——内存读取只是增强，失败永远不该让界面变差。
    /// Reads position and duration from memory and keeps the SMTC baseline as-is except for the timeline. Returns null on a failed read or
    /// an implausible value, and <see cref="MediaSessionService"/> then falls back to the pure SMTC snapshot — the memory read is an
    /// enhancement, and failing must never make the UI worse.
    /// </summary>
    private MediaSnapshot? ReadTimeline(MediaSnapshot baseline)
    {
        if (_memoryPlayer is null && (_memoryPlayer = KuGou.Attach()) is null)
        {
            return null;
        }

        if (!_memoryPlayer.TryReadTimeline(out var position, out var duration))
        {
            // 链断了（酷狗更新移动了偏移，或进程正好退出）：丢弃读取器，下一次轮询重新附加。
            // The chain is broken (a KuGou update moved the offsets, or the process just exited): drop the reader and re-attach on the
            // next poll.
            ResetMemoryPlayer();
            return null;
        }

        // 读到了不代表可信：偏移失效后读到的是不相干的数据。时长必须落在正数与上限之间；进度只容许因取整
        // 而在曲末略微越过时长。
        // A successful read is not necessarily a believable one: with stale offsets the bytes belong to something else. The duration must
        // fall between zero and the cap, and the position may exceed the duration only slightly, from rounding at a track's end.
        if (duration <= 0 || duration > MaximumPlausibleDurationSeconds ||
            position > duration + PositionRoundingSlackSeconds)
        {
            return null;
        }

        return baseline with
        {
            Position = position,
            Duration = duration,
            TimelineUpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    private void Publish(MediaSnapshot? snapshot)
    {
        if (_isDisposed || _dispatcher.HasShutdownStarted)
        {
            return;
        }

        _dispatcher.BeginInvoke(
            () => SnapshotChanged?.Invoke(this, snapshot),
            DispatcherPriority.Background);
    }

    private void ResetMemoryPlayer()
    {
        _memoryPlayer?.Dispose();
        _memoryPlayer = null;
    }
}
