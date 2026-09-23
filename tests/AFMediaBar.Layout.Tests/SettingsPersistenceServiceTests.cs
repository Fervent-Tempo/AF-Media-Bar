using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.ViewModels.Pages;
using System.Windows;

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
            Appearance = AppearanceSettings.Default with
            {
                LatinFont = "Comic Sans MS",
                CjkFont = "Microsoft YaHei UI",
                FontWeight = 700,
                BackdropMode = ApplicationBackdropMode.Acrylic
            },
            TrayWheelBehavior = TrayWheelBehavior.Disabled,
            LyricsEnabled = false,
            TwoLineLyricsEnabled = true,
            LyricsSecondaryLine = new LyricsSecondaryLineSettings(
            [
                LyricsSecondaryLineMode.Romanization,
                LyricsSecondaryLineMode.Translation
            ]),
            TaskbarBarEnabled = false,
            TaskbarTargetMonitorDeviceIds = [@"\\.\DISPLAY1", @"\\.\DISPLAY2"],
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
            DynamicIslandEdgeDocked = false,
            TaskbarExperience = new TaskbarExperienceSettings(
                false,
                true,
                TaskbarInformationDensity.Information,
                TaskbarContentLayout.CenteredStack,
                new TaskbarFullPanelSettings(true, false, true, false),
                TaskbarLengthMode.Fixed,
                444)
            {
                MediaTextAlignment = TaskbarMediaTextAlignment.Right,
                SpectrumVisible = false,
                PerformanceVisible = false,
                HoverControls = new TaskbarHoverControlsSettings(true, true, false, false, false),
                // 静置层组件顺序与"无媒体时保留"是列表字段：列表在记录生成的 ToString 里只打印类型名，
                // 因此只有真正写进文件再读回来才能证明它们被序列化了。
                // The rest-layer component order and the "kept without media" list are list fields, and a record's generated ToString prints
                // only their type name, so only a real write-then-read proves they are serialized at all.
                OutputDeviceVisible = true,
                VolumeVisible = true,
                RestComponentOrder =
                [
                    TaskbarRestComponent.Volume,
                    TaskbarRestComponent.Spectrum
                ],
                IdleComponents = [TaskbarRestComponent.Performance, TaskbarRestComponent.Volume]
            },
            Interaction = new GlobalInteractionSettings(
                PlayerClickAction.ActivateSource,
                PlayerClickAction.TogglePlayPause,
                WheelAction.SwitchMediaSource,
                InteractionModifier.RightMouseButton,
                WheelAction.PreviousNext,
                TrayClickAction.OpenSettings,
                TrayWheelBehavior.AdjustVolume,
                TrayWheelBehavior.SwitchOutputDevice),
            TaskbarSurface = new ModeSurfaceSettings(PlayerSurfaceStyle.ThemeTint, 72, 12),
            LyricsTextAlignment = LyricsTextAlignment.Right,
            TrackChangeNotification = new TrackChangeNotificationSettings(
                true,
                true,
                7000,
                TrackChangeNotificationPosition.TopRight,
                NotificationTargetMode.ForegroundWindow,
                @"\\.\DISPLAY3"),
            SmtcSourceFilter = new SmtcSourceFilterSettings(true, ["PLAYER.ONE", "player.two"]),
            QuickLaunch = new QuickLaunchSettings([
                new QuickLaunchEntry("player", "Player", QuickLaunchTargetKind.Executable, @"C:\Apps\Player.exe", "Player.One")]),
            SpectrumComponent = new SpectrumComponentSettings(7, 25, 180),
            PerformanceComponent = new PerformanceComponentSettings(
                [MetricKind.SystemCpu, MetricKind.ProcessMemory], 1800, true),
            Update = new UpdateSettings(
                AutoCheckEnabled: false,
                SkippedVersion: "1.2.0",
                LastCheckUtc: new DateTimeOffset(2026, 9, 16, 8, 30, 0, TimeSpan.Zero),
                LastCheckSucceeded: false),
            // 刻意取一个非默认的选项：默认值（跟随系统）即使序列化失败也会"看起来正确"。
            // A deliberately non-default option: the default (follow the system) would look correct even if serialization failed.
            InterfaceLanguage = InterfaceLanguage.TraditionalChinese
        };
        using (var writer = new SettingsPersistenceService(_directory)) { writer.Initialize(); SettingsManager.Replace(settings); writer.Flush(); }
        SettingsManager.ResetAll();
        using var reader = new SettingsPersistenceService(_directory);
        reader.Initialize();

        Assert.AreEqual(TrayWheelBehavior.Disabled, SettingsManager.Current.TrayWheelBehavior);
        CollectionAssert.AreEqual(
            new[] { LyricsSecondaryLineMode.Romanization, LyricsSecondaryLineMode.Translation },
            SettingsManager.Current.LyricsSecondaryLine.Order!.ToArray());
        Assert.AreEqual(WindowMode.Taskbar, SettingsManager.Current.WindowMode);
        Assert.AreEqual(DynamicIslandEdge.Right, SettingsManager.Current.DynamicIslandEdge);
        Assert.AreEqual(700, SettingsManager.Current.Appearance.FontWeight);
        Assert.AreEqual("Comic Sans MS", SettingsManager.Current.Appearance.LatinFont);
        Assert.AreEqual("Microsoft YaHei UI", SettingsManager.Current.Appearance.CjkFont);
        Assert.AreEqual(120, SettingsManager.Current.DynamicIslandLeft);
        Assert.AreEqual(PlayerClickAction.ActivateSource, SettingsManager.Current.Interaction.ArtworkClickAction);
        Assert.AreEqual(WheelAction.SwitchMediaSource, SettingsManager.Current.Interaction.PrimaryWheelAction);
        Assert.AreEqual(InteractionModifier.RightMouseButton, SettingsManager.Current.Interaction.Modifier);
        Assert.AreEqual(TaskbarInformationDensity.Information, SettingsManager.Current.TaskbarExperience.Density);
        Assert.AreEqual(new TaskbarFullPanelSettings(true, false, true, false), SettingsManager.Current.TaskbarExperience.FullPanel);
        Assert.AreEqual(TaskbarLengthMode.Fixed, SettingsManager.Current.TaskbarExperience.LengthMode);
        Assert.AreEqual(444, SettingsManager.Current.TaskbarExperience.FixedLengthDip);
        Assert.AreEqual(TaskbarMediaTextAlignment.Right, SettingsManager.Current.TaskbarExperience.MediaTextAlignment);
        Assert.IsFalse(SettingsManager.Current.TaskbarExperience.SpectrumVisible);
        Assert.IsFalse(SettingsManager.Current.TaskbarExperience.PerformanceVisible);
        Assert.IsTrue(SettingsManager.Current.TaskbarExperience.OutputDeviceVisible);
        Assert.IsTrue(SettingsManager.Current.TaskbarExperience.VolumeVisible);
        CollectionAssert.AreEqual(
            new[]
            {
                TaskbarRestComponent.Volume,
                TaskbarRestComponent.Spectrum
            },
            SettingsManager.Current.TaskbarExperience.RestComponentOrder!.ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                TaskbarRestComponent.Performance,
                TaskbarRestComponent.Volume
            },
            SettingsManager.Current.TaskbarExperience.IdleComponents!.ToArray());
        Assert.AreEqual(72, SettingsManager.Current.TaskbarSurface.BackgroundOpacityPercent);
        Assert.AreEqual(LyricsTextAlignment.Right, SettingsManager.Current.LyricsTextAlignment);
        CollectionAssert.AreEqual(
            new[] { @"\\.\DISPLAY1", @"\\.\DISPLAY2" },
            SettingsManager.Current.TaskbarTargetMonitorDeviceIds!.ToArray());
        Assert.IsNull(SettingsManager.Current.TaskbarTargetMonitorDeviceId);
        Assert.AreEqual(7000, SettingsManager.Current.TrackChangeNotification.DurationMilliseconds);
        Assert.AreEqual(TrackChangeNotificationPosition.TopRight, SettingsManager.Current.TrackChangeNotification.Position);
        Assert.AreEqual(NotificationTargetMode.ForegroundWindow, SettingsManager.Current.TrackChangeNotification.TargetMode);
        Assert.AreEqual(@"\\.\DISPLAY3", SettingsManager.Current.TrackChangeNotification.FixedMonitorDeviceId);
        Assert.IsTrue(SettingsManager.Current.SmtcSourceFilter.Enabled);
        CollectionAssert.AreEquivalent(
            new[] { "PLAYER.ONE", "player.two" },
            SettingsManager.Current.SmtcSourceFilter.AllowedSourceIds!.ToArray());
        Assert.AreEqual("Player", SettingsManager.Current.QuickLaunch.Entries!.Single().DisplayName);
        // 柱数 7 低于新的下限 9，写入时被夹取；采样间隔 1800 ms 不在 0.5 秒网格上，被吸附到 2000 ms。
        // A bar count of seven is below the new minimum of nine and is clamped on write, and a 1800 ms interval does not sit
        // on the 0.5-second grid, so it snaps to 2000 ms.
        Assert.AreEqual(new SpectrumComponentSettings(9, 25, 180), SettingsManager.Current.SpectrumComponent);
        CollectionAssert.AreEqual(
            new[] { MetricKind.SystemCpu, MetricKind.ProcessMemory },
            SettingsManager.Current.PerformanceComponent.Metrics!.ToArray());
        Assert.AreEqual(2000, SettingsManager.Current.PerformanceComponent.RefreshIntervalMilliseconds);
        Assert.IsTrue(SettingsManager.Current.PerformanceComponent.OpenTaskManagerOnClick);
        Assert.IsFalse(SettingsManager.Current.Update.AutoCheckEnabled);
        Assert.AreEqual("1.2.0", SettingsManager.Current.Update.SkippedVersion);
        Assert.AreEqual(
            new DateTimeOffset(2026, 9, 16, 8, 30, 0, TimeSpan.Zero),
            SettingsManager.Current.Update.LastCheckUtc);
        Assert.IsFalse(SettingsManager.Current.Update.LastCheckSucceeded);
        Assert.AreEqual(InterfaceLanguage.TraditionalChinese, SettingsManager.Current.InterfaceLanguage);
        var persisted = File.ReadAllText(reader.SettingsPath);
        // 断言取当前 schema 常量而不是写死的数字：版本号每升一级都要改七处断言，而这里要证明的是
        // "文件被按当前 schema 重写过"，不是某一个具体数字。
        // The assertion reads the current schema constant instead of a hardcoded number: a version bump would otherwise mean
        // editing seven assertions, while what this proves is "the file was rewritten at the current schema", not one number.
        StringAssert.Contains(persisted, $"\"schemaVersion\": {SettingsPersistenceService.CurrentSchemaVersion}");
        StringAssert.Contains(persisted, "\"Disabled\"");
        StringAssert.Contains(persisted, "\"TraditionalChinese\"");
    }

    [TestMethod]
    public void MissingFieldsUseDefaultsAndInvalidValuesNormalize()
    {
        // 缺字段取声明处的默认值，写坏的值由 Normalize() 夹回合法区间；显式写出的 null 不参与反序列化
        // （这五个字段在模型里是非空数值，JSON null 会让反序列化直接失败）。
        // A missing field takes the declared default and a corrupt one is clamped back into range by Normalize(); an explicit JSON null
        // is dropped before deserialization, because those five fields are non-nullable numbers in the model.
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            $"{{\"schemaVersion\":{SettingsPersistenceService.CurrentSchemaVersion},\"settings\":{{\"layoutLengthScalePercent\":999,\"layoutThicknessScalePercent\":null,\"taskbarBarCrossAxisOffsetDip\":-999,\"dynamicIslandLeft\":-1,\"windowMode\":\"bad\",\"trayWheelBehavior\":\"bad\"}}}}");
        using var service = new SettingsPersistenceService(_directory);
        service.Initialize();

        Assert.AreEqual(125, SettingsManager.Current.LayoutLengthScalePercent);
        Assert.AreEqual(100, SettingsManager.Current.LayoutThicknessScalePercent);
        Assert.AreEqual(-20, SettingsManager.Current.TaskbarBarCrossAxisOffsetDip);
        Assert.IsNull(SettingsManager.Current.DynamicIslandLeft);
        Assert.AreEqual(WindowMode.Taskbar, SettingsManager.Current.WindowMode);
        Assert.AreEqual(TrayWheelBehavior.SwitchOutputDevice, SettingsManager.Current.TrayWheelBehavior);
        Assert.IsTrue(SettingsManager.Current.LyricsEnabled);
        Assert.AreEqual(GlobalInteractionSettings.Default, SettingsManager.Current.Interaction);
    }

    [TestMethod]
    public void LegacySingleTaskbarTargetNormalizesToExplicitSelection()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            $"{{\"schemaVersion\":{SettingsPersistenceService.CurrentSchemaVersion},\"settings\":{{\"taskbarTargetMonitorDeviceId\":\"  DISPLAY2  \"}}}}");
        using var service = new SettingsPersistenceService(_directory);
        service.Initialize();

        CollectionAssert.AreEqual(
            new[] { "DISPLAY2" },
            SettingsManager.Current.TaskbarTargetMonitorDeviceIds!.ToArray());
        Assert.IsNull(SettingsManager.Current.TaskbarTargetMonitorDeviceId);
    }

    [TestMethod]
    public void AppearanceAccentAndMaterialFieldsNormalizeWithoutRelyingOnMissingFields()
    {
        // 外观这三项是纯新增字段：文件里没有它们，因此必须是文档化的默认值（跟随系统、默认色、浓度 60%），
        // 而写坏的值必须在 `Normalize()` 里被夹回合法区间，不靠"缺字段恰好等于 0"这种巧合——浓度尤其危险：
        // 缺字段给的是 null，若按 0 处理会被夹到下限 30%，用户会看到与文档默认值不同的窗口。
        // These three appearance fields are purely additive: a file that lacks them must produce the documented defaults (follow the
        // system, the default colour, 60% concentration), and corrupt values must be clamped back into range rather than resting on the
        // coincidence that a missing field equals zero. The concentration is the dangerous one: a missing field hands over null, and
        // treating that as 0 would clamp to the 30% floor, so the window would not look like the documented default.
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            $"{{\"schemaVersion\":{SettingsPersistenceService.CurrentSchemaVersion},\"settings\":{{\"appearance\":{{\"latinFont\":\"SegoeUi\",\"cjkFont\":\"SystemDefault\",\"fontWeight\":700,\"playerForegroundMode\":\"Automatic\",\"enhancedReadability\":false,\"applicationThemeMode\":\"Dark\",\"backdropMode\":\"Acrylic\"}}}}}}");
        using var service = new SettingsPersistenceService(_directory);
        service.Initialize();

        var loaded = SettingsManager.Current.Appearance.Normalize();
        Assert.AreEqual(ApplicationBackdropMode.Acrylic, loaded.BackdropMode);
        Assert.AreEqual(700, loaded.FontWeight);
        Assert.AreEqual("Segoe UI", loaded.LatinFont);
        Assert.AreEqual(string.Empty, loaded.CjkFont);
        Assert.AreEqual(AccentColorMode.System, loaded.AccentColorMode);
        Assert.AreEqual(AppearanceSettings.DefaultAccentColorHex, loaded.AccentColor);
        Assert.AreEqual(
            AppearanceSettings.DefaultBackdropTintOpacityPercent,
            loaded.ResolveBackdropTintOpacityPercent());

        var corrupt = (AppearanceSettings.Default with
        {
            AccentColor = "not-a-colour",
            BackdropTintOpacityPercent = 900
        }).Normalize();
        Assert.AreEqual(AppearanceSettings.DefaultAccentColorHex, corrupt.AccentColor);
        Assert.AreEqual(
            AppearanceSettings.MaximumBackdropTintOpacityPercent,
            corrupt.ResolveBackdropTintOpacityPercent());

        // 自选色按规范写法回写：大小写与是否带 # 都会被统一，避免同一颜色在文件里出现两种写法。
        // A custom colour is written back in its canonical form: case and the leading # are unified, so one colour never appears
        // in two spellings in the file.
        var custom = (AppearanceSettings.Default with
        {
            AccentColorMode = AccentColorMode.Custom,
            AccentColor = "7c3aed",
            BackdropTintOpacityPercent = 1
        }).Normalize();
        Assert.AreEqual("#7C3AED", custom.AccentColor);
        Assert.AreEqual(
            AppearanceSettings.MinimumBackdropTintOpacityPercent,
            custom.ResolveBackdropTintOpacityPercent());
    }

    [TestMethod]
    public void NumericLatinFontZeroKeepsTheHistoricalSegoeUiMeaning()
    {
        // 旧版本允许数字枚举输入，0 当时表示 SegoeUi；在枚举开头插入新成员会把已有配置静默改成另一种字体。
        // Older versions accepted numeric enum input where 0 meant SegoeUi; inserting a new member at the front would silently
        // reinterpret an existing configuration as a different font.
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            $"{{\"schemaVersion\":{SettingsPersistenceService.CurrentSchemaVersion},\"settings\":{{\"appearance\":{{\"latinFont\":0}}}}}}");
        using var service = new SettingsPersistenceService(_directory);
        service.Initialize();

        Assert.AreEqual(LatinFontPreset.SegoeUi, SettingsManager.Current.Appearance.LatinFont);
    }

    [TestMethod]
    public void CorruptMainRecoversBackupAtTheCurrentSchemaAndQuarantinesMain()
    {
        Directory.CreateDirectory(_directory);
        var main = Path.Combine(_directory, "settings.json");
        File.WriteAllText(main, "not-json");
        File.WriteAllText(
            main + ".bak",
            $"{{\"schemaVersion\":{SettingsPersistenceService.CurrentSchemaVersion},\"settings\":{{\"lyricsEnabled\":false}}}}");
        using var service = new SettingsPersistenceService(_directory);
        service.Initialize();

        Assert.IsFalse(SettingsManager.Current.LyricsEnabled, "损坏的主文件必须回退到当前编号的备份。");
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
        StringAssert.Contains(File.ReadAllText(main), $"\"schemaVersion\": {SettingsPersistenceService.CurrentSchemaVersion}");
    }

    /// <summary>
    /// 编号不同的设置文件整份不读取：文件改名留档（不删除），内存里是内置默认值，磁盘上随即写回当前编号的新文件。
    /// 取 14 作为样本是刻意的——1.1.1 写出的就是 14，它比当前编号"大"，因此这条用例同时证明了拦截不是靠"编号太小"。
    /// A settings file with a different number is not read at all: the file is renamed and kept (never deleted), memory holds the
    /// built-in defaults, and a fresh file at the current number is written straight away. The sample number 14 is deliberate —
    /// 1.1.1 wrote 14, which is larger than the current number, so this also proves the rejection is not "the number was too small".
    /// </summary>
    [TestMethod]
    public void PreviousSchemaIsQuarantinedAndNeverRead()
    {
        Directory.CreateDirectory(_directory);
        var main = Path.Combine(_directory, "settings.json");
        File.WriteAllText(
            main,
            "{\"schemaVersion\":14,\"settings\":{\"lyricsEnabled\":false,\"interfaceLanguage\":\"TraditionalChinese\",\"windowMode\":\"DynamicIsland\",\"launchAtStartup\":false}}");

        using var service = new SettingsPersistenceService(_directory);
        service.Initialize();

        Assert.IsTrue(SettingsManager.Current.LyricsEnabled, "旧编号文件里的取值不得参与读取。");
        Assert.AreEqual(InterfaceLanguage.System, SettingsManager.Current.InterfaceLanguage);
        Assert.AreEqual(WindowMode.Taskbar, SettingsManager.Current.WindowMode);
        Assert.IsTrue(SettingsManager.Current.LaunchAtStartup);

        var quarantined = Directory.GetFiles(_directory, "settings.json.unsupported-*");
        Assert.AreEqual(1, quarantined.Length, "旧文件必须改名留档，而不是被删除。");
        StringAssert.Contains(
            File.ReadAllText(quarantined[0]),
            "\"schemaVersion\":14",
            "留档文件仍应是用户原来那份内容，可以手工找回。");
        StringAssert.Contains(File.ReadAllText(main), $"\"schemaVersion\": {SettingsPersistenceService.CurrentSchemaVersion}");
    }

    [TestMethod]
    public void StaleBackupInAnOlderSchemaIsNotUsed()
    {
        Directory.CreateDirectory(_directory);
        var main = Path.Combine(_directory, "settings.json");
        File.WriteAllText(main, "not-json");
        File.WriteAllText(main + ".bak", "{\"schemaVersion\":14,\"settings\":{\"lyricsEnabled\":false}}");
        using var service = new SettingsPersistenceService(_directory);
        service.Initialize();

        Assert.IsTrue(
            SettingsManager.Current.LyricsEnabled,
            "编号不同的备份不得顶上主文件：宁可回到默认值，也不要读一份读不准的旧设置。");
        Assert.AreEqual(1, Directory.GetFiles(_directory, "settings.json.invalid-*").Length);
    }

    /// <summary>
    /// 已经是当前编号的文件在加载后**不被重写**：每次启动都重写用户文件，除了一次多余的磁盘写入，还会把文件里
    /// 那些无关的空白与顺序抹掉，让"用户自己改过什么"再也看不出来。
    /// A file that already carries the current number is **not rewritten** on load: rewriting the user's file on every start costs a
    /// needless write and erases the incidental spacing and ordering that show what the user changed by hand.
    /// </summary>
    [TestMethod]
    public void CurrentSchemaFileIsLeftUntouchedOnLoad()
    {
        Directory.CreateDirectory(_directory);
        var main = Path.Combine(_directory, "settings.json");
        var original = $"{{\"schemaVersion\":{SettingsPersistenceService.CurrentSchemaVersion},   \"settings\":{{\"lyricsEnabled\":false}}}}";
        File.WriteAllText(main, original);

        using var service = new SettingsPersistenceService(_directory);
        service.Initialize();

        Assert.IsFalse(SettingsManager.Current.LyricsEnabled, "当前编号的文件必须被读取。");
        Assert.AreEqual(original, File.ReadAllText(main), "当前编号的文件不得在加载时被重写。");
    }

    [TestMethod]
    public void FullPanelPresetsAndInvalidAllHiddenValueNormalizePredictably()
    {
        Assert.AreEqual(new TaskbarFullPanelSettings(true, true, false, false), TaskbarFullPanelSettings.Compact);
        Assert.AreEqual(new TaskbarFullPanelSettings(true, true, true, true), TaskbarFullPanelSettings.Full);
        Assert.AreEqual(
            TaskbarFullPanelSettings.Compact,
            new TaskbarFullPanelSettings(false, false, false, false).Normalize());
    }

    [TestMethod]
    public void InvalidNotificationValuesNormalizeToSafeDefaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            $"{{\"schemaVersion\":{SettingsPersistenceService.CurrentSchemaVersion},\"settings\":{{\"trackChangeNotification\":{{\"enabled\":true,\"showWhenFullscreen\":false,\"durationMilliseconds\":60000,\"position\":\"bad\",\"targetMode\":99,\"fixedMonitorDeviceId\":\"  DISPLAY2  \"}}}}}}");

        using var service = new SettingsPersistenceService(_directory);
        service.Initialize();

        Assert.IsTrue(SettingsManager.Current.TrackChangeNotification.Enabled);
        Assert.AreEqual(10000, SettingsManager.Current.TrackChangeNotification.DurationMilliseconds);
        Assert.AreEqual(TrackChangeNotificationPosition.BottomLeft, SettingsManager.Current.TrackChangeNotification.Position);
        Assert.AreEqual(NotificationTargetMode.Fixed, SettingsManager.Current.TrackChangeNotification.TargetMode);
        Assert.AreEqual("DISPLAY2", SettingsManager.Current.TrackChangeNotification.FixedMonitorDeviceId);
    }

    [TestMethod]
    public void PartialSectionsUseDocumentedDefaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            $"{{\"schemaVersion\":{SettingsPersistenceService.CurrentSchemaVersion},\"settings\":{{\"trackChangeNotification\":{{\"enabled\":true}}}}}}");

        using var service = new SettingsPersistenceService(_directory);
        service.Initialize();

        Assert.IsTrue(SettingsManager.Current.TrackChangeNotification.Enabled);
        Assert.IsFalse(SettingsManager.Current.TrackChangeNotification.ShowWhenFullscreen);
        Assert.AreEqual(1000, SettingsManager.Current.TrackChangeNotification.DurationMilliseconds);
        Assert.AreEqual(TrackChangeNotificationPosition.BottomLeft, SettingsManager.Current.TrackChangeNotification.Position);
        Assert.AreEqual(NotificationTargetMode.Fixed, SettingsManager.Current.TrackChangeNotification.TargetMode);
        Assert.AreEqual(TaskbarLengthMode.FollowContent, SettingsManager.Current.TaskbarExperience.LengthMode);
        Assert.AreEqual(TaskbarExperienceSettings.Default.FixedLengthDip, SettingsManager.Current.TaskbarExperience.FixedLengthDip);
    }

    [TestMethod]
    public void MissingSectionsTakeTheirDeclaredDefaultsInsteadOfBeingInferredAsOff()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            $"{{\"schemaVersion\":{SettingsPersistenceService.CurrentSchemaVersion},\"settings\":{{\"taskbarExperience\":{{\"density\":\"Information\"}}}}}}");

        using var service = new SettingsPersistenceService(_directory);
        service.Initialize();

        Assert.AreEqual(TaskbarInformationDensity.Information, SettingsManager.Current.TaskbarExperience.Density);
        Assert.IsTrue(
            SettingsManager.Current.Update.AutoCheckEnabled,
            "文件里没有更新设置时必须取默认值（自动检查开启），而不是被推断成关闭。");
        Assert.AreEqual(UpdateSettings.Default, SettingsManager.Current.Update);
        Assert.AreEqual(SmtcSourceFilterSettings.Default, SettingsManager.Current.SmtcSourceFilter);
        Assert.AreEqual(SpectrumComponentSettings.Default, SettingsManager.Current.SpectrumComponent);
        CollectionAssert.AreEqual(
            PerformanceComponentSettings.Default.Metrics!.ToArray(),
            SettingsManager.Current.PerformanceComponent.Metrics!.ToArray());
        Assert.AreEqual(2500, SettingsManager.Current.PerformanceComponent.RefreshIntervalMilliseconds);
    }

    /// <summary>
    /// 偏离网格的取值由各自的 Normalize 吸附与夹取，与文件编号无关；未定义的枚举值回落到柱状图。
    /// Off-grid values are snapped and clamped by their own Normalize regardless of the file's number, and an undefined enum value
    /// falls back to bars.
    /// </summary>
    [TestMethod]
    public void OffGridSpectrumAndFontWeightValuesAreSnappedAndClamped()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "settings.json"),
            $"{{\"schemaVersion\":{SettingsPersistenceService.CurrentSchemaVersion},\"settings\":{{\"appearance\":{{\"fontWeight\":350}},\"spectrumComponent\":{{\"bandCount\":3,\"refreshRateHz\":25,\"sensitivityPercent\":7}},\"performanceComponent\":{{\"metrics\":[\"SystemCpu\"],\"refreshIntervalMilliseconds\":250}}}}}}");

        using var service = new SettingsPersistenceService(_directory);
        service.Initialize();

        var spectrum = SettingsManager.Current.SpectrumComponent;
        Assert.AreEqual(SpectrumComponentSettings.MinimumBandCount, spectrum.BandCount, "柱数 3 必须抬到下限 9。");
        Assert.AreEqual(25, spectrum.RefreshRateHz);
        Assert.AreEqual(10, spectrum.SensitivityPercent, "灵敏度 7 必须抬到步进下限 10。");
        Assert.AreEqual(500, SettingsManager.Current.PerformanceComponent.RefreshIntervalMilliseconds, "250 毫秒必须吸附到 0.5 秒网格。");
        Assert.AreEqual(400, SettingsManager.Current.Appearance.FontWeight, "350 必须吸附到最近的真实字重 400。");
        Assert.AreEqual(
            SpectrumStyle.Bars,
            (new SpectrumComponentSettings(12, 20, 100) { Style = (SpectrumStyle)99 }).Normalize().Style,
            "未定义的样式值回落到柱状图。");
    }

    /// <summary>
    /// 「我的默认设置」快照保存、读回与清除；编号不同的快照与设置文件一样不被读取，且改名留档而不是删除。
    /// The user-defaults snapshot saves, reads back, and clears; a snapshot with a different number is not read, exactly like the
    /// settings file, and it is renamed rather than deleted.
    /// </summary>
    [TestMethod]
    public void UserDefaultsSnapshotRoundTripsAndAnOlderSchemaIsIgnored()
    {
        Directory.CreateDirectory(_directory);
        using var service = new SettingsPersistenceService(_directory);
        service.Initialize();

        SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with { SpectrumVisible = false };
        Assert.IsNull(service.SaveCurrentAsUserDefaults());
        Assert.IsNotNull(SettingsManager.UserDefaults);
        var snapshot = service.LoadUserDefaults();
        Assert.IsNotNull(snapshot);
        Assert.IsFalse(snapshot!.TaskbarExperience.SpectrumVisible);

        // 重置现在回到用户快照而不是内置默认值。
        // A reset now lands on the user snapshot rather than the built-in defaults.
        SettingsManager.ResetDisplayModes();
        Assert.IsFalse(
            SettingsManager.Current.TaskbarExperience.SpectrumVisible,
            "重置必须回到用户保存的默认设置。");

        // 旧编号的快照：不读取、改名留档，页面据此显示"未保存"。
        var snapshotPath = Path.Combine(_directory, "user-defaults.json");
        File.WriteAllText(snapshotPath, "{\"schemaVersion\":14,\"settings\":{\"lyricsEnabled\":false}}");
        Assert.IsNull(service.LoadUserDefaults(), "旧编号的快照不得被读成当前默认值。");
        Assert.AreEqual(1, Directory.GetFiles(_directory, "user-defaults.json.unsupported-*").Length);

        Assert.IsNull(service.ClearUserDefaults());
        Assert.IsNull(SettingsManager.UserDefaults);
        SettingsManager.ResetDisplayModes();
        Assert.IsTrue(
            SettingsManager.Current.TaskbarExperience.SpectrumVisible,
            "清除快照之后重置必须回到程序内置默认值。");
    }
    [TestMethod]
    public void UnimplementedDisplayModeSelectionDoesNotChangeRuntimeModeOrTaskbarSettings()
    {
        SettingsManager.Replace(new AppSettings());
        var viewModel = new DisplayModesViewModel(new FakeDisplayMonitorService(), new TaskbarLengthConstraintsService(), new LocalizationService());
        var original = SettingsManager.Current.TaskbarExperience;

        viewModel.SwitchToFloatingBallModeCommand.Execute(null);
        viewModel.HoverLayerEnabled = false;

        Assert.IsTrue(viewModel.IsFloatingBallMode);
        Assert.IsTrue(viewModel.IsUnimplementedMode);
        Assert.AreEqual(WindowMode.Taskbar, SettingsManager.Current.WindowMode);
        Assert.AreEqual(original, SettingsManager.Current.TaskbarExperience);
    }

    [TestMethod]
    public void DisplayModesProtectsLastFullPanelGroupAndRecognizesPresets()
    {
        SettingsManager.Replace(new AppSettings
        {
            TaskbarExperience = TaskbarExperienceSettings.Default with
            {
                FullPanel = new TaskbarFullPanelSettings(true, false, false, false)
            }
        });
        var viewModel = new DisplayModesViewModel(new FakeDisplayMonitorService(), new TaskbarLengthConstraintsService(), new LocalizationService());

        viewModel.FullPanelMediaInfoVisible = false;
        Assert.IsTrue(viewModel.FullPanelMediaInfoVisible);
        Assert.IsFalse(viewModel.CanToggleFullPanelMediaInfo);

        viewModel.FullPanelMediaControlsVisible = true;
        viewModel.FullPanelMediaInfoVisible = false;
        Assert.IsFalse(viewModel.FullPanelMediaInfoVisible);
        Assert.AreEqual("自定义", viewModel.FullPanelLayoutStatus);

        viewModel.FullPanelMediaInfoVisible = true;
        Assert.AreEqual("紧凑", viewModel.FullPanelLayoutStatus);
        viewModel.FullPanelAudioControlsVisible = true;
        viewModel.FullPanelPerformanceVisible = true;
        Assert.AreEqual("完整", viewModel.FullPanelLayoutStatus);

        viewModel.ApplyCompactFullPanelPresetCommand.Execute(null);
        Assert.AreEqual("紧凑", viewModel.FullPanelLayoutStatus);
        viewModel.ApplyFullFullPanelPresetCommand.Execute(null);
        Assert.AreEqual("完整", viewModel.FullPanelLayoutStatus);
    }

    [TestMethod]
    public void DisplayModesUpdatesIndependentNotificationAndTaskbarTargets()
    {
        SettingsManager.Replace(new AppSettings());
        var viewModel = new DisplayModesViewModel(new FakeDisplayMonitorService(), new TaskbarLengthConstraintsService(), new LocalizationService());

        viewModel.TrackChangeNotificationEnabled = true;
        viewModel.ShowTrackChangeNotificationWhenFullscreen = true;
        viewModel.TrackChangeNotificationDurationSeconds = 6;
        viewModel.TrackChangeNotificationPosition = TrackChangeNotificationPosition.TopCenter;
        viewModel.TrackChangeNotificationTargetMode = NotificationTargetMode.Fixed;
        viewModel.TrackChangeNotificationFixedMonitorDeviceId = "DISPLAY2";
        var primary = viewModel.TaskbarMonitorOptions.Single(option => option.DeviceId == "DISPLAY1");
        var secondary = viewModel.TaskbarMonitorOptions.Single(option => option.DeviceId == "DISPLAY2");
        secondary.IsSelected = true;

        Assert.AreEqual("DISPLAY2", SettingsManager.Current.TrackChangeNotification.FixedMonitorDeviceId);
        CollectionAssert.AreEqual(
            new[] { "DISPLAY1", "DISPLAY2" },
            SettingsManager.Current.TaskbarTargetMonitorDeviceIds!.ToArray());
        Assert.IsNull(SettingsManager.Current.TaskbarTargetMonitorDeviceId);
        Assert.IsTrue(primary.CanToggle);
        primary.IsSelected = false;
        CollectionAssert.AreEqual(
            new[] { "DISPLAY2" },
            SettingsManager.Current.TaskbarTargetMonitorDeviceIds!.ToArray());
        Assert.IsFalse(secondary.CanToggle);
        secondary.IsSelected = false;
        Assert.IsTrue(secondary.IsSelected);
        Assert.IsFalse(viewModel.MonitorOptions.Any(option =>
            option.DeviceId == TaskbarTargetPolicy.LegacyAllTaskbarsDeviceId));
        Assert.AreEqual(6000, SettingsManager.Current.TrackChangeNotification.DurationMilliseconds);
        Assert.AreEqual(TrackChangeNotificationPosition.TopCenter, SettingsManager.Current.TrackChangeNotification.Position);
        Assert.IsTrue(SettingsManager.Current.TrackChangeNotification.ShowWhenFullscreen);
        Assert.IsTrue(viewModel.CanSelectTrackChangeNotificationMonitor);
    }

    [TestMethod]
    public void DisplayModesClampsFixedLengthToLiveTaskbarRange()
    {
        SettingsManager.Replace(new AppSettings());
        var constraints = new TaskbarLengthConstraintsService();
        constraints.Update(280, 520);
        var viewModel = new DisplayModesViewModel(new FakeDisplayMonitorService(), constraints, new LocalizationService());

        viewModel.FollowMediaTextLength = false;
        viewModel.FixedTaskbarLengthDip = 900;

        Assert.IsTrue(viewModel.UsesFixedTaskbarLength);
        Assert.AreEqual(520, viewModel.FixedTaskbarLengthDip);
        Assert.AreEqual(520, SettingsManager.Current.TaskbarExperience.FixedLengthDip);

        constraints.Update(320, 460);
        Assert.AreEqual(320, viewModel.FixedTaskbarLengthMinimum);
        Assert.AreEqual(460, viewModel.FixedTaskbarLengthMaximum);
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
        Assert.IsFalse(SettingsManager.Current.LyricsEnabled);
        Assert.AreEqual(700, SettingsManager.Current.Appearance.FontWeight);
        Assert.AreEqual(WindowMode.Taskbar, SettingsManager.Current.WindowMode);
        SettingsManager.ResetLayout();
        Assert.AreEqual(WindowMode.Taskbar, SettingsManager.Current.WindowMode);
        Assert.AreEqual(700, SettingsManager.Current.Appearance.FontWeight);
        SettingsManager.ResetAppearance();
        Assert.AreEqual(AppearanceSettings.Default, SettingsManager.Current.Appearance);

        SettingsManager.Current.Interaction = GlobalInteractionSettings.Default with
        {
            PrimaryWheelAction = WheelAction.SwitchMediaSource
        };
        SettingsManager.Current.TrayWheelBehavior = TrayWheelBehavior.Disabled;
        SettingsManager.ResetInteraction();
        Assert.AreEqual(GlobalInteractionSettings.Default, SettingsManager.Current.Interaction);
        Assert.AreEqual(TrayWheelBehavior.Disabled, SettingsManager.Current.TrayWheelBehavior);

        SettingsManager.Current.TaskbarExperience = TaskbarExperienceSettings.Default with
        {
            FullPanel = TaskbarFullPanelSettings.Compact
        };
        SettingsManager.Current.TrackChangeNotification = TrackChangeNotificationSettings.Default with { Enabled = true };
        SettingsManager.Current.TaskbarTargetMonitorDeviceIds = ["DISPLAY2"];
        SettingsManager.ResetDisplayModes();
        Assert.AreEqual(TaskbarFullPanelSettings.Full, SettingsManager.Current.TaskbarExperience.FullPanel);
        Assert.IsTrue(SettingsManager.Current.TrackChangeNotification.Enabled);
        Assert.AreEqual(0, SettingsManager.Current.TaskbarTargetMonitorDeviceIds?.Count ?? 0);
        SettingsManager.Current.SmtcSourceFilter = new SmtcSourceFilterSettings(true, ["player"]);
        SettingsManager.Current.QuickLaunch = new QuickLaunchSettings([
            new QuickLaunchEntry("player", "Player", QuickLaunchTargetKind.AppUserModelId, "Player.App!App")]);
        SettingsManager.Current.SpectrumComponent = new SpectrumComponentSettings(3, 8, 250);
        SettingsManager.Current.PerformanceComponent = new PerformanceComponentSettings([MetricKind.SystemGpu], 900, true);
        SettingsManager.ResetExtraFeatures();
        Assert.AreEqual(TrackChangeNotificationSettings.Default, SettingsManager.Current.TrackChangeNotification);
        Assert.AreEqual(SmtcSourceFilterSettings.Default, SettingsManager.Current.SmtcSourceFilter);
        Assert.AreEqual(0, SettingsManager.Current.QuickLaunch.Entries!.Count);
        Assert.AreEqual(SpectrumComponentSettings.Default, SettingsManager.Current.SpectrumComponent);
        CollectionAssert.AreEqual(
            PerformanceComponentSettings.Default.Metrics!.ToArray(),
            SettingsManager.Current.PerformanceComponent.Metrics!.ToArray());
        Assert.AreEqual(2500, SettingsManager.Current.PerformanceComponent.RefreshIntervalMilliseconds);
        Assert.IsTrue(SettingsManager.Current.PerformanceComponent.OpenTaskManagerOnClick);
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

    private static DisplayMonitorInfo Monitor(string id, bool primary) =>
        new(id, id, primary, new Rect(0, 0, 1920, 1080), new Rect(0, 0, 1920, 1040), 96, 96);

    private sealed class FakeDisplayMonitorService : IDisplayMonitorService
    {
        private readonly IReadOnlyList<DisplayMonitorInfo> _monitors =
            [Monitor("DISPLAY1", true), Monitor("DISPLAY2", false)];

        public event EventHandler? MonitorsChanged;

        public IReadOnlyList<DisplayMonitorInfo> GetMonitors() => _monitors;

        public void Refresh() => MonitorsChanged?.Invoke(this, EventArgs.Empty);

        public DisplayMonitorInfo? ResolveFixedMonitor(string? deviceId) =>
            DisplayTargetPolicy.ResolveFixed(_monitors, deviceId);

        public DisplayMonitorInfo? ResolveNotificationMonitor(NotificationTargetMode mode, string? fixedDeviceId) =>
            ResolveFixedMonitor(fixedDeviceId);

        public bool IsForegroundWindowFullscreen() => false;
    }
}
