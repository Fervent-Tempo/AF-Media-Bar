// 验证多任务栏的共同范围不会制造超出真实空间的上限，不操作用户配置。
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Settings;
using AFMediaBar.ViewModels.Pages;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>任务栏运行时长度约束回归。</summary>
[TestClass]
[DoNotParallelize]
public sealed class TaskbarLengthConstraintsTests
{
    [TestMethod]
    public void DisjointHostRangesDoNotInflateTheSmallestMaximum()
    {
        var service = new TaskbarLengthConstraintsService();
        var large = new object();
        var small = new object();
        service.Update(large, 300, 500);
        service.Update(small, 200, 250);
        Assert.AreEqual(250.0, service.MaximumLengthDip);
        Assert.IsFalse(service.HasAvailableRange);
        service.Remove(small);
        Assert.IsTrue(service.HasAvailableRange);
        service.Update(large, 300, 0);
        Assert.IsFalse(service.HasAvailableRange);
    }
    [TestMethod]
    public void RuntimeDisplayCoercionDoesNotRewriteSavedFixedLength()
    {
        StaTest.Run(_ =>
        {
            var original = SettingsManager.Current.Clone();
            AppearanceViewModel? vm = null;
            using var localization = new LocalizationService();
            try
            {
                SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with
                { FixedLengthDip = 900, LengthMode = TaskbarLengthMode.Fixed };
                var constraints = new TaskbarLengthConstraintsService();
                constraints.Update(new object(), 200, 400);
                vm = new AppearanceViewModel(localization, constraints);
                Assert.AreEqual(400.0, vm.FixedTaskbarLengthDip);
                vm.FixedTaskbarLengthDip = 400;
                Assert.AreEqual(900.0, SettingsManager.Current.TaskbarExperience.FixedLengthDip);
                vm.FollowMediaTextLength = true;
                vm.FollowMediaTextLength = false;
                Assert.AreEqual(900.0, SettingsManager.Current.TaskbarExperience.FixedLengthDip);
                vm.FixedTaskbarLengthDip = 350;
                Assert.AreEqual(350.0, SettingsManager.Current.TaskbarExperience.FixedLengthDip);
            }
            finally
            {
                if (vm is not null)
                {
                    var handlers = (EventHandler<SettingsChangedEventArgs>?)typeof(SettingsManager)
                        .GetField("SettingsChanged", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
                    foreach (var handler in handlers?.GetInvocationList() ?? [])
                        if (ReferenceEquals(handler.Target, vm)) SettingsManager.SettingsChanged -= (EventHandler<SettingsChangedEventArgs>)handler;
                }
                SettingsManager.Replace(original);
            }
            return Task.CompletedTask;
        });
    }

}
