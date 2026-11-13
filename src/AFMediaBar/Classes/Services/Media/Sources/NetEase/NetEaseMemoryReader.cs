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
    private NetEase? _reader;
    private int _processId;

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

        var candidates = ProcessIdentity.Find("cloudmusic.exe", MaximumCandidates);
        var valid = new List<(NetEase Reader, PlayerInfo Info, int ProcessId)>();
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

                valid.Add((reader, info, candidate.ProcessId));
                if (valid.Count > 1)
                    break;
            }
            catch (Exception exception)
            {
                reader?.Dispose();
                MediaReadDiagnostics.Report("netease", "attach", "读取器创建失败", candidate.ProcessId,
                    "cloudmusic.exe", exception);
            }
        }

        if (valid.Count != 1)
        {
            foreach (var item in valid)
                item.Reader.Dispose();
            MediaReadDiagnostics.Report("netease", "attach", $"有效候选数={valid.Count}", 0, "cloudmusic.exe");
            return (null, 0);
        }

        _reader = valid[0].Reader;
        _processId = valid[0].ProcessId;
        MediaReadDiagnostics.Info("netease", $"已附加 pid={_processId} title=\"{valid[0].Info.Title}\"");
        return (valid[0].Info, _processId);
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
