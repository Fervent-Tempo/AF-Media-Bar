// 来源编排回归，防止官方直链重新抢到镜像之前或忽略明确禁用加速的清单。
using AFMediaBar.Classes.Models.Updates;
using AFMediaBar.Classes.Services.Updates;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class UpdateSourcePlanTests
{
    [TestMethod]
    public void MirrorsComeBeforeOfficialUrlsAndDuplicateSourcesAreAttemptedOnce()
    {
        const string Official = "https://github.com/example/app/releases/download/v1/setup.exe";
        const string Mirror = "https://mirror.example/setup.exe";
        var manifest = Manifest([Official, Mirror, Official], ["https://proxy.example/", "https://proxy.example/"]);
        var plan = UpdateSourcePlanPolicy.BuildDownloadPlan(manifest);
        CollectionAssert.AreEqual(new[] { "https://proxy.example/" + Official, Mirror, Official }, plan.Select(source => source.Url).ToArray());
        Assert.IsTrue(plan[0].IsAccelerated);
        Assert.IsFalse(plan[^1].IsAccelerated);
        Assert.AreEqual(Official, UpdateSourcePlanPolicy.SelectNext(plan, plan.Take(2).Select(source => source.Url).ToArray())?.Url);
    }

    [TestMethod]
    public void ExplicitlyDisabledAccelerationKeepsMirrorsAndOfficialFallbackOnly()
    {
        const string Official = "https://github.com/example/app/releases/download/v1/setup.exe";
        const string Mirror = "https://mirror.example/setup.exe";
        var plan = UpdateSourcePlanPolicy.BuildDownloadPlan(Manifest([Official, Mirror], []));
        CollectionAssert.AreEqual(new[] { Mirror, Official }, plan.Select(source => source.Url).ToArray());
        Assert.IsTrue(UpdateSourcePlanPolicy.IsManualOnly(plan, [Mirror, Official]));
    }

    private static UpdateManifest Manifest(string[] urls, string[] accelerators) => new("1.0", null, null, [], null, null, null, false,
        urls.Select(url => new UpdatePackageAsset(url, null, new string('a', 64))).ToArray(), accelerators, null);
}
