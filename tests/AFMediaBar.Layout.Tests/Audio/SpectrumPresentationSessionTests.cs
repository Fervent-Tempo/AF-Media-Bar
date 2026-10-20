// 验证采集失效后清除已呈现频谱；使用真实缓存读取路径，不启动音频后台服务或枚举设备。
using System.Reflection;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Audio;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
[DoNotParallelize]
public sealed class SpectrumPresentationSessionTests
{
    [TestMethod]
    public void UnavailableCaptureClearsPreviouslyPresentedFrame()
    {
        StaTest.Run(async dispatcher =>
        {
            var original = SettingsManager.Current;
            try
            {
                SettingsManager.Current = new AppSettings
                {
                    SpectrumComponent = SpectrumComponentSettings.Default with
                    { CaptureSource = SpectrumCaptureSource.AllApplications }
                };
                using var devices = new AudioCaptureDeviceResolver(new MediaSourceProcessResolver());
                using var monitor = new AudioMonitorService(devices,
                    new SpectrumCaptureProcessResolver(new MediaSourceProcessResolver()));
                // 与既有采集回归相同，准备真实正弦样本和缓存标记，不创建原生采集对象。
                const int sampleRate = 48000;
                var monitorType = typeof(AudioMonitorService);
                monitorType.GetMethod("ConfigureFft", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(monitor, [sampleRate]);
                monitorType.GetField("_sampleRate", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(monitor, sampleRate);
                var push = monitorType.GetMethod("PushSample", BindingFlags.Instance | BindingFlags.NonPublic)!;
                for (var index = 0; index < SpectrumAnalysisPolicy.ResolveFftSize(sampleRate); index++)
                    push.Invoke(monitor, [(float)Math.Sin(2 * Math.PI * 1000 * index / sampleRate)]);
                typeof(AudioMonitorService).GetField("_captureReady", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(monitor, true);
                var lastPacket = monitorType.GetField("_lastPacketTick", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var frameReady = new TaskCompletionSource<float[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                var awaitingActiveFrame = true;
                using var session = new SpectrumPresentationSession(monitor, dispatcher,
                    () =>
                    {
                        lastPacket.SetValue(monitor, Environment.TickCount64);
                        return SettingsManager.Current.SpectrumComponent;
                    }, () => true,
                    (bands, count) =>
                    {
                        var frame = bands.AsSpan(0, count).ToArray();
                        if (!awaitingActiveFrame || frame.Any(value => value > 0)) frameReady.TrySetResult(frame);
                    },
                    exception => frameReady.TrySetException(exception));
                session.Start();
                var active = await frameReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.IsTrue(active.Any(value => value > 0));

                monitor.ResetAfterEnvironmentChange();
                awaitingActiveFrame = false;
                frameReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var cleared = await frameReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.IsTrue(cleared.All(value => value == 0), "失效采集不能让旧频谱停留在画面中。");
            }
            finally { SettingsManager.Current = original; }
        });
    }
}
