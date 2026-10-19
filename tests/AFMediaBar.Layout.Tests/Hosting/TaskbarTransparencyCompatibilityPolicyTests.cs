using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class TaskbarTransparencyCompatibilityPolicyTests
{
    [TestMethod]
    public void FirstDetectionEnablesMaterialAndShowsPrompt()
    {
        var decision = TaskbarTransparencyCompatibilityPolicy.Resolve(
            promptShown: false,
            translucentTbRunning: true,
            TaskbarBackgroundMaterial.Transparent);

        Assert.IsTrue(decision.EnableFrostedBackground);
        Assert.IsTrue(decision.ShowPrompt);
    }

    [TestMethod]
    public void ExistingMaterialStillExplainsAutomaticCompatibility()
    {
        var decision = TaskbarTransparencyCompatibilityPolicy.Resolve(
            promptShown: false,
            translucentTbRunning: true,
            TaskbarBackgroundMaterial.Frosted);

        Assert.IsFalse(decision.EnableFrostedBackground);
        Assert.IsTrue(decision.ShowPrompt);
    }

    [TestMethod]
    public void MissingProcessDoesNothingAndCanBeDetectedOnLaterStart()
    {
        var decision = TaskbarTransparencyCompatibilityPolicy.Resolve(
            promptShown: false,
            translucentTbRunning: false,
            TaskbarBackgroundMaterial.Transparent);

        Assert.IsFalse(decision.EnableFrostedBackground);
        Assert.IsFalse(decision.ShowPrompt);
    }

    [TestMethod]
    public void AcknowledgedDetectionNeverOverridesUsersLaterChoice()
    {
        var decision = TaskbarTransparencyCompatibilityPolicy.Resolve(
            promptShown: true,
            translucentTbRunning: true,
            TaskbarBackgroundMaterial.Transparent);

        Assert.IsFalse(decision.EnableFrostedBackground);
        Assert.IsFalse(decision.ShowPrompt);
    }
}
