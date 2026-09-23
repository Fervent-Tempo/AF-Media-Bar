using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class MediaTransportPolicyTests
{
    [TestMethod]
    public void ExplicitPlayPauseWorksWithoutToggleSupport()
    {
        Assert.AreEqual(MediaPlayPauseAction.Pause, MediaTransportPolicy.ResolvePlayPause(true, false, true, true));
        Assert.AreEqual(MediaPlayPauseAction.Play, MediaTransportPolicy.ResolvePlayPause(false, false, true, true));
    }

    [TestMethod]
    public void UnsupportedDirectionDoesNotAdvertiseAControl()
    {
        Assert.AreEqual(MediaPlayPauseAction.None, MediaTransportPolicy.ResolvePlayPause(true, false, true, false));
        Assert.AreEqual(MediaPlayPauseAction.None, MediaTransportPolicy.ResolvePlayPause(false, false, false, true));
        Assert.AreEqual(MediaPlayPauseAction.None, MediaTransportPolicy.ResolvePlayPause(false, false, false, false));
    }

    [TestMethod]
    public void ToggleOnlySourcesKeepTheirExistingCommand()
    {
        Assert.AreEqual(MediaPlayPauseAction.Toggle, MediaTransportPolicy.ResolvePlayPause(true, true, false, false));
        Assert.AreEqual(MediaPlayPauseAction.Toggle, MediaTransportPolicy.ResolvePlayPause(false, true, false, false));
    }
}
