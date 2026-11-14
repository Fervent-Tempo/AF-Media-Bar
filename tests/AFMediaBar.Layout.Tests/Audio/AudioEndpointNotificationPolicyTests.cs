// 验证音频端点属性通知过滤：采集启停键不触发刷新，其它属性仍触发。
using AFMediaBar.Classes.Services.Audio;
using NAudio.CoreAudioApi;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests.Audio;

[TestClass]
public sealed class AudioEndpointNotificationPolicyTests
{
    [TestMethod]
    public void CaptureLifecyclePropertyIsIgnored()
    {
        var key = new PropertyKey(new Guid("c4f6b0fa-fd32-4c99-8050-8f8a193a7e48"), 3);

        Assert.IsTrue(AudioEndpointNotificationPolicy.IsCaptureState(key));
        Assert.IsFalse(AudioEndpointNotificationPolicy.ShouldInvalidate(key));
    }

    [TestMethod]
    public void OtherEndpointPropertyStillInvalidates()
    {
        var other = new PropertyKey(new Guid("c4f6b0fa-fd32-4c99-8050-8f8a193a7e48"), 2);
        var otherFormat = new PropertyKey(new Guid("11111111-2222-3333-4444-555555555555"), 3);

        Assert.IsFalse(AudioEndpointNotificationPolicy.IsCaptureState(other));
        Assert.IsTrue(AudioEndpointNotificationPolicy.ShouldInvalidate(other));
        Assert.IsTrue(AudioEndpointNotificationPolicy.ShouldInvalidate(otherFormat));
    }
}
