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
            TaskbarExperiencePolicy.CalculateWidth(120, 44, 38, 4, true, true, true, TaskbarInformationDensity.Information, 900) >
            TaskbarExperiencePolicy.CalculateWidth(120, 44, 38, 4, true, true, true, TaskbarInformationDensity.Minimal, 900));
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
    public void ButtonsDisablePlayerWheelButTrayCanKeepGlobalMapping()
    {
        var settings = GlobalInteractionSettings.Default with { Mode = MediaInteractionMode.Buttons };
        Assert.IsNull(GlobalWheelGesturePolicy.Resolve(settings, false, false, isTray: false));
        Assert.AreEqual(WheelAction.PreviousNext, GlobalWheelGesturePolicy.Resolve(settings, false, false, isTray: true));
    }

    [TestMethod]
    public void ConfiguredChordOverridesPrimaryWheelAction()
    {
        var settings = GlobalInteractionSettings.Default with
        {
            ChordWheelEnabled = true,
            ChordButton = MouseChordButton.Right,
            PrimaryWheelAction = WheelAction.PreviousNext,
            ChordWheelAction = WheelAction.OutputDevice
        };
        Assert.AreEqual(WheelAction.OutputDevice, GlobalWheelGesturePolicy.Resolve(settings, false, true, false));
        Assert.AreEqual(WheelAction.PreviousNext, GlobalWheelGesturePolicy.Resolve(settings, true, false, false));
    }
}
