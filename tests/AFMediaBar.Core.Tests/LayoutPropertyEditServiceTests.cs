using AFMediaBar.Layout.Defaults;
using AFMediaBar.Layout.Editing;
using AFMediaBar.Layout.Models;
using AFMediaBar.Layout.Widgets;

namespace AFMediaBar.Core.Tests;

[TestClass]
public sealed class LayoutPropertyEditServiceTests
{
    [TestMethod]
    public void WidgetSettingsUpdateAndResetPreserveSemanticCommand()
    {
        var document = LayoutDefaultTemplates.LoadDocument();
        var profile = document.Horizontal;
        var widget = profile.Containers
            .SelectMany(container => container.PrimarySlot.Children)
            .OfType<LayoutWidgetElement>()
            .First(item => item.TypeId == BuiltInWidgetTypeIds.Command);

        var changed = LayoutPropertyEditService.TryUpdateWidgetSettings(
            profile,
            widget.InstanceId,
            new CommandWidgetSettings(MediaCommandKind.Next, 48),
            out var updated);

        Assert.IsTrue(changed);
        Assert.AreEqual(MediaCommandKind.Next, FindWidget(updated, widget.InstanceId).Settings.As<CommandWidgetSettings>().Command);

        Assert.IsTrue(LayoutPropertyEditService.TryResetWidgetProperties(
            updated,
            widget.InstanceId,
            out var reset));
        Assert.AreEqual(MediaCommandKind.Next, FindWidget(reset, widget.InstanceId).Settings.As<CommandWidgetSettings>().Command);
        Assert.AreEqual(CommandWidgetSettings.DefaultButtonSizeDip,
            FindWidget(reset, widget.InstanceId).Settings.As<CommandWidgetSettings>().ButtonSizeDip);
    }

    private static LayoutWidgetElement FindWidget(LayoutProfile profile, string instanceId) =>
        profile.Containers
            .SelectMany(container => container.PrimarySlot.Children.Concat(container.SecondarySlot.Children))
            .OfType<LayoutWidgetElement>()
            .First(item => item.InstanceId == instanceId);
}

file static class WidgetSettingsExtensions
{
    public static T As<T>(this WidgetSettings settings) where T : WidgetSettings =>
        settings as T ?? throw new AssertFailedException($"Expected {typeof(T).Name}.");
}
