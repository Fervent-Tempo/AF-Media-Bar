using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AFMediaBar.ViewModels.Pages;

namespace AFMediaBar.Layout.Tests;

/// <summary>设置持久化服务的纯文件和恢复测试。 / Pure file and recovery tests for settings persistence.</summary>
[TestClass]
[DoNotParallelize]
public sealed class SettingsPersistenceServiceTests
{
    private string _directory = string.Empty;

    [TestInitialize]
    public void SetUp() => _directory = Path.Combine(Path.GetTempPath(), "AFMediaBarTests", Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void TearDown()
    {
        SettingsManager.ResetAll();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        if (File.Exists(_directory)) File.Delete(_directory);
    }

    [TestMethod]
    public void RoundTripPersistsAllFieldsAndStringEnums()
    {
        var settings = new AppSettings
        {
            Appearance = AppearanceSettings.Default with { FontWeight = 700, BackdropMode = ApplicationBackdropMode.Acrylic },
            TrayWheelBehavior = TrayWheelBehavior.Disabled,
            LyricsEnabled = false,
            TwoLineLyricsEnabled = true,
            LyricsSecondaryLineMode = LyricsSecondaryLineMode.Translation,
            TaskbarBarEnabled = false,
            TaskbarBarSelectedMonitor = 2,
            Position = TaskbarBarPosition.End,
            TaskbarBarBackgroundBlur = true,
            TaskbarBarManualPadding = 14,
            WindowMode = WindowMode.DynamicIsland,
            LayoutOrientationMode = LayoutOrientationMode.Vertical,
            LayoutLengthScalePercent = 115,
            LayoutThicknessScalePercent = 85,
            DynamicIslandBackgroundMode = DynamicIslandBackgroundMode.Transparent,
            TaskbarBarCrossAxisOffsetDip = -10,
            TaskbarBarAvoidIcons = false,
            TaskbarBarPositionLocked = true,
            DynamicIslandLeft = 120,
            DynamicIslandTop = 240,
            DynamicIslandEdge = DynamicIslandEdge.Right,
            DynamicIslandEdgeDocked = false
        };
        using (var writer = new SettingsPersistenceService(_directory)) { writer.Initialize(); SettingsManager.Replace(settings); writer.Flush(); }
        SettingsManager.ResetAll();
        using var reader = new SettingsPersistenceService(_directory);
        reader.Initialize();

        Assert.AreEqual(TrayWheelBehavior.Disabled, SettingsManager.Current.TrayWheelBehavior);
        Assert.AreEqual(LyricsSecondaryLineMode.Translation, SettingsManager.Current.LyricsSecondaryLineMode);
        Assert.AreEqual(WindowMode.DynamicIsland, SettingsManager.Current.WindowMode);
        Assert.AreEqual(DynamicIslandEdge.Right, SettingsManager.Current.DynamicIslandEdge);
        Assert.AreEqual(700, SettingsManager.Current.Appearance.FontWeight);
        Assert.AreEqual(120, SettingsManager.Current.DynamicIslandLeft);
        StringAssert.Contains(File.ReadAllText(reader.SettingsPath), "\"Disabled\"");
    }

    [TestMethod]
    public void MissingFieldsUseDefaultsAndInvalidValuesNormalize()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "settings.json"), "{\"schemaVersion\":1,\"settings\":{\"layoutLengthScalePercent\":999,\"layoutThicknessScalePercent\":null,\"taskbarBarCrossAxisOffsetDip\":-999,\"dynamicIslandLeft\":-1,\"windowMode\":\"bad\",\"trayWheelBehavior\":\"bad\"}}");
        using var service = new SettingsPersistenceService(_directory);
        service.Initialize();

        Assert.AreEqual(125, SettingsManager.Current.LayoutLengthScalePercent);
        Assert.AreEqual(100, SettingsManager.Current.LayoutThicknessScalePercent);
        Assert.AreEqual(-20, SettingsManager.Current.TaskbarBarCrossAxisOffsetDip);
        Assert.IsNull(SettingsManager.Current.DynamicIslandLeft);
        Assert.AreEqual(WindowMode.Taskbar, SettingsManager.Current.WindowMode);
        Assert.AreEqual(TrayWheelBehavior.SwitchOutputDevice, SettingsManager.Current.TrayWheelBehavior);
        Assert.IsTrue(SettingsManager.Current.LyricsEnabled);
    }

    [TestMethod]
    public void CorruptMainRecoversBackupAndQuarantinesMain()
    {
        Directory.CreateDirectory(_directory);
        var main = Path.Combine(_directory, "settings.json");
        File.WriteAllText(main, "not-json");
        File.WriteAllText(main + ".bak", "{\"schemaVersion\":1,\"settings\":{\"lyricsEnabled\":false}}");
        using var service = new SettingsPersistenceService(_directory);
        service.Initialize();

        Assert.IsFalse(SettingsManager.Current.LyricsEnabled);
        Assert.IsTrue(Directory.GetFiles(_directory, "settings.json.invalid-*").Length == 1);
        Assert.IsTrue(File.Exists(main));
    }

    [TestMethod]
    public void UnsupportedSchemaIsQuarantinedAndDefaultsAreWritten()
    {
        Directory.CreateDirectory(_directory);
        var main = Path.Combine(_directory, "settings.json");
        File.WriteAllText(main, "{\"schemaVersion\":99,\"settings\":{\"lyricsEnabled\":false}}");
        using var service = new SettingsPersistenceService(_directory);
        service.Initialize();

        Assert.IsTrue(SettingsManager.Current.LyricsEnabled);
        Assert.IsTrue(Directory.GetFiles(_directory, "settings.json.unsupported-*").Length == 1);
        StringAssert.Contains(File.ReadAllText(main), "\"schemaVersion\": 1");
    }

    [TestMethod]
    public void ResetScopesOnlyChangeTheirOwnedFields()
    {
        SettingsManager.Replace(new AppSettings
        {
            LyricsEnabled = false,
            Appearance = AppearanceSettings.Default with { FontWeight = 700 },
            WindowMode = WindowMode.DynamicIsland,
            DynamicIslandLeft = 50
        });
        SettingsManager.ResetGeneral();
        Assert.IsTrue(SettingsManager.Current.LyricsEnabled);
        Assert.AreEqual(700, SettingsManager.Current.Appearance.FontWeight);
        Assert.AreEqual(WindowMode.DynamicIsland, SettingsManager.Current.WindowMode);
        SettingsManager.ResetLayout();
        Assert.AreEqual(WindowMode.Taskbar, SettingsManager.Current.WindowMode);
        Assert.AreEqual(700, SettingsManager.Current.Appearance.FontWeight);
        SettingsManager.ResetAppearance();
        Assert.AreEqual(AppearanceSettings.Default, SettingsManager.Current.Appearance);
    }

    [TestMethod]
    public void DebounceAndFlushPersistLatestValue()
    {
        using var service = new SettingsPersistenceService(_directory, TimeSpan.FromMilliseconds(50));
        service.Initialize();
        SettingsManager.Current.LyricsEnabled = false;
        SettingsManager.Current.LyricsEnabled = true;
        Thread.Sleep(150);
        var text = File.ReadAllText(service.SettingsPath);
        StringAssert.Contains(text, "\"lyricsEnabled\": true");
    }

    [TestMethod]
    public void SaveFailureKeepsInMemorySettings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_directory)!);
        File.WriteAllText(_directory, "blocks-directory-creation");
        using var service = new SettingsPersistenceService(_directory);
        service.Initialize();
        SettingsManager.Current.LyricsEnabled = false;
        service.Flush();
        Assert.IsFalse(SettingsManager.Current.LyricsEnabled);
    }

    [TestMethod]
    public void ResetEventsRefreshSingletonViewModelState()
    {
        SettingsManager.Replace(new AppSettings { WindowMode = WindowMode.DynamicIsland, LayoutLengthScalePercent = 125 });
        var viewModel = new LayoutViewModel();
        var layoutEvents = 0;
        EventHandler<LayoutSettingsChangedEventArgs> handler = (_, _) => layoutEvents++;
        SettingsManager.LayoutSettingsChanged += handler;
        try
        {
            SettingsManager.ResetLayout();
            Assert.AreEqual(WindowMode.Taskbar, viewModel.CurrentWindowMode);
            Assert.AreEqual(100, viewModel.LayoutLengthScalePercent);
            Assert.AreEqual(1, layoutEvents);
        }
        finally { SettingsManager.LayoutSettingsChanged -= handler; }
    }
}
