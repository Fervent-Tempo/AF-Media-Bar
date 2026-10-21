// Covers exact input, domain precision, locale parsing, and invalid drafts that must never overwrite saved settings.
using System.Globalization;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class SettingsNumericInputTests
{
    [DataTestMethod]
    [DataRow("450", "en-US", 100d, 900d, 100d, 500d)]
    [DataRow("1.3", "en-US", 0.5d, 5d, 0.5d, 1.5d)]
    [DataRow("1,5", "fr-FR", 0.5d, 5d, 0.5d, 1.5d)]
    public void ValidInputUsesTheSettingsPrecision(string text, string culture, double minimum, double maximum, double step, double expected)
    {
        Assert.IsTrue(SettingsNumericInputPolicy.TryResolve(text, CultureInfo.GetCultureInfo(culture), minimum, maximum, step, out var value));
        Assert.AreEqual(expected, value, 1e-8d);
    }

    [DataTestMethod]
    [DataRow("NaN")]
    [DataRow("2000")]
    [DataRow("hello")]
    public void InvalidInputIsRejectedBeforeItCanReachSettings(string text) =>
        Assert.IsFalse(SettingsNumericInputPolicy.TryResolve(text, CultureInfo.GetCultureInfo("en-US"), 0d, 1000d, 1d, out _));

    [TestMethod]
    public void DynamicBoundsAndInvalidPrecisionCannotProduceUnsafeValues()
    {
        Assert.IsFalse(SettingsNumericInputPolicy.TryResolve("200", CultureInfo.InvariantCulture, 300d, 100d, 1d, out _));
        Assert.IsFalse(SettingsNumericInputPolicy.TryResolve("200", CultureInfo.InvariantCulture, 0d, double.NaN, 1d, out _));
        Assert.IsFalse(SettingsNumericInputPolicy.TryResolve("200", CultureInfo.InvariantCulture, 0d, 1000d, 0d, out _));
        Assert.IsFalse(SettingsNumericInputPolicy.TryResolve("200", CultureInfo.InvariantCulture, 0d, 180d, 1d, out _));
    }
}
