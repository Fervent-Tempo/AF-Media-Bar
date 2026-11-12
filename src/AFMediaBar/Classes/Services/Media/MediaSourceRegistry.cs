// 汇总已注册独立来源的身份、候选及合并策略；不启动或释放 Provider，不拥有外部资源。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services;

/// <summary>通用来源目录，供协调者、过滤和设置页共享同一套来源身份。 / Shares source identity and arbitration across coordination, filtering, and settings.</summary>
public sealed class MediaSourceRegistry
{
    private readonly IMediaSourceProvider[] _providers;
    private readonly IIndependentMediaSourceProvider[] _sources;

    /// <summary>从现有 Provider 注册中发现独立来源能力，不需要另一份注册列表。</summary>
    public MediaSourceRegistry(IEnumerable<IMediaSourceProvider> providers)
    {
        _providers = providers.ToArray();
        _sources = _providers.OfType<IIndependentMediaSourceProvider>().ToArray();
        if (_sources.Select(p => p.SourcePolicy.SourceId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != _sources.Length ||
            _sources.Select(p => p.SourcePolicy.SelectionKey).Distinct(StringComparer.Ordinal).Count() != _sources.Length)
            throw new ArgumentException("Independent media sources must have unique identities and selection keys.", nameof(providers));
    }

    /// <summary>未知应用保留原 ID；已注册来源的别名统一为规范 ID。</summary>
    public string NormalizeSourceId(string sourceId) =>
        _sources.FirstOrDefault(p => p.SourcePolicy.Matches(sourceId))?.SourcePolicy.SourceId ?? sourceId;

    /// <summary>使用提供器声明的能力确定取词所有者，不在 SMTC 构建器维护播放器名单。</summary>
    public bool ProvidesLyrics(string sourceId) =>
        _providers.Any(provider => provider.ProvidesLyrics && provider.CanHandle(sourceId));

    /// <summary>该应用是否有专门适配的通道（进程内存读取一类），用于区分“适配失效”和“本来就不上报”。</summary>
    public bool IsAdapted(string? sourceId) =>
        sourceId is not null && _providers.Any(provider => provider.CanHandle(sourceId));

    /// <summary>使用同一身份规则判断现有允许列表。</summary>
    public bool IsAllowed(string? sourceId, SmtcSourceFilterSettings settings) =>
        MediaSourceFilterPolicy.IsAllowed(sourceId, settings, NormalizeSourceId);

    /// <summary>供设置目录探测运行中的独立播放器。</summary>
    internal IEnumerable<string> IndependentSourceIds => _sources.Select(source => source.SourcePolicy.SourceId);

    /// <summary>合并已检测来源的别名，保留独立来源的规范标识。</summary>
    public IEnumerable<string> DiscoverSourceIds(IEnumerable<string> detectedSourceIds) =>
        detectedSourceIds.Select(NormalizeSourceId)
            .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>通过各来源的纯策略组合候选，不要求存在 SMTC 会话。</summary>
    public IReadOnlyList<MediaSourceCandidate> BuildCandidates(
        IReadOnlyList<MediaSourceCandidate> sessions, IReadOnlyDictionary<IMediaSourceProvider, MediaSnapshot?> snapshots)
    {
        foreach (var source in _sources)
            sessions = source.SourcePolicy.Combine(sessions, snapshots.GetValueOrDefault(source));
        return sessions;
    }

    /// <summary>判断选择项能否在 SMTC 快照缺失时继续解析。</summary>
    public bool IsIndependentSelection(string? key) => Find(key) is not null;

    /// <summary>解析独立来源；null 表示普通 SMTC 选择，断开快照表示独立来源不可用。</summary>
    public MediaSnapshot? ResolveSnapshot(string? key, MediaSnapshot smtc,
        IReadOnlyDictionary<IMediaSourceProvider, MediaSnapshot?> snapshots, SmtcSourceFilterSettings settings)
    {
        var source = Find(key);
        if (source is null) return null;
        return IsAllowed(source.SourcePolicy.SourceId, settings)
            ? source.SourcePolicy.Merge(snapshots.GetValueOrDefault(source), smtc)
            : MediaSnapshot.Disconnected;
    }

    private IIndependentMediaSourceProvider? Find(string? key) =>
        _sources.FirstOrDefault(p => string.Equals(p.SourcePolicy.SelectionKey, key, StringComparison.Ordinal));
}
