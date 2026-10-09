// 外观批量更新只同步绑定，不能再次发布半更新的配置；使用真实配置适配器并恢复静态设置。
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.Classes.Settings;
using AFMediaBar.ViewModels.Pages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>验证批量外观同步不会回写中间配置。</summary>
[TestClass]
[DoNotParallelize]
public sealed class AppearanceSynchronizationTests
{
    [TestMethod]
    public void ReplacingAppearanceDoesNotRepublishPartiallyRefreshedValues()
    {
        StaTest.Run(_ =>
        {
            var original = SettingsManager.Current;
            using var localization = new LocalizationService();
            using var context = new SettingsPageContext();
            var writes = 0;
            EventHandler<SettingsChangedEventArgs> changed = (_, args) => { if (args.PropertyName == nameof(AppSettings.Appearance)) writes++; };
            try
            {
                SettingsManager.Current = new AppSettings();
                using var viewModel = new AppearanceViewModel(localization, new LegacySettingsConfiguration(context));
                SettingsManager.SettingsChanged += changed;
                var appearance = SettingsManager.Current.Appearance with
                {
                    BackdropMode = ApplicationBackdropMode.Acrylic,
                    AccentColorMode = AccentColorMode.Custom,
                    AccentColor = "#7C3AED",
                    BackdropTintOpacityPercent = 75
                };
                SettingsManager.SetAppearanceSettings(appearance);
                Assert.AreEqual(1, writes, "Refreshing bound properties must not write additional configurations.");
                Assert.AreEqual(appearance.Normalize(), SettingsManager.Current.Appearance);
                Assert.AreEqual(75, viewModel.BackdropTintOpacityPercent);
                Assert.AreEqual("#7C3AED", viewModel.AccentColorHex);
            }
            finally { SettingsManager.SettingsChanged -= changed; SettingsManager.Current = original; }
            return Task.CompletedTask;
        });
    }
}
