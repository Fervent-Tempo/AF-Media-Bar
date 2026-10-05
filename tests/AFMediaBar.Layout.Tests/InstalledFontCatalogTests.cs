// 验证离页取消不会终止共享字体扫描，以及扫描前的选择和语言刷新保持可用。
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Data;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Resources;
using AFMediaBar.ViewModels.Pages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>字体目录异步等待及预览回退的回归验证。</summary>
[TestClass]
[DoNotParallelize]
public sealed class InstalledFontCatalogTests
{
    [TestMethod]
    public async Task CancelledNavigationDoesNotCancelAnotherWaiterOrSharedScan()
    {
        var field = typeof(InstalledFontCatalog).GetField("_inFlightRefresh", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = field.GetValue(null);
        var scan = new TaskCompletionSource<(IReadOnlyList<FontFamilyChoice> Latin, IReadOnlyList<FontFamilyChoice> Cjk)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        field.SetValue(null, scan.Task);
        try
        {
            var cancelledWait = InstalledFontCatalog.RefreshChoicesAsync(cancellation.Token);
            var activeWait = InstalledFontCatalog.RefreshChoicesAsync(CancellationToken.None);
            cancellation.Cancel();
            await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => cancelledWait);
            Assert.IsFalse(scan.Task.IsCompleted);
            Assert.IsFalse(activeWait.IsCompleted);

            IReadOnlyList<FontFamilyChoice> latin = [new("Segoe UI", "Segoe UI")];
            IReadOnlyList<FontFamilyChoice> cjk = [new("Microsoft YaHei", "微软雅黑")];
            scan.SetResult((latin, cjk));
            var result = await activeWait.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreSame(latin, result.Latin);
            Assert.AreSame(cjk, result.Cjk);
        }
        finally
        {
            scan.TrySetResult(([], []));
            field.SetValue(null, original);
        }
    }

    [TestMethod]
    public void SavedFontRemainsSelectableBeforeInstalledScanCompletes()
    {
        StaTest.Run(_ =>
        {
            var choices = InstalledFontCatalog.GetInitialChoices("Appearance.LatinFont.FollowSystem", cjk: false, "Segoe UI, Arial");
            Assert.AreEqual(string.Empty, choices[0].Name);
            Assert.AreEqual("Segoe UI", InstalledFontCatalog.MatchSelection("Segoe UI, Arial", choices));
            Assert.AreEqual("Segoe UI", choices[1].PreviewFontFamily?.Source);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void RelocalizingUpdatesExistingObjectsWithoutReplacingTheSelection()
    {
        IReadOnlyList<FontFamilyChoice> choices = [new("", "old system", "old sample"), new("Example", "Example", "old sample")];
        var changed = new List<string?>();
        choices[1].PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var localized = InstalledFontCatalog.RelocalizeChoices(choices, "Appearance.CjkFont.FollowSystem", cjk: true);
        Assert.AreSame(choices, localized);
        Assert.AreSame(choices[1], localized[1]);
        Assert.AreEqual(Translations.Get("Appearance.CjkFont.FollowSystem"), localized[0].DisplayName);
        Assert.AreEqual("Example", localized[1].Name);
        Assert.AreEqual("Example", localized[1].DisplayName);
        Assert.AreEqual(Translations.Get("Appearance.CjkFont.Sample"), localized[1].PreviewText);
        CollectionAssert.Contains(changed, nameof(FontFamilyChoice.PreviewText));
    }

    [TestMethod]
    public void LanguageChangeRetainsSelectedFontObjectsInBoundSelectors()
    {
        StaTest.Run(_ =>
        {
            var originalSettings = SettingsManager.Current;
            var originalLanguage = Translations.ActiveLanguage;
            using var localization = new LocalizationService();
            AppearanceViewModel? viewModel = null;
            try
            {
                SettingsManager.Current = new AppSettings();
                localization.Apply(InterfaceLanguage.SimplifiedChinese);
                viewModel = new AppearanceViewModel(localization, new TaskbarLengthConstraintsService());
                var latin = CreateSelector(viewModel, nameof(AppearanceViewModel.LatinFontChoices), nameof(AppearanceViewModel.LatinFontFamily));
                var cjk = CreateSelector(viewModel, nameof(AppearanceViewModel.CjkFontChoices), nameof(AppearanceViewModel.CjkFontFamily));
                Assert.IsNotNull(latin.SelectedItem);
                Assert.IsNotNull(cjk.SelectedItem);

                localization.Apply(InterfaceLanguage.English);
                Assert.AreSame(viewModel.LatinFontChoices[0], latin.SelectedItem);
                Assert.AreSame(viewModel.CjkFontChoices[0], cjk.SelectedItem);
                Assert.IsTrue(latin.SelectedIndex >= 0);
                Assert.IsTrue(cjk.SelectedIndex >= 0);
                Assert.AreEqual(Translations.Get("Appearance.LatinFont.FollowSystem"), ((FontFamilyChoice)latin.SelectedItem).DisplayName);
                Assert.AreEqual(Translations.Get("Appearance.CjkFont.Sample"), ((FontFamilyChoice)cjk.SelectedItem).PreviewText);
                return Task.CompletedTask;
            }
            finally
            {
                // 生产 ViewModel 与进程同寿命；测试需移除它对全局设置的订阅。
                if (viewModel is not null)
                {
                    var handlers = (EventHandler<SettingsChangedEventArgs>?)typeof(SettingsManager)
                        .GetField("SettingsChanged", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
                    foreach (var handler in handlers?.GetInvocationList() ?? [])
                        if (ReferenceEquals(handler.Target, viewModel)) SettingsManager.SettingsChanged -= (EventHandler<SettingsChangedEventArgs>)handler;
                }
                SettingsManager.Current = originalSettings;
                localization.Apply(originalLanguage switch
                {
                    LocalizationLanguage.English => InterfaceLanguage.English,
                    LocalizationLanguage.TraditionalChinese => InterfaceLanguage.TraditionalChinese,
                    _ => InterfaceLanguage.SimplifiedChinese
                });
            }
        });
    }

    private static ComboBox CreateSelector(AppearanceViewModel viewModel, string choices, string selection)
    {
        var combo = new ComboBox { DataContext = viewModel, SelectedValuePath = nameof(FontFamilyChoice.Name) };
        combo.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(choices));
        combo.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedValueProperty, new Binding(selection) { Mode = BindingMode.TwoWay });
        return combo;
    }

    [TestMethod]
    public void InstalledRefreshReusesTheSelectedProvisionalFont()
    {
        StaTest.Run(_ =>
        {
            var initial = InstalledFontCatalog.GetInitialChoices("Appearance.CjkFont.FollowSystem", true, "Microsoft YaHei");
            var combo = new ComboBox { ItemsSource = initial, SelectedValuePath = nameof(FontFamilyChoice.Name), SelectedValue = "Microsoft YaHei" };
            var selected = combo.SelectedItem;
            IReadOnlyList<FontFamilyChoice> scanned = [new("", "system"), new("Arial", "Arial"), new("Microsoft YaHei", "微软雅黑")];
            combo.ItemsSource = InstalledFontCatalog.RelocalizeChoices(scanned, "Appearance.CjkFont.FollowSystem", true, initial);
            Assert.AreSame(selected, combo.SelectedItem);
            Assert.AreEqual(2, combo.SelectedIndex);
            Assert.AreEqual("微软雅黑", ((FontFamilyChoice)combo.SelectedItem).DisplayName);
            return Task.CompletedTask;
        });
    }
}
