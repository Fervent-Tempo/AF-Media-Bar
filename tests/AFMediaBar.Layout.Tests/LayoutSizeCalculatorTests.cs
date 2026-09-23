using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Services.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Classes.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Windows;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class LayoutSizeCalculatorTests
{
    [TestMethod]
    public void SpacingScaleChangesOnlyThePrimaryGap()
    {
        var layout = LayoutPresets.GetLayout(WindowMode.Taskbar, LayoutOrientation.Horizontal);

        var request = LayoutSizeCalculator.Calculate(layout, 1.25, 1, 0, 1000, "base");

        Assert.AreEqual(62, request.Width, 0.01);
        Assert.AreEqual(44, request.Height, 0.01);
    }

    [TestMethod]
    public void ThicknessScaleScalesCanvasButPreservesGapContribution()
    {
        var layout = LayoutPresets.GetLayout(WindowMode.Taskbar, LayoutOrientation.Horizontal);

        var request = LayoutSizeCalculator.Calculate(layout, 1, 1.25, 0, 1000, "thick");

        Assert.AreEqual(75, request.Width, 0.01);
        Assert.AreEqual(55, request.Height, 0.01);
    }

    [TestMethod]
    public void MeasuredTextExpandsTheAutoSizedComponentAndHonorsMaximum()
    {
        var layout = LayoutPresets.GetLayout(WindowMode.Taskbar, LayoutOrientation.Horizontal);

        var request = LayoutSizeCalculator.Calculate(layout, 1, 1, 400, 350, "long");

        Assert.AreEqual(350, request.Width, 0.01);
    }

    [TestMethod]
    public void VerticalLayoutShrinksHeightAlongThePrimaryAxis()
    {
        var layout = LayoutPresets.GetLayout(WindowMode.Taskbar, LayoutOrientation.Vertical);

        var request = LayoutSizeCalculator.Calculate(layout, 1.25, 1, 0, 1000, "vertical");

        Assert.AreEqual(80, request.Width, 0.01);
        Assert.AreEqual(80, request.Height, 0.01);
    }

    [TestMethod]
    public void ContentFingerprintAndForcedRefreshFlagArePreserved()
    {
        var layout = LayoutPresets.GetLayout(WindowMode.DynamicIsland, LayoutOrientation.Horizontal);

        var request = LayoutSizeCalculator.Calculate(layout, 1, 1, 120, 1000, "lyric-1", true);

        Assert.AreEqual("lyric-1", request.ContentFingerprint);
        Assert.IsTrue(request.IsForcedRefresh);
    }

    [TestMethod]
    public void HiddenComponentsDoNotContributeSpacingOrContentWidth()
    {
        var layout = new LayoutSchema
        {
            Orientation = LayoutOrientation.Horizontal,
            Canvas = new CanvasConfig { Width = 200, Height = 40 },
            Components =
            [
                new ComponentConfig
                {
                    Id = LayoutComponentIds.Artwork,
                    IsVisible = false,
                    SpacingAfter = 100,
                    Bounds = new ComponentBounds(0, 0, 40, 40)
                },
                new ComponentConfig
                {
                    Id = LayoutComponentIds.Title,
                    AutoSizePrimary = true,
                    Bounds = new ComponentBounds(40, 0, 100, 40)
                }
            ]
        };

        var request = LayoutSizeCalculator.Calculate(layout, 1.25, 1, 100, 500, "title");

        Assert.AreEqual(200, request.Width, 0.01);
    }

    [TestMethod]
    public void EmptyContentRemovesTheAutoSizedComponentWidth()
    {
        var layout = new LayoutSchema
        {
            Orientation = LayoutOrientation.Horizontal,
            Canvas = new CanvasConfig { Width = 100, Height = 40 },
            Components =
            [
                new ComponentConfig
                {
                    Id = LayoutComponentIds.Title,
                    AutoSizePrimary = true,
                    Bounds = new ComponentBounds(0, 0, 20, 40)
                }
            ]
        };

        var request = LayoutSizeCalculator.Calculate(layout, 1, 1, 0, 1000, "empty");

        Assert.AreEqual(80, request.Width, 0.01);
    }

    [TestMethod]
    public void ShortTextCanShrinkBelowThePresetContentWidth()
    {
        var layout = LayoutPresets.GetLayout(WindowMode.Taskbar, LayoutOrientation.Horizontal);

        var request = LayoutSizeCalculator.Calculate(layout, 1, 1, 40, 1000, "short");
        var resized = LayoutSizeCalculator.ResizePrimary(layout, request.PrimaryLength);

        Assert.AreEqual(100, request.Width, 0.01);
        Assert.AreEqual(40, resized.Components.Single(component => component.Id == "song-info").Bounds.Width, 0.01);
    }

    [TestMethod]
    public void TaskbarOccupiedRangesLeaveOnlySafeIntervals()
    {
        var ranges = TaskbarFreeRangeCalculator.Calculate(
            primaryLength: 1000,
            occupied:
            [
                new TaskbarPrimaryRange(300, 500),
                new TaskbarPrimaryRange(700, 800)
            ],
            edgePaddingPixels: 20,
            gapPixels: 10);

        Assert.AreEqual(3, ranges.Count);
        Assert.AreEqual(20, ranges[0].Start);
        Assert.AreEqual(290, ranges[0].End);
        Assert.AreEqual(510, ranges[1].Start);
        Assert.AreEqual(690, ranges[1].End);
        Assert.AreEqual(810, ranges[2].Start);
        Assert.AreEqual(980, ranges[2].End);
    }

    [TestMethod]
    public void ScaledLayoutFactoryKeepsThicknessIndependentFromSpacingScale()
    {
        var layout = LayoutPresets.GetLayout(WindowMode.Taskbar, LayoutOrientation.Horizontal);

        var scaled = ScaledLayoutFactory.Create(layout, lengthScale: 1.25, thicknessScale: 1);

        Assert.AreEqual(302, scaled.Canvas.Width, 0.01);
        Assert.AreEqual(44, scaled.Canvas.Height, 0.01);
        Assert.AreEqual(54, scaled.Components[1].Bounds.X, 0.01);
    }

    [TestMethod]
    public void ScaledLayoutFactoryScalesMediaFontSizesWithoutChangingBounds()
    {
        var layout = LayoutPresets.GetLayout(WindowMode.Taskbar, LayoutOrientation.Horizontal);

        var scaled = ScaledLayoutFactory.Create(layout, lengthScale: 1, thicknessScale: 1, mediaFontScale: 1.25);

        var songInfo = scaled.Components[1];
        Assert.AreEqual(44, scaled.Canvas.Height, 0.01);
        Assert.AreEqual(layout.Components[1].Bounds.Width, songInfo.Bounds.Width, 0.01);
        Assert.AreEqual(14.0 * 1.25, (double)songInfo.Properties["titleFontSize"], 0.01);
        Assert.AreEqual(12.0 * 1.25, (double)songInfo.Properties["artistFontSize"], 0.01);
        Assert.AreEqual(12.0 * 1.25, (double)songInfo.Properties["lyricsFontSize"], 0.01);
    }

    [TestMethod]
    public void ScaledLayoutFactoryCombinesThicknessAndMediaFontScale()
    {
        var layout = LayoutPresets.GetLayout(WindowMode.Taskbar, LayoutOrientation.Horizontal);

        var scaled = ScaledLayoutFactory.Create(layout, lengthScale: 1, thicknessScale: 0.9, mediaFontScale: 1.2);

        var songInfo = scaled.Components[1];
        Assert.AreEqual(14.0 * 0.9 * 1.2, (double)songInfo.Properties["titleFontSize"], 0.01);
        Assert.AreEqual(12.0 * 0.9 * 1.2, (double)songInfo.Properties["artistFontSize"], 0.01);
    }

    [TestMethod]
    public void ScaledLayoutFactoryClampsMediaFontScale()
    {
        var layout = LayoutPresets.GetLayout(WindowMode.Taskbar, LayoutOrientation.Horizontal);

        var scaled = ScaledLayoutFactory.Create(layout, lengthScale: 1, thicknessScale: 1, mediaFontScale: 4);

        Assert.AreEqual(14.0 * 2, (double)scaled.Components[1].Properties["titleFontSize"], 0.01);
    }

    [TestMethod]
    public void TaskbarPlacementCalculatorClampsManualOffsetsToSafeRange()
    {
        var placement = TaskbarBarPlacementCalculator.Calculate(
            primaryLength: 1000,
            primarySize: 240,
            crossLength: 48,
            crossSize: 44,
            preferredRange: new TaskbarPrimaryRange(100, 400),
            position: TaskbarBarPosition.End,
            manualPadding: 200,
            crossAxisOffsetDip: 20,
            dpiScale: 1,
            edgePadding: 20);

        Assert.AreEqual(160, placement.Primary);
        Assert.AreEqual(4, placement.Cross);
    }

    /// <summary>
    /// 媒体栏的顶边在任何输入下都 MUST NOT 落在任务栏顶边之上：居中/偏移算出来的横轴位置一律夹进
    /// `[0, 任务栏横轴 − 媒体栏横轴]`，而媒体栏比任务栏还高（横轴差为负）时结果是 0——顶边与任务栏顶边对齐，
    /// 溢出留在下方被裁掉。
    ///
    /// 这条守的正是"媒体栏顶边越过任务栏顶边被裁切"：厚度解析器一旦读到错误的 DPI 或过期的任务栏矩形，
    /// 媒体栏就会比任务栏高（本机实测：任务栏 48 px / DPI 125%，而 44 DIP 的画布是 55 px，高出 7 px），
    /// 若那时的横轴位置允许为负，界面就会把顶部切掉一截。
    /// The bar's top edge MUST NOT land above the taskbar's top edge under any input: the centred or offset cross position is always clamped into
    /// `[0, taskbar cross extent - bar cross extent]`, and when the bar is taller than the taskbar (a negative difference) the result is 0 — the top edge
    /// lines up with the taskbar's top edge and the overflow stays below, where it is clipped.
    ///
    /// This guards exactly "the bar's top edge crosses the taskbar's top edge and is clipped": once the thickness resolver reads a wrong DPI or a stale
    /// taskbar rectangle the bar becomes taller than the taskbar (measured on this machine: taskbar 48 px at 125% DPI while a 44 DIP canvas is 55 px, so
    /// 7 px too tall), and a negative cross position there would cut the top off.
    /// </summary>
    [TestMethod]
    public void BarTopEdgeNeverStartsAboveTheTaskbarTopEdge()
    {
        // 媒体栏比任务栏高：夹到 0，顶边与任务栏顶边对齐（溢出只能出现在下方）。
        // The bar is taller than the taskbar: clamped to 0, so the top edges line up and any overflow is below.
        var taller = TaskbarBarPlacementCalculator.Calculate(
            primaryLength: 1920,
            primarySize: 400,
            crossLength: 48,
            crossSize: 55,
            preferredRange: new TaskbarPrimaryRange(20, 1900),
            position: TaskbarBarPosition.Start,
            manualPadding: 0,
            crossAxisOffsetDip: 0,
            dpiScale: 1.25,
            edgePadding: 20);
        Assert.AreEqual(0, taller.Cross, "a bar taller than the taskbar must align with its top edge, never start above it");

        // 负的横轴偏移也不能把它推出任务栏上方。
        // A negative cross-axis offset must not push it above the taskbar either.
        var offset = TaskbarBarPlacementCalculator.Calculate(
            primaryLength: 1920,
            primarySize: 400,
            crossLength: 48,
            crossSize: 44,
            preferredRange: new TaskbarPrimaryRange(20, 1900),
            position: TaskbarBarPosition.Start,
            manualPadding: 0,
            crossAxisOffsetDip: -20,
            dpiScale: 1,
            edgePadding: 20);
        Assert.AreEqual(0, offset.Cross);

        // 竖向任务栏走同一条规则（横轴是宽度）。
        // A vertical taskbar follows the same rule (the cross axis is the width there).
        var vertical = TaskbarBarPlacementCalculator.Calculate(
            primaryLength: 1080,
            primarySize: 400,
            crossLength: 48,
            crossSize: 60,
            preferredRange: new TaskbarPrimaryRange(20, 1000),
            position: TaskbarBarPosition.Start,
            manualPadding: 0,
            crossAxisOffsetDip: 0,
            dpiScale: 1,
            edgePadding: 20);
        Assert.AreEqual(0, vertical.Cross);
    }

    [TestMethod]
    public void DominantColorCalculatorReturnsColorForSmallOpaqueArtwork()
    {
        // BGRA pixels: two red and two blue samples.
        byte[] pixels =
        [
            0, 0, 240, 255, 0, 0, 240, 255,
            240, 0, 0, 255, 240, 0, 0, 255
        ];

        var colors = DominantColorCalculator.Calculate(pixels, 2, 2, 1);

        Assert.AreEqual(1, colors.Count);
        Assert.AreEqual(255, colors[0].A);
        Assert.IsTrue(colors[0].R > 100 || colors[0].B > 100);
    }

    [TestMethod]
    public void DominantColorCalculatorPreservesSingleColorFallbackForTransparentPixels()
    {
        byte[] transparent = [0, 0, 255, 0, 0, 0, 255, 0];

        var colors = DominantColorCalculator.Calculate(transparent, 2, 1, 1);

        Assert.AreEqual(1, colors.Count);
        Assert.AreEqual(7, colors[0].R);
        Assert.AreEqual(7, colors[0].G);
        Assert.AreEqual(7, colors[0].B);
    }

    [TestMethod]
    public void DominantColorCalculatorReturnsEmptyForInvalidInput()
    {
        byte[] transparent = [0, 0, 255, 0, 0, 0, 255, 0];

        Assert.AreEqual(0, DominantColorCalculator.Calculate(transparent, 0, 1, 1).Count);
        Assert.AreEqual(0, DominantColorCalculator.Calculate([1, 2, 3], 1, 1, 1).Count);
    }

    [TestMethod]
    public void DominantColorCalculatorCapsKMeansColorsToAvailableSamples()
    {
        byte[] pixels = [0, 0, 240, 255];

        var colors = DominantColorCalculator.Calculate(pixels, 1, 1, 4);

        Assert.AreEqual(1, colors.Count);
    }

    [TestMethod]
    public void DynamicIslandPositionCalculatorClampsExpandedPositionToWorkArea()
    {
        var position = DynamicIslandPositionCalculator.GetExpandedPosition(
            new Rect(100, 50, 800, 500), 240, 80, savedLeft: 999, savedTop: -100);

        Assert.AreEqual(660, position.X, 0.01);
        Assert.AreEqual(50, position.Y, 0.01);
    }

    [TestMethod]
    public void DynamicIslandPositionCalculatorKeepsCollapsedRevealOnEachEdge()
    {
        var workArea = new Rect(100, 50, 800, 500);

        Assert.AreEqual(-135, DynamicIslandPositionCalculator.GetCollapsedPosition(workArea, 240, 80, DynamicIslandEdge.Left, 5, 300, 200).X, 0.01);
        Assert.AreEqual(895, DynamicIslandPositionCalculator.GetCollapsedPosition(workArea, 240, 80, DynamicIslandEdge.Right, 5, 300, 200).X, 0.01);
        Assert.AreEqual(-25, DynamicIslandPositionCalculator.GetCollapsedPosition(workArea, 240, 80, DynamicIslandEdge.Top, 5, 300, 200).Y, 0.01);
        Assert.AreEqual(545, DynamicIslandPositionCalculator.GetCollapsedPosition(workArea, 240, 80, DynamicIslandEdge.Bottom, 5, 300, 200).Y, 0.01);
    }

    [TestMethod]
    public void DynamicIslandPositionCalculatorDetectsNearestDockedEdge()
    {
        var workArea = new Rect(0, 0, 1000, 600);

        Assert.AreEqual(DynamicIslandEdge.Right,
            DynamicIslandPositionCalculator.FindDockedEdge(770, 200, 220, 80, workArea, 28));
        Assert.IsNull(
            DynamicIslandPositionCalculator.FindDockedEdge(400, 200, 220, 80, workArea, 28));
    }

    [TestMethod]
    public void DynamicIslandPositionCalculatorRestoresNormalizedDpiCenterAndDocking()
    {
        var workArea = new Rect(100, 50, 800, 500);
        var free = DynamicIslandPositionCalculator.GetDpiRestoredPosition(
            workArea, 200, 80, new Point(0.75, 0.5), dockedEdge: null);
        var docked = DynamicIslandPositionCalculator.GetDpiRestoredPosition(
            workArea, 200, 80, new Point(0.75, 0.5), DynamicIslandEdge.Left);

        Assert.AreEqual(600, free.X, 0.01);
        Assert.AreEqual(260, free.Y, 0.01);
        Assert.AreEqual(100, docked.X, 0.01);
        Assert.AreEqual(260, docked.Y, 0.01);
    }

    [TestMethod]
    public void MediaBarSizeAnimationCalculatorUsesCubicEaseOut()
    {
        var frame = MediaBarSizeAnimationCalculator.Advance(100, 300, 0, 110);

        Assert.AreEqual(0.5, frame.Progress, 0.001);
        Assert.AreEqual(275, frame.Value, 0.01);
        Assert.IsFalse(frame.IsCompleted);
    }

    [TestMethod]
    public void MediaBarSizeAnimationCalculatorClampsProgressAndCompletes()
    {
        var frame = MediaBarSizeAnimationCalculator.Advance(100, 300, 0.9, 100);

        Assert.AreEqual(300, frame.Value, 0.01);
        Assert.AreEqual(1, frame.Progress, 0.001);
        Assert.IsTrue(frame.IsCompleted);
    }

    [TestMethod]
    public void MediaBarSizeAnimationCalculatorHandlesNonPositiveDuration()
    {
        var frame = MediaBarSizeAnimationCalculator.Advance(100, 300, 0, 16, 0);

        Assert.AreEqual(300, frame.Value, 0.01);
        Assert.IsTrue(frame.IsCompleted);
    }

    [TestMethod]
    public void MediaBarSizeAnimationCalculatorUsesProvidedDuration()
    {
        var frame = MediaBarSizeAnimationCalculator.Advance(100, 300, 0, 80, 160);

        Assert.AreEqual(0.5, frame.Progress, 0.001);
        Assert.AreEqual(275, frame.Value, 0.01);
        Assert.IsFalse(frame.IsCompleted);
    }

    [TestMethod]
    public void MediaBarSizeAnimationCalculatorTreatsNonFiniteProgressAndElapsedAsNeutral()
    {
        var frame = MediaBarSizeAnimationCalculator.Advance(100, 300, double.NaN, double.PositiveInfinity, 220);

        Assert.AreEqual(0, frame.Progress, 0.001);
        Assert.AreEqual(100, frame.Value, 0.01);
        Assert.IsFalse(frame.IsCompleted);
    }

    [TestMethod]
    public void WindowBackdropPolicyFallsBackForUnsupportedMicaAndHighContrast()
    {
        // 没有系统材质时退到 Acrylic（Windows 10 上走窗口自绘的 Accent 模糊路径，仍然给到半透明表面），
        // 只有高对比度才退到纯色。
        // Without a system material it falls back to Acrylic, which on Windows 10 goes through the window-painted Accent blur path and
        // still yields a translucent surface; only high contrast falls back to solid.
        Assert.AreEqual(
            ApplicationBackdropMode.Acrylic,
            WindowBackdropPolicy.Resolve(ApplicationBackdropMode.Mica, highContrast: false, supportsMica: false, supportsMicaAlt: false));
        Assert.AreEqual(
            ApplicationBackdropMode.FluentSolid,
            WindowBackdropPolicy.Resolve(ApplicationBackdropMode.Acrylic, highContrast: true, supportsMica: true, supportsMicaAlt: true));
        Assert.AreEqual(
            ApplicationBackdropMode.FluentSolid,
            WindowBackdropPolicy.Resolve(ApplicationBackdropMode.Mica, highContrast: true, supportsMica: false, supportsMicaAlt: false));
        Assert.AreEqual(
            ApplicationBackdropMode.Acrylic,
            WindowBackdropPolicy.Resolve(ApplicationBackdropMode.Acrylic, highContrast: false, supportsMica: false, supportsMicaAlt: false));
        // 云母 Alt 拿不到时退到云母；连云母都没有（Windows 10）时退到 Acrylic 而不是纯色。
        // Mica Alt falls back to Mica when unavailable, and to Acrylic rather than solid when Mica is unavailable too (Windows 10).
        Assert.AreEqual(
            ApplicationBackdropMode.Mica,
            WindowBackdropPolicy.Resolve(ApplicationBackdropMode.MicaAlt, highContrast: false, supportsMica: true, supportsMicaAlt: false));
        Assert.AreEqual(
            ApplicationBackdropMode.Acrylic,
            WindowBackdropPolicy.Resolve(ApplicationBackdropMode.MicaAlt, highContrast: false, supportsMica: false, supportsMicaAlt: false));
        Assert.AreEqual(
            ApplicationBackdropMode.MicaAlt,
            WindowBackdropPolicy.Resolve(ApplicationBackdropMode.MicaAlt, highContrast: false, supportsMica: true, supportsMicaAlt: true));
    }

    [TestMethod]
    public void WindowBackdropTintFollowsConcentrationAndTheme()
    {
        // 浓度直接映射成 alpha：默认 60% ≈ 0x99，上限 100% = 不透明，且深色主题用深底、浅色主题用浅底。
        // The concentration maps straight onto alpha: the 60% default is about 0x99, the 100% bound is opaque, and a dark theme
        // gets a dark base while a light theme gets a light one.
        Assert.AreEqual(unchecked((int)0x99202020), WindowBackdropPolicy.ResolveLegacyTint(dark: true, AppearanceSettings.DefaultBackdropTintOpacityPercent));
        Assert.AreEqual(unchecked((int)0xFF202020), WindowBackdropPolicy.ResolveLegacyTint(dark: true, 100));
        Assert.AreEqual(unchecked((int)0x99F9F9F9), WindowBackdropPolicy.ResolveLegacyTint(dark: false, AppearanceSettings.DefaultBackdropTintOpacityPercent));
        // 越界值按滑杆区间夹取，而不是写进一个无法呈现的 alpha。
        // Out-of-range values are clamped to the slider's range instead of producing an unrenderable alpha.
        Assert.AreEqual(
            unchecked((int)0xFF202020),
            WindowBackdropPolicy.ResolveLegacyTint(dark: true, 400));
        Assert.AreEqual(
            WindowBackdropPolicy.ResolveLegacyTint(dark: true, AppearanceSettings.MinimumBackdropTintOpacityPercent),
            WindowBackdropPolicy.ResolveLegacyTint(dark: true, -50));
    }

    [TestMethod]
    public void PopupAppearancePolicyKeepsMenuOpaque()
    {
        Assert.IsFalse(PopupAppearancePolicy.AllowsNativeBackdrop);
        Assert.AreEqual("AppMenuBackgroundBrush", PopupAppearancePolicy.BackgroundResourceKey);
    }

    [TestMethod]
    public void AudioPoliciesPreserveDelayVersionWheelAndTooltipSemantics()
    {
        Assert.AreEqual(1200, AudioApplyPolicy.OutputDevicePreviewDelayMilliseconds);
        Assert.AreEqual(100, AudioApplyPolicy.ApplicationVolumeDelayMilliseconds);
        Assert.IsTrue(AudioApplyPolicy.IsCurrent(false, 3, 3));
        Assert.IsFalse(AudioApplyPolicy.IsCurrent(false, 2, 3));
        Assert.AreEqual(1, TrayWheelPolicy.GetVolumeSteps(120));
        Assert.AreEqual(-1, TrayWheelPolicy.GetVolumeSteps(-120));
        Assert.AreEqual(0, TrayWheelPolicy.GetVolumeSteps(0));
        Assert.AreEqual(-1, TrayWheelPolicy.GetDeviceSteps(120));
        Assert.AreEqual(1, TrayWheelPolicy.GetDeviceSteps(-120));
        Assert.AreEqual(-2, TrayWheelPolicy.GetDeviceSteps(240));
        Assert.AreEqual(
            "输出设备：扬声器",
            AudioTooltipPolicy.Build(
                TrayWheelBehavior.SwitchOutputDevice,
                null,
                new AudioDeviceOption("id", "policy", "扬声器", true)));
        Assert.AreEqual(
            "当前媒体音量：不可用",
            AudioTooltipPolicy.Build(TrayWheelBehavior.AdjustVolume, null, null));
        Assert.AreEqual(
            "托盘滚轮已禁用",
            AudioTooltipPolicy.Build(TrayWheelBehavior.Disabled, null, null));
    }

    [TestMethod]
    public void TaskbarRecoveryPolicyPreservesValidatedTimingAndMessageBoundaries()
    {
        Assert.AreEqual(8, TaskbarRecoveryPolicy.MaximumAttempts);
        Assert.AreEqual(TimeSpan.FromMilliseconds(900), TaskbarRecoveryPolicy.GetDelay(0));
        Assert.AreEqual(TimeSpan.FromMilliseconds(600), TaskbarRecoveryPolicy.GetDelay(7));
        Assert.AreEqual(2, TaskbarRecoveryPolicy.RequiredStableSamples);
        Assert.IsTrue(TaskbarHostMessagePolicy.IsEnvironmentChange(NativeMethods.WM_DISPLAYCHANGE));
        Assert.IsTrue(TaskbarHostMessagePolicy.ShouldSuppressPropagation(NativeMethods.WM_GETOBJECT));
        Assert.IsFalse(TaskbarHostMessagePolicy.ShouldSuppressPropagation(0x000F));
    }

}
