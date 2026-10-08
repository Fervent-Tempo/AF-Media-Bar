// 验证对齐变化能同步刷新实际下拉框，以及新默认值不会覆盖已保存的选择。
using System.Reflection;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Settings;
using AFMediaBar.ViewModels.Pages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>内容排布绑定及默认文字对齐的回归。</summary>
[TestClass]
[DoNotParallelize]
public sealed class TaskbarArrangementSettingsTests
{
    private sealed class Monitors : IDisplayMonitorService
    {
        public event EventHandler? MonitorsChanged { add { } remove { } }
        public IReadOnlyList<DisplayMonitorInfo> GetMonitors() => [];
        public void Refresh() { }
        public DisplayMonitorInfo? ResolveFixedMonitor(string? deviceId) => null;
        public DisplayMonitorInfo? ResolveNotificationMonitor(NotificationTargetMode mode, string? deviceId) => null;
        public bool IsForegroundWindowFullscreen() => false;
    }

    [TestMethod]
    public void SwitchingPositionRefreshesTheEffectiveArrangementInTheComboBox()
    {
        StaTest.Run(_ =>
        {
            var original = SettingsManager.Current.Clone();
            DisplayModesViewModel? vm = null;
            using var localization = new LocalizationService();
            try
            {
                SettingsManager.Current = new AppSettings { Position = TaskbarBarPosition.End };
                vm = new DisplayModesViewModel(new Monitors(), localization);
                var combo = new ComboBox { DataContext = vm, SelectedValuePath = "Tag" };
                combo.Items.Add(new ComboBoxItem { Tag = TaskbarContentArrangement.Left });
                combo.Items.Add(new ComboBoxItem { Tag = TaskbarContentArrangement.Right });
                combo.SetBinding(Selector.SelectedValueProperty,
                    new Binding(nameof(DisplayModesViewModel.TaskbarArrangement)) { Mode = BindingMode.TwoWay });
                Assert.AreEqual(TaskbarContentArrangement.Right, combo.SelectedValue);
                vm.TaskbarPosition = TaskbarBarPosition.Start;
                Assert.AreEqual(TaskbarContentArrangement.Left, vm.TaskbarArrangement);
                Assert.AreEqual(TaskbarContentArrangement.Left, combo.SelectedValue);
                Assert.IsNull(SettingsManager.Current.TaskbarExperience.Arrangement);
                combo.SelectedValue = TaskbarContentArrangement.Right;
                Assert.AreEqual(TaskbarContentArrangement.Right, SettingsManager.Current.TaskbarExperience.Arrangement);
                vm.TaskbarPosition = TaskbarBarPosition.End;
                Assert.IsNull(SettingsManager.Current.TaskbarExperience.Arrangement);
                SettingsManager.Current.Position = TaskbarBarPosition.Start;
                Assert.AreEqual(TaskbarContentArrangement.Left, combo.SelectedValue);
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

    [TestMethod]
    public void CenteredDefaultsPreserveExplicitOldValuesAndUserDefaultSnapshots()
    {
        var original = SettingsManager.Current.Clone();
        var originalDefaults = SettingsManager.UserDefaults;
        try
        {
            var defaults = JsonSerializer.Deserialize<AppSettings>("{}")!;
            Assert.AreEqual(TaskbarMediaTextAlignment.Center, defaults.TaskbarExperience.MediaTextAlignment);
            Assert.AreEqual(LyricsTextAlignment.Center, defaults.LyricsTextAlignment);
            var saved = JsonSerializer.Deserialize<AppSettings>("{\"LyricsTextAlignment\":0,\"TaskbarExperience\":{\"MediaTextAlignment\":0}}")!;
            Assert.AreEqual(TaskbarMediaTextAlignment.Left, saved.Clone().TaskbarExperience.Normalize().MediaTextAlignment);
            Assert.AreEqual(LyricsTextAlignment.Left, saved.Clone().LyricsTextAlignment);
            SettingsManager.SetUserDefaults(saved);
            SettingsManager.ResetAppearance();
            SettingsManager.ResetLyrics();
            Assert.AreEqual(TaskbarMediaTextAlignment.Left, SettingsManager.Current.TaskbarExperience.MediaTextAlignment);
            Assert.AreEqual(LyricsTextAlignment.Left, SettingsManager.Current.LyricsTextAlignment);
            SettingsManager.SetUserDefaults(null);
            SettingsManager.ResetAppearance();
            SettingsManager.ResetLyrics();
            Assert.AreEqual(TaskbarMediaTextAlignment.Center, SettingsManager.Current.TaskbarExperience.MediaTextAlignment);
            Assert.AreEqual(LyricsTextAlignment.Center, SettingsManager.Current.LyricsTextAlignment);
        }
        finally
        {
            SettingsManager.SetUserDefaults(originalDefaults);
            SettingsManager.Replace(original);
        }
    }
}
