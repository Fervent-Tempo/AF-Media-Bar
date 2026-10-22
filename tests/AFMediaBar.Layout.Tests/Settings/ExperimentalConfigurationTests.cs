// 验证旧设置读取、悬浮配置独立编辑与默认快照重置；结束恢复全局设置。
using System.Text.Json;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Models.Settings;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.ViewModels.Pages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ExperimentalConfigurationTests
{
    [TestMethod]
    public void ExistingPagesSharePresentationAndInteractionAcrossHosts()
    {
        var previous = SettingsManager.Current;
        using var page = new SettingsPageContext();
        page.Initialize(SettingsContext.Initial with { Mode = SettingsMode.FloatingBar });
        var configuration = new LegacySettingsConfiguration(page);
        try
        {
            SettingsManager.Current = new AppSettings { Experiments = new() { FloatingBar = true } };
            var taskbarAppearance = SettingsManager.Current.Appearance;
            var taskbarInteraction = SettingsManager.Current.Interaction;
            configuration.SetTaskbarAppearance(configuration.Current.Appearance with { TaskbarBackgroundOpacityPercent = 63 });
            configuration.SetAppearance(configuration.Current.Appearance with { FontWeight = 700 });
            Assert.AreEqual(63, SettingsManager.Current.Appearance.TaskbarBackgroundOpacityPercent);
            Assert.AreEqual(63, configuration.Current.Appearance.TaskbarBackgroundOpacityPercent);
            Assert.AreEqual(700, SettingsManager.Current.Appearance.FontWeight);
            configuration.SetInteractionSettings(configuration.Current.Interaction with
            {
                PrimaryWheelAction = WheelAction.Disabled,
                TrayClickAction = TrayClickAction.OpenSettings
            });
            Assert.AreEqual(WheelAction.Disabled, SettingsManager.Current.Interaction.PrimaryWheelAction);
            Assert.AreEqual(TrayClickAction.OpenSettings, SettingsManager.Current.Interaction.TrayClickAction);
            Assert.AreEqual(WheelAction.Disabled, configuration.Current.Interaction.PrimaryWheelAction);
            var taskbarLength = SettingsManager.Current.TaskbarExperience.FixedLengthDip;
            configuration.SetTaskbarExperience(configuration.Current.TaskbarExperience with { MediaFontSizePercent = 120, FixedLengthDip = 444 });
            Assert.AreEqual(120, SettingsManager.Current.TaskbarExperience.MediaFontSizePercent);
            Assert.AreEqual(taskbarLength, SettingsManager.Current.TaskbarExperience.FixedLengthDip);
            Assert.AreEqual(444d, configuration.Current.TaskbarExperience.FixedLengthDip);
            SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with { MediaFontSizePercent = 110 };
            Assert.AreEqual(110, configuration.Current.TaskbarExperience.MediaFontSizePercent);
            Assert.AreEqual(444d, configuration.Current.TaskbarExperience.FixedLengthDip);
            configuration.SetExperiments(new());
            configuration.SetLyricsFixedWidthDip(550);
            Assert.AreEqual(550, configuration.Current.LyricsFixedWidthDip, "旧实验字段不再限制正式悬浮配置。");
            page.Deactivate();
            var saved = SettingsManager.Current.FloatingBar;
            configuration.SetLyricsFixedWidthDip(150);
            Assert.AreSame(saved, SettingsManager.Current.FloatingBar, "离开上下文后旧编辑结果不得写回。");
        }
        finally { SettingsManager.Current = previous; }
    }

    [TestMethod]
    public void TaskbarOrientationsUseOneConfiguration()
    {
        var previous = SettingsManager.Current;
        using var horizontal = new SettingsPageContext();
        using var vertical = new SettingsPageContext();
        vertical.Initialize(SettingsContext.Initial with { Orientation = LayoutOrientation.Vertical });
        try
        {
            SettingsManager.Current = new AppSettings();
            var first = new LegacySettingsConfiguration(horizontal);
            var second = new LegacySettingsConfiguration(vertical);
            first.SetLyricsFixedWidthDip(310);
            Assert.AreSame(first.Current, second.Current);
            Assert.AreEqual(310, second.Current.LyricsFixedWidthDip);
            second.SetLyricsFixedWidthDip(330);
            Assert.AreEqual(330, first.Current.LyricsFixedWidthDip);
        }
        finally { SettingsManager.Current = previous; }
    }

    [TestMethod]
    public void FloatingEditorDoesNotUseTaskbarRuntimeLengthBudget()
    {
        StaTest.Run(_ =>
        {
            var previous = SettingsManager.Current;
            using var page = new SettingsPageContext();
            page.Initialize(SettingsContext.Initial with { Mode = SettingsMode.FloatingBar });
            using var localization = new LocalizationService();
            try
            {
                SettingsManager.Current = new AppSettings { Experiments = new() { FloatingBar = true } };
                var configuration = new LegacySettingsConfiguration(page);
                configuration.SetTaskbarExperience(configuration.Current.TaskbarExperience with { LengthMode = TaskbarLengthMode.Fixed });
                var limits = new TaskbarLengthConstraintsService();
                limits.Update(300, 400);
                using var editor = new TaskbarAppearanceViewModel(configuration, limits, localization);
                Assert.IsFalse(editor.CanEditFixedTaskbarLength, "悬浮宿主未接入时不得套用任务栏预算。");
                var before = configuration.Current.TaskbarExperience.FixedLengthDip;
                editor.FixedTaskbarLengthDip = 350;
                Assert.AreEqual(before, configuration.Current.TaskbarExperience.FixedLengthDip);
            }
            finally { SettingsManager.Current = previous; }
            return Task.CompletedTask;
        });
    }
    [TestMethod]
    public void FloatingProfileAndExperimentsRoundTripThroughUserDefaultsFile()
    {
        var previous = SettingsManager.Current;
        var previousDefaults = SettingsManager.UserDefaults;
        var directory = Path.Combine(Path.GetTempPath(), "afmb-experimental-defaults-" + Guid.NewGuid().ToString("N"));
        try
        {
            SettingsManager.Current = new AppSettings
            {
                LyricsFixedWidthDip = 220,
                Experiments = new() { FloatingBar = true },
                FloatingBar = new()
                {
                    Profile = BarDisplaySettings.Capture(new AppSettings { LyricsFixedWidthDip = 310 }),
                    EdgeCollapseEnabled = false,
                    Positions = [new("display", 150, 200) { DockEdge = FloatingBarDockEdge.Top }]
                }
            };
            using var persistence = new SettingsPersistenceService(directory);
            Assert.IsNull(persistence.SaveCurrentAsUserDefaults());
            var restored = persistence.LoadUserDefaults()!;
            Assert.IsTrue(restored.Experiments.FloatingBar);
            Assert.IsFalse(restored.Experiments.VerticalTaskbar);
            Assert.AreEqual(220, restored.LyricsFixedWidthDip);
            Assert.AreEqual(310, restored.FloatingBar.Profile!.LyricsFixedWidthDip);
            Assert.AreEqual(150, restored.FloatingBar.Positions.Single().CenterXDip);
            Assert.IsFalse(restored.FloatingBar.EdgeCollapseEnabled);
            Assert.AreEqual(FloatingBarDockEdge.Top, restored.FloatingBar.Positions.Single().DockEdge);
        }
        finally
        {
            SettingsManager.Current = previous;
            SettingsManager.SetUserDefaults(previousDefaults);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
    [TestMethod]
    public void AudioEditorsIncludeInputAndResetUsesUserDefaults()
    {
        StaTest.Run(_ =>
        {
            var previous = SettingsManager.Current;
            var previousDefaults = SettingsManager.UserDefaults;
            using var context = new SettingsPageContext();
            using var localization = new LocalizationService();
            try
            {
                SettingsManager.SetUserDefaults(new AppSettings
                {
                    AudioDevices = new() { OutputCycleHotkey = "Ctrl+Alt+Y", InputCycleHotkey = "Ctrl+Alt+U" }
                });
                SettingsManager.Current = new AppSettings
                {
                    AudioDevices = new()
                    {
                        OutputCycleHotkey = "Ctrl+Alt+X",
                        InputCycleHotkey = "Ctrl+Alt+M",
                        InputBlacklist = [new("mic", "Microphone")]
                    },
                    Interaction = GlobalInteractionSettings.Default with { PrimaryWheelAction = WheelAction.InputDevice },
                    TaskbarExperience = TaskbarExperienceSettings.Default with
                    {
                        InputDeviceVisible = true,
                        RestComponentOrder = [TaskbarRestComponent.InputDevice, TaskbarRestComponent.Spectrum, TaskbarRestComponent.Performance,
                            TaskbarRestComponent.OutputDevice, TaskbarRestComponent.Volume]
                    }
                };
                using var editor = new TaskbarAppearanceViewModel(new LegacySettingsConfiguration(context), new TaskbarLengthConstraintsService(), localization);
                Assert.IsTrue(editor.RestOrderEntries.Any(entry => entry.Component == TaskbarRestComponent.InputDevice));
                var volume = editor.RestOrderEntries.Single(entry => entry.Component == TaskbarRestComponent.Volume);
                editor.MoveRestComponentUpCommand.Execute(volume);
                Assert.AreEqual(TaskbarRestComponent.InputDevice, SettingsManager.Current.TaskbarExperience.RestComponentOrder![0]);
                SettingsManager.ResetInteraction();
                Assert.AreEqual("Ctrl+Alt+Y", SettingsManager.Current.AudioDevices.OutputCycleHotkey);
                Assert.AreEqual("Ctrl+Alt+U", SettingsManager.Current.AudioDevices.InputCycleHotkey);
                Assert.AreEqual("mic", SettingsManager.Current.AudioDevices.InputBlacklist.Single().EndpointId);
                Assert.AreEqual(GlobalInteractionSettings.Default.PrimaryWheelAction, SettingsManager.Current.Interaction.PrimaryWheelAction);
                SettingsManager.ResetContentLayout();
                Assert.AreEqual(TaskbarExperienceSettings.Default.InputDeviceVisible, SettingsManager.Current.TaskbarExperience.InputDeviceVisible);
                Assert.IsNull(SettingsManager.Current.TaskbarExperience.RestComponentOrder);
            }
            finally
            {
                SettingsManager.Current = previous;
                SettingsManager.SetUserDefaults(previousDefaults);
            }
            return Task.CompletedTask;
        });
    }
    [TestMethod]
    public void RetiredAudioFlagIsIgnoredAndDevicePreferencesSurvive()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("""
            {"Experiments":{"NewAudioControl":false},"AudioDevices":{
              "OutputCycleHotkey":"Ctrl+Alt+X","InputCycleHotkey":"Ctrl+Alt+M",
              "InputBlacklist":[{"EndpointId":"mic","DisplayName":"Microphone"}],
              "Preferences":[{"Direction":0,"EndpointId":"speaker","LastKnownName":"Speaker","Alias":"Desk"}]}}
            """)!.Normalize().Clone();
        Assert.AreEqual("Ctrl+Alt+X", settings.AudioDevices.OutputCycleHotkey);
        Assert.AreEqual("Ctrl+Alt+M", settings.AudioDevices.InputCycleHotkey);
        Assert.IsFalse(settings.AudioDevices.IsAllowed(AFMediaBar.Classes.Models.AudioDeviceDirection.Input, "mic"));
        Assert.AreEqual("Desk", settings.AudioDevices.FindPreference(AFMediaBar.Classes.Models.AudioDeviceDirection.Output, "speaker")?.Alias);
        Assert.IsFalse(JsonSerializer.Serialize(settings).Contains("NewAudioControl"));
    }

    [TestMethod]
    public void OldSettingsKeepTaskbarValuesAndLeaveAllExperimentsOff()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("""{"LyricsFixedWidthDip":210,"WindowMode":1}""")!.Normalize();
        Assert.AreEqual(210, settings.LyricsFixedWidthDip);
        Assert.AreEqual(new ExperimentalSettings(), settings.Experiments);
        Assert.IsNull(settings.FloatingBar.Profile);
        Assert.AreEqual(WindowMode.Taskbar, settings.WindowMode);
        Assert.AreEqual(2, SettingsPersistenceService.CurrentSchemaVersion);
    }

    [TestMethod]
    public void FloatingEditsSharePresentationAndResetKeepsTaskbarGeometry()
    {
        var previous = SettingsManager.Current;
        var previousDefaults = SettingsManager.UserDefaults;
        using var page = new SettingsPageContext();
        page.Initialize(SettingsContext.Initial with { Mode = SettingsMode.FloatingBar });
        var configuration = new LegacySettingsConfiguration(page);
        try
        {
            SettingsManager.Current = new AppSettings { LyricsFixedWidthDip = 220, TwoLineLyricsEnabled = false };
            ((AFMediaBar.Classes.Abstractions.ISettingsConfiguration)configuration).SetWindowMode(WindowMode.FloatingBar);
            var captured = SettingsManager.Current.FloatingBar.Profile!;
            configuration.SetTwoLineLyricsEnabled(true);
            configuration.SetLyricsFixedWidthDip(330);
            configuration.SetTaskbarAppearance(configuration.Current.Appearance with { TaskbarBackgroundOpacityPercent = 63 });
            Assert.IsTrue(SettingsManager.Current.TwoLineLyricsEnabled);
            Assert.AreEqual(220, SettingsManager.Current.LyricsFixedWidthDip);
            Assert.AreEqual(330, configuration.Current.LyricsFixedWidthDip);
            SettingsManager.Current.Appearance = SettingsManager.Current.Appearance with { FontWeight = 700 };
            Assert.AreEqual(700, configuration.Current.Appearance.FontWeight);
            SettingsManager.Current.LyricsFixedWidthDip = 260;
            Assert.AreEqual(330, configuration.Current.LyricsFixedWidthDip);

            var defaults = SettingsManager.Current.Clone();
            defaults.TwoLineLyricsEnabled = false;
            SettingsManager.SetUserDefaults(defaults);
            configuration.Reset(SettingsResetScope.Lyrics);
            Assert.IsFalse(configuration.Current.TwoLineLyricsEnabled);
            Assert.AreEqual(330, configuration.Current.LyricsFixedWidthDip, "歌词页重置不应修改布局页宽度。");
            Assert.AreEqual(260, SettingsManager.Current.LyricsFixedWidthDip);
            Assert.AreEqual(700, SettingsManager.Current.Appearance.FontWeight);
            configuration.SetExperiments(new());
            configuration.SetExperiments(new() { FloatingBar = true });
            Assert.AreEqual(330, configuration.Current.LyricsFixedWidthDip);
            page.Deactivate();
            configuration.SetLyricsFixedWidthDip(150);
            Assert.AreEqual(330, configuration.Current.LyricsFixedWidthDip);

            var roundTrip = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(SettingsManager.Current))!.Normalize();
            Assert.AreEqual(330, roundTrip.FloatingBar.Profile!.LyricsFixedWidthDip);
            Assert.AreEqual(260, roundTrip.LyricsFixedWidthDip);
        }
        finally
        {
            SettingsManager.Current = previous;
            SettingsManager.SetUserDefaults(previousDefaults);
        }
    }
}
