// 后台读取边界：发现网易云进程并独占其读取器；多个候选只接受恰好一个完整有效的播放进程。
// 双读/多进程/失败关闭思路参考 EricHongXDD/ChassisCanvas；该项目无许可证，本实现只借鉴设计，不复制代码。
using System.Diagnostics;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Media.Readers;
using AFMediaBar.Classes.Services.Media.Sources.Shared;

namespace AFMediaBar.Classes.Services.Media.Sources.NetEase;

/// <summary>一次网易云进程读取，允许独立测试取消和资源释放而不访问真实进程。</summary>
internal interface INetEaseMemoryReader : IDisposable
{
    (PlayerInfo? Info, int ProcessId) Read(MediaSnapshot session);
}

/// <summary>
/// 在进程变化、退出或候选失效时替换并释放网易云读取器；多个进程只有恰好一个有效播放者时才发布。
/// Replaces and disposes the NetEase reader when the process changes, exits, or a candidate fails; publishes only when exactly one process is a valid player.
/// </summary>
internal sealed class NetEaseMemoryReader : INetEaseMemoryReader
{
    private const int MaximumCandidates = 8;
    private readonly Func<string, int, (IReadOnlyList<ProcessIdentity.Candidate> Candidates, int? UnreadableProcessId)> _findCandidates;
    private NetEase? _reader;
    private int _processId;

    public NetEaseMemoryReader()
        : this(ProcessIdentity.Find)
    {
    }

    /// <summary>
    /// 用注入的进程发现替代真实枚举，便于测试「客户端没开」这条路径而不依赖本机是否装了网易云。
    /// </summary>
    /// <remarks>
    /// Uses an injected process lookup instead of the real enumeration, so the "client not running" path can be tested without
    /// depending on whether NetEase is installed on the machine.
    /// </remarks>
    internal NetEaseMemoryReader(
        Func<string, int, (IReadOnlyList<ProcessIdentity.Candidate> Candidates, int? UnreadableProcessId)> findCandidates) =>
        _findCandidates = findCandidates;

    public (PlayerInfo? Info, int ProcessId) Read(MediaSnapshot session)
    {
        var expectedTitle = session.IsConnected ? session.Title : null;
        var expectedArtist = session.IsConnected ? session.Artist : null;
        var expectedDuration = session.IsConnected && session.Duration > 0 ? session.Duration : (double?)null;

        // 窗口/进程未变时复用已验证的读取器；身份失效立即释放并重新发现。
        if (_reader is { } cached && _processId > 0 &&
            ProcessIdentity.IsExpectedImage(_processId, "cloudmusic.exe", out _))
        {
            var info = Read(cached, expectedTitle, expectedArtist, expectedDuration);
            if (info is not null)
                return (info, _processId);
            Dispose();
        }

        var (candidates, unreadableProcessId) = _findCandidates("cloudmusic.exe", MaximumCandidates);
        if (candidates.Count == 0)
        {
            if (unreadableProcessId is { } processId)
                MediaReadDiagnostics.Report("netease", "attach", "无法验证进程映像", processId, "cloudmusic.exe");
            return (null, 0);
        }

        var collected = new List<(NetEase Reader, PlayerInfo Info, int ProcessId)>();
        foreach (var candidate in candidates)
        {
            NetEase? reader = null;
            try
            {
                if (!ProcessIdentity.IsExpectedImage(candidate.ProcessId, "cloudmusic.exe", out _))
                    continue;
                reader = new NetEase(candidate.ProcessId);
                if (Read(reader, expectedTitle, expectedArtist, expectedDuration) is not { } info)
                {
                    reader.Dispose();
                    continue;
                }

                collected.Add((reader, info, candidate.ProcessId));
                // 两个候选都能给出曲目身份才算歧义；只有时间轴的候选不构成"另一个播放器"。
                if (collected.Count > 1 && SelectCandidate(collected.Select(item => item.Info.Title).ToList()) < 0)
                    break;
            }
            catch (Exception exception)
            {
                reader?.Dispose();
                MediaReadDiagnostics.Report("netease", "attach", "读取器创建失败", candidate.ProcessId,
                    "cloudmusic.exe", exception);
            }
        }

        var chosen = SelectCandidate(collected.Select(item => item.Info.Title).ToList());
        if (chosen < 0)
        {
            foreach (var item in collected)
                item.Reader.Dispose();
            MediaReadDiagnostics.Report("netease", "attach",
                $"有效候选数={collected.Count} 带元数据={collected.Count(item => !string.IsNullOrWhiteSpace(item.Info.Title))}",
                0, "cloudmusic.exe");
            return (null, 0);
        }

        foreach (var (item, index) in collected.Select((item, index) => (item, index)))
        {
            if (index != chosen)
                item.Reader.Dispose();
        }

        _reader = collected[chosen].Reader;
        _processId = collected[chosen].ProcessId;
        MediaReadDiagnostics.Info("netease", $"已附加 pid={_processId} title=\"{collected[chosen].Info.Title}\"");
        return (collected[chosen].Info, _processId);
    }

    /// <summary>
    /// 从候选中选出唯一的播放器：优先带元数据的候选（它更能证明这是正在播放的客户端），
    /// 都只有时间轴时必须恰好一个，其余情况一律判为歧义而不发布。
    /// Picks the single player out of the candidates: a candidate carrying metadata wins because only it proves this is the client
    /// actually playing, and when every candidate has a bare timeline exactly one of them is required; anything else is ambiguous.
    /// </summary>
    /// <param name="titles">各候选读到的曲名。/ Title read from each candidate.</param>
    /// <returns>选中的下标；无法唯一确定时为 -1。/ Index of the pick, or -1 when it cannot be determined uniquely.</returns>
    internal static int SelectCandidate(IReadOnlyList<string> titles)
    {
        var withMetadata = new List<int>();
        for (var index = 0; index < titles.Count; index++)
        {
            if (!string.IsNullOrWhiteSpace(titles[index]))
                withMetadata.Add(index);
        }

        if (withMetadata.Count == 1)
            return withMetadata[0];
        return withMetadata.Count == 0 && titles.Count == 1 ? 0 : -1;
    }

    private static PlayerInfo? Read(NetEase reader, string? expectedTitle, string? expectedArtist,
        double? expectedDuration)
    {
        try
        {
            return reader.GetPlayerInfo(expectedTitle, expectedArtist, expectedDuration);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[NetEaseMemoryReader] Read failed: {exception.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        _reader?.Dispose();
        _reader = null;
        _processId = 0;
    }
}
