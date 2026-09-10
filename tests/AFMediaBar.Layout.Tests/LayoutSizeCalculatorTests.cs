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
    public void ContentFingerprintAndResetFlagArePreserved()
    {
        var layout = LayoutPresets.GetLayout(WindowMode.DynamicIsland, LayoutOrientation.Horizontal);

        var request = LayoutSizeCalculator.Calculate(layout, 1, 1, 120, 1000, "lyric-1", true);

        Assert.AreEqual("lyric-1", request.ContentFingerprint);
        Assert.IsTrue(request.IsResetToPreset);
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
    public void WindowBackdropPolicyFallsBackForUnsupportedMicaAndHighContrast()
    {
        Assert.AreEqual(
            ApplicationBackdropMode.FluentSolid,
            WindowBackdropPolicy.Resolve(ApplicationBackdropMode.Mica, highContrast: false, supportsMica: false));
        Assert.AreEqual(
            ApplicationBackdropMode.FluentSolid,
            WindowBackdropPolicy.Resolve(ApplicationBackdropMode.Acrylic, highContrast: true, supportsMica: true));
        Assert.AreEqual(
            ApplicationBackdropMode.Acrylic,
            WindowBackdropPolicy.Resolve(ApplicationBackdropMode.Acrylic, highContrast: false, supportsMica: false));
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
        Assert.AreEqual(
            "输出设备：扬声器",
            AudioTooltipPolicy.Build(
                TrayWheelBehavior.SwitchOutputDevice,
                null,
                new AudioDeviceOption("id", "policy", "扬声器", true)));
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

    [TestMethod]
    public void LyricLinePresenterCachesLrcAndClearsOnMissingLyrics()
    {
        var presenter = new LyricLinePresenter();
        var lyrics = new LyricsResult(
            "test",
            "[00:01.00]第一行\n[00:02.00]第二行",
            "[00:01.02]First line\n[00:02.02]Second line");

        var first = presenter.Update(lyrics, 1.1);
        Assert.IsTrue(first.Changed);
        Assert.AreEqual("第一行", first.Text);
        Assert.AreEqual("第二行", first.NextText);
        Assert.AreEqual("First line", first.TranslationText);

        var unchanged = presenter.Update(lyrics, 1.1);
        Assert.IsFalse(unchanged.Changed);
        Assert.AreEqual("第一行", unchanged.Text);

        var second = presenter.Update(lyrics, 2.1);
        Assert.AreEqual("第二行", second.Text);
        Assert.AreEqual(string.Empty, second.NextText);
        Assert.AreEqual("Second line", second.TranslationText);
        Assert.IsTrue(presenter.Update(null, 0).Changed);
        Assert.AreEqual(string.Empty, presenter.Update(null, 0).Text);
    }

    [TestMethod]
    public void LyricLinePresenterDoesNotReuseAnUnmatchedTranslation()
    {
        var presenter = new LyricLinePresenter();
        var lyrics = new LyricsResult(
            "test",
            "[00:01.00]第一行\n[00:03.00]第三行",
            "[00:01.00]First line");

        var update = presenter.Update(lyrics, 3.1);

        Assert.AreEqual("第三行", update.Text);
        Assert.AreEqual(string.Empty, update.TranslationText);
    }

    [TestMethod]
    public void LyricLinePresenterRefreshesWhenTranslationArrivesForTheSameLyrics()
    {
        var presenter = new LyricLinePresenter();
        const string lrc = "[00:01.00]第一行";

        presenter.Update(new LyricsResult("test", lrc, null), 1.1);
        var enriched = presenter.Update(
            new LyricsResult("test", lrc, "[00:01.00]First line"),
            1.1);

        Assert.IsTrue(enriched.Changed);
        Assert.AreEqual("First line", enriched.TranslationText);
    }
}
