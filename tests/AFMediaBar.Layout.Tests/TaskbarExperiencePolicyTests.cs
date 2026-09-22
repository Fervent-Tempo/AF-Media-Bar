using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>任务栏密度、进度与滚轮映射的纯策略测试。 / Pure policy tests for density, progress, and wheel mapping.</summary>
[TestClass]
public sealed class TaskbarExperiencePolicyTests
{
    [TestMethod]
    public void DensityChangesRealControlMetricsAndMinimumWidth()
    {
        var compact = TaskbarDensityMetrics.From(TaskbarInformationDensity.Minimal);
        var information = TaskbarDensityMetrics.From(TaskbarInformationDensity.Information);
        Assert.IsTrue(information.ButtonSize > compact.ButtonSize);
        Assert.IsTrue(information.ProgressWidth > compact.ProgressWidth);
        Assert.IsTrue(information.HoverLayerHeight > compact.HoverLayerHeight);
        Assert.IsTrue(information.SectionGap > compact.SectionGap);
        Assert.IsTrue(
            TaskbarExperiencePolicy.CalculateWidth(120, 44, 38, 4, true, true, true, true, true, TaskbarInformationDensity.Information, 900) >
            TaskbarExperiencePolicy.CalculateWidth(120, 44, 38, 4, true, true, true, true, true, TaskbarInformationDensity.Minimal, 900));
    }

    [TestMethod]
    public void HoverMinimumBelongsToMiddleSectionOnly()
    {
        var middleMinimum = TaskbarExperiencePolicy.CalculateHoverLayerWidth(
            true,
            true,
            TaskbarInformationDensity.Balanced);
        var metrics = TaskbarDensityMetrics.From(TaskbarInformationDensity.Balanced);
        var width = TaskbarExperiencePolicy.CalculateWidth(
            12,
            44,
            38,
            4,
            true,
            true,
            true,
            true,
            true,
            TaskbarInformationDensity.Balanced,
            double.PositiveInfinity);

        Assert.AreEqual(44 + metrics.SectionGap + middleMinimum + metrics.SectionGap + 38 + 4, width, 0.001);
    }

    [TestMethod]
    public void DisablingHoverRemovesArtificialMiddleMinimum()
    {
        var metrics = TaskbarDensityMetrics.From(TaskbarInformationDensity.Balanced);
        var width = TaskbarExperiencePolicy.CalculateWidth(
            12,
            44,
            38,
            4,
            true,
            true,
            true,
            false,
            true,
            TaskbarInformationDensity.Balanced,
            double.PositiveInfinity);

        Assert.AreEqual(44 + metrics.SectionGap + 12 + metrics.SectionGap + 38 + 4, width, 0.001);
    }

    [TestMethod]
    public void HoverMinimumShrinksWhenTransportOrProgressIsHidden()
    {
        var full = TaskbarExperiencePolicy.CalculateHoverLayerWidth(true, true, TaskbarInformationDensity.Balanced);
        var gestures = TaskbarExperiencePolicy.CalculateHoverLayerWidth(false, true, TaskbarInformationDensity.Balanced);
        var withoutProgress = TaskbarExperiencePolicy.CalculateHoverLayerWidth(false, false, TaskbarInformationDensity.Balanced);
        Assert.IsTrue(full > gestures);
        Assert.IsTrue(gestures > withoutProgress);
    }

    [TestMethod]
    public void IndependentHoverControlsDetermineMinimumWidth()
    {
        var defaults = TaskbarHoverControlsSettings.Default;
        var defaultWidth = TaskbarExperiencePolicy.CalculateHoverLayerWidth(
            defaults, true, TaskbarInformationDensity.Balanced);
        var allControlsWidth = TaskbarExperiencePolicy.CalculateHoverLayerWidth(
            new TaskbarHoverControlsSettings(true, true, true, true, true),
            true,
            TaskbarInformationDensity.Balanced);
        var progressUnavailableWidth = TaskbarExperiencePolicy.CalculateHoverLayerWidth(
            defaults, false, TaskbarInformationDensity.Balanced);

        Assert.IsTrue(allControlsWidth > defaultWidth);
        Assert.IsTrue(defaultWidth > progressUnavailableWidth);
    }

    [TestMethod]
    public void ComponentSpacingChangesHoverMinimum()
    {
        var compact = TaskbarExperiencePolicy.CalculateHoverLayerWidth(true, true, TaskbarInformationDensity.Balanced, 4);
        var spacious = TaskbarExperiencePolicy.CalculateHoverLayerWidth(true, true, TaskbarInformationDensity.Balanced, 24);
        Assert.IsTrue(spacious > compact);
    }

    [TestMethod]
    public void DisconnectedWidthContainsOnlyArtworkAndTrailingMargin()
    {
        var width = TaskbarExperiencePolicy.CalculateWidth(
            120,
            44,
            38,
            4,
            false,
            false,
            true,
            true,
            true,
            TaskbarInformationDensity.Balanced,
            double.PositiveInfinity);

        Assert.AreEqual(48, width, 0.001);
    }

    [TestMethod]
    public void PausedMediaWidthDoesNotReserveSpectrumSpace()
    {
        var metrics = TaskbarDensityMetrics.From(TaskbarInformationDensity.Balanced);
        var width = TaskbarExperiencePolicy.CalculateWidth(
            120,
            44,
            38,
            4,
            true,
            false,
            true,
            false,
            true,
            TaskbarInformationDensity.Balanced,
            double.PositiveInfinity);

        Assert.AreEqual(44 + metrics.SectionGap + 120 + 4, width, 0.001);
    }

    [TestMethod]
    public void FixedLengthClampsBetweenHoverMinimumAndAvailableMaximum()
    {
        Assert.AreEqual(
            280,
            TaskbarExperiencePolicy.ResolvePrimaryLength(640, 280, 520, TaskbarLengthMode.Fixed, 120),
            0.001);
        Assert.AreEqual(
            520,
            TaskbarExperiencePolicy.ResolvePrimaryLength(220, 280, 520, TaskbarLengthMode.Fixed, 900),
            0.001);
        Assert.AreEqual(
            420,
            TaskbarExperiencePolicy.ResolvePrimaryLength(420, 280, 520, TaskbarLengthMode.FollowContent, 300),
            0.001);
    }

    [TestMethod]
    public void InvalidFixedLengthSettingsNormalizeSafely()
    {
        var invalid = TaskbarExperienceSettings.Default with
        {
            LengthMode = (TaskbarLengthMode)99,
            FixedLengthDip = double.NaN
        };
        var normalized = invalid.Normalize();

        Assert.AreEqual(TaskbarLengthMode.FollowContent, normalized.LengthMode);
        Assert.AreEqual(TaskbarExperienceSettings.Default.FixedLengthDip, normalized.FixedLengthDip);
    }

    [TestMethod]
    public void MediaFontSizePercentNormalizesMissingAndOutOfRangeValues()
    {
        // schema 7 及更早的设置文件没有该字段，反序列化得到 0，必须回退到默认值而不是夹到下限。
        // Settings files up to schema 7 lack the field and deserialize it as 0, which must fall back to the default rather
        // than being clamped to the lower bound.
        Assert.AreEqual(TaskbarExperienceSettings.Default.MediaFontSizePercent,
            (TaskbarExperienceSettings.Default with { MediaFontSizePercent = 0 }).Normalize().MediaFontSizePercent);
        Assert.AreEqual(TaskbarExperienceSettings.MinimumMediaFontSizePercent,
            (TaskbarExperienceSettings.Default with { MediaFontSizePercent = 10 }).Normalize().MediaFontSizePercent);
        Assert.AreEqual(TaskbarExperienceSettings.MaximumMediaFontSizePercent,
            (TaskbarExperienceSettings.Default with { MediaFontSizePercent = 400 }).Normalize().MediaFontSizePercent);
        Assert.AreEqual(115,
            (TaskbarExperienceSettings.Default with { MediaFontSizePercent = 115 }).Normalize().MediaFontSizePercent);
    }

    /// <summary>
    /// 跑马灯的判据是"文字比可用宽度长"，与长度模式无关：跟随内容模式下媒体栏被任务栏上限夹住时文字同样溢出，
    /// 必须能滚动看全，否则用户只看到被截断的标题。
    /// The marquee keys off the text being wider than the available width, independent of the length mode: in follow-content mode
    /// the bar is clamped by the taskbar maximum and the text overflows there too, so it must stay scrollable instead of leaving a
    /// truncated title.
    /// </summary>
    [TestMethod]
    public void MarqueeOverflowFollowsTheMeasuredTextRegardlessOfLengthMode()
    {
        Assert.AreEqual(80, TaskbarExperiencePolicy.CalculateMarqueeOverflow(280, 200), 0.001);
        Assert.AreEqual(0, TaskbarExperiencePolicy.CalculateMarqueeOverflow(180, 200), 0.001);
        Assert.AreEqual(0, TaskbarExperiencePolicy.CalculateMarqueeOverflow(200, 200), 0.001);
        Assert.AreEqual(0, TaskbarExperiencePolicy.CalculateMarqueeOverflow(double.NaN, 200), 0.001);
        Assert.AreEqual(0, TaskbarExperiencePolicy.CalculateMarqueeOverflow(280, double.PositiveInfinity), 0.001);
        Assert.AreEqual(280, TaskbarExperiencePolicy.CalculateMarqueeOverflow(280, 0), 0.001);
    }

    [TestMethod]
    public void ProgressExtrapolatesWhilePlayingAndClampsToDuration()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = MediaSnapshot.Disconnected with
        {
            IsConnected = true,
            IsPlaying = true,
            Position = 8,
            Duration = 10,
            PlaybackRate = 1,
            TimelineUpdatedAt = now.AddSeconds(-5)
        };
        Assert.AreEqual(10, TaskbarExperiencePolicy.GetPosition(snapshot, now));
    }

    [TestMethod]
    public void ShiftModifierSelectsChordAndPlainWheelOtherwise()
    {
        var settings = GlobalInteractionSettings.Default with { Modifier = InteractionModifier.Shift };
        Assert.AreEqual(WheelAction.SwitchMediaSource, GlobalWheelGesturePolicy.Resolve(settings, true, false, false));
        Assert.AreEqual(WheelAction.PreviousNext, GlobalWheelGesturePolicy.Resolve(settings, false, false, false));
    }

    [TestMethod]
    public void PlayerClickBindingsRemainIndependent()
    {
        var settings = GlobalInteractionSettings.Default with
        {
            ArtworkClickAction = PlayerClickAction.ActivateSource,
            TextClickAction = PlayerClickAction.TogglePlayPause
        };

        Assert.AreEqual(PlayerClickAction.ActivateSource, PlayerClickBindingPolicy.Resolve(settings, artwork: true));
        Assert.AreEqual(PlayerClickAction.TogglePlayPause, PlayerClickBindingPolicy.Resolve(settings, artwork: false));
    }

    [TestMethod]
    public void ConfiguredChordOverridesPrimaryWheelAction()
    {
        var settings = GlobalInteractionSettings.Default with
        {
            Modifier = InteractionModifier.RightMouseButton,
            PrimaryWheelAction = WheelAction.PreviousNext,
            ChordWheelAction = WheelAction.SwitchMediaSource
        };
        Assert.AreEqual(WheelAction.SwitchMediaSource, GlobalWheelGesturePolicy.Resolve(settings, false, false, true));
        Assert.AreEqual(WheelAction.PreviousNext, GlobalWheelGesturePolicy.Resolve(settings, false, true, false));
    }

    [TestMethod]
    public void AllPlayerWheelActionsRemainSelectableAndTrayUsesSharedModifier()
    {
        var settings = GlobalInteractionSettings.Default with
        {
            PrimaryWheelAction = WheelAction.OutputDevice,
            ChordWheelAction = WheelAction.CurrentApplicationVolume,
            Modifier = InteractionModifier.LeftMouseButton,
            TrayPrimaryWheelAction = TrayWheelBehavior.SwitchOutputDevice,
            TrayChordWheelAction = TrayWheelBehavior.AdjustVolume
        };

        var normalized = settings.Normalize();

        Assert.AreEqual(WheelAction.OutputDevice, normalized.PrimaryWheelAction);
        Assert.AreEqual(WheelAction.CurrentApplicationVolume, normalized.ChordWheelAction);
        Assert.AreEqual(TrayWheelBehavior.AdjustVolume, GlobalWheelGesturePolicy.ResolveTray(normalized, false, true, false));
        Assert.AreEqual(TrayWheelBehavior.SwitchOutputDevice, GlobalWheelGesturePolicy.ResolveTray(normalized, false, false, false));
    }

    [TestMethod]
    public void MediaSourceWheelMovesCircularlyInBothDirections()
    {
        Assert.AreEqual(2, WheelInput.MoveCircular(0, -1, 3));
        Assert.AreEqual(0, WheelInput.MoveCircular(2, 1, 3));
        Assert.AreEqual(1, WheelInput.MoveCircular(0, 4, 3));
    }
}
