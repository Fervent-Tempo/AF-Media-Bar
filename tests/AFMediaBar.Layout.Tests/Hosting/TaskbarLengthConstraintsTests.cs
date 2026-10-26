// 验证多任务栏的共同范围不会制造超出真实空间的上限，不操作用户配置。
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Settings;
using AFMediaBar.ViewModels.Pages;
using System.Text.Json;
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
        Assert.IsTrue(service.HasSources);
        service.Remove(small);
        Assert.IsTrue(service.HasAvailableRange);
        service.Update(large, 300, 0);
        Assert.IsFalse(service.HasAvailableRange);
        Assert.IsFalse(service.HasSources);
    }
    [TestMethod]
    public void RuntimeDisplayCoercionDoesNotRewriteSavedFixedLength()
    {
        StaTest.Run(_ =>
        {
            var original = SettingsManager.Current.Clone();
            TaskbarAppearanceViewModel? vm = null;
            using var localization = new LocalizationService();
            using var context = new SettingsPageContext();
            var configuration = new LegacySettingsConfiguration(context);
            try
            {
                SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with
                { FixedLengthDip = 900, LengthMode = TaskbarLengthMode.Fixed };
                var constraints = new TaskbarLengthConstraintsService();
                constraints.Update(new object(), 200, 400);
                vm = new TaskbarAppearanceViewModel(configuration, constraints, localization);
                Assert.AreEqual(400.0, vm.FixedTaskbarLengthDip);
                vm.FixedTaskbarLengthDip = 400;
                Assert.AreEqual(900.0, SettingsManager.Current.TaskbarExperience.FixedLengthDip);
                vm.FollowMediaTextLength = true;
                vm.FollowMediaTextLength = false;
                Assert.AreEqual(900.0, SettingsManager.Current.TaskbarExperience.FixedLengthDip);
                vm.FixedTaskbarLengthDip = 350;
                Assert.AreEqual(350.0, SettingsManager.Current.TaskbarExperience.FixedLengthDip);
                var conflicting = new object();
                constraints.Update(conflicting, 450, 500);
                Assert.IsFalse(vm.CanEditFixedTaskbarLength);
                vm.FixedTaskbarLengthDip = 470;
                Assert.AreEqual(350.0, SettingsManager.Current.TaskbarExperience.FixedLengthDip);
                constraints.Remove(conflicting);
            }
            finally
            {
                vm?.Dispose();
                SettingsManager.Replace(original);
            }
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void HiddenBarLengthCanBeEditedAndRoundedWithoutRuntimeSources()
    {
        StaTest.Run(_ =>
        {
            var previous = SettingsManager.Current;
            using var localization = new LocalizationService();
            using var context = new SettingsPageContext();
            try
            {
                SettingsManager.Current = JsonSerializer.Deserialize<AppSettings>(
                    """{"TaskbarExperience":{"LengthMode":1,"IdleComponents":[],"FixedLengthDip":180.5}}""")!;
                using var vm = new TaskbarAppearanceViewModel(new LegacySettingsConfiguration(context),
                    new TaskbarLengthConstraintsService(), localization);
                Assert.IsTrue(vm.CanEditFixedTaskbarLength);
                Assert.AreEqual(120.0, vm.FixedTaskbarLengthMinimum);
                Assert.AreEqual(4096.0, vm.FixedTaskbarLengthMaximum);
                Assert.AreEqual(181.0, vm.FixedTaskbarLengthDip);
                vm.FixedTaskbarLengthDip = 250.5;
                Assert.AreEqual(251.0, SettingsManager.Current.TaskbarExperience.FixedLengthDip);
                Assert.AreEqual(0, SettingsManager.Current.TaskbarExperience.IdleComponents!.Count);
            }
            finally { SettingsManager.Current = previous; }
            return Task.CompletedTask;
        });
    }

}
