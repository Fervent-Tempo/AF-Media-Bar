// 行说明末尾的提示图标：文案变化（含运行时切换语言）必须重建 Inlines，否则 Run 里会留下旧语言的文字。
using System.Windows.Documents;
using AFMediaBar.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Wpf.Ui.Controls;

namespace AFMediaBar.Layout.Tests;

[TestClass]
[DoNotParallelize]
public sealed class SettingsDescriptionTextTests
{
    [TestMethod]
    public void RebuildsDescriptionAndHintWhenTextChanges() => SettingsMotionTests.RunSta(() =>
    {
        var text = new SettingsDescriptionText { Description = "说明", Hint = "提示" };

        Assert.AreEqual("说明", DescriptionOf(text));
        Assert.AreEqual("提示", IconOf(text).ToolTip);

        // 切换语言时动态资源逐条写入新值：任何一条变化都要重建 Inlines，而不是保留首次构建时的文字。
        text.Hint = "ヒント";
        Assert.AreEqual("ヒント", IconOf(text).ToolTip);
        Assert.AreEqual("说明", DescriptionOf(text));

        text.Description = "説明";
        Assert.AreEqual("説明", DescriptionOf(text));
        Assert.AreEqual("ヒント", IconOf(text).ToolTip);
    });

    [TestMethod]
    public void KeepsPlainTextWhenNoHintIsSet() => SettingsMotionTests.RunSta(() =>
    {
        var text = new SettingsDescriptionText { Description = "说明" };

        Assert.AreEqual(1, text.Inlines.Count);
        Assert.IsInstanceOfType<Run>(text.Inlines.First());
    });

    private static string DescriptionOf(SettingsDescriptionText text) =>
        ((Run)text.Inlines.First()).Text;

    private static SymbolIcon IconOf(SettingsDescriptionText text) =>
        (SymbolIcon)((InlineUIContainer)text.Inlines.Last()).Child;
}
