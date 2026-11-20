// 验证网易云客户端没开时的处理：那是正常状态，不该被当成读取失败反复上报；进程存在却读不出元数据才是真故障。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Media.Readers;
using AFMediaBar.Classes.Services.Media.Sources.NetEase;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 网易云客户端没开时的读取行为：静默等待而不是按故障上报。
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class NetEaseClientNotRunningTests
{
    [TestMethod]
    public void NoClientProcessIsNotReportedAsFailure()
    {
        // 客户端没开：发现不到任何候选进程，直接返回空，不进入失败上报的分支。
        using var reader = new NetEaseMemoryReader((_, _) => []);

        var (info, processId) = reader.Read(MediaSnapshot.Disconnected);

        Assert.IsNull(info, "客户端没开时没有可读的信息。");
        Assert.AreEqual(0, processId, "客户端没开时没有进程可归属。");
    }

    [TestMethod]
    public void CandidateLookupIsAskedForTheNetEaseClient()
    {
        string? requestedName = null;
        var requestedMaximum = 0;
        using var reader = new NetEaseMemoryReader((name, maximum) =>
        {
            requestedName = name;
            requestedMaximum = maximum;
            return [];
        });

        reader.Read(MediaSnapshot.Disconnected);

        Assert.AreEqual("cloudmusic.exe", requestedName, "只应查找网易云客户端。");
        Assert.IsTrue(requestedMaximum > 0, "候选数量上限应当为正。");
    }

    [TestMethod]
    public void ClientRunningButUnreadableStillCountsAsFailure()
    {
        // 进程在、却读不出曲目身份：这是真故障，仍然要能被 SelectCandidate 判为歧义而拒绝发布。
        Assert.AreEqual(-1, NetEaseMemoryReader.SelectCandidate([]),
            "没有有效候选时不得选出任何一个。");
        Assert.AreEqual(-1, NetEaseMemoryReader.SelectCandidate(["A", "B"]),
            "两个都能给出曲目身份时视为歧义，不选。");
        Assert.AreEqual(0, NetEaseMemoryReader.SelectCandidate(["A"]),
            "只有一个有效候选时选中它。");
    }
}
