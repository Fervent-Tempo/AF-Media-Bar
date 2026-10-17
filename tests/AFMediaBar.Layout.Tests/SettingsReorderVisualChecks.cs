// 在隔离 WPF 进程中驱动实际排序输入路径，不移动系统鼠标；窗口关闭后检查预览和回调清理。
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Settings;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Services.Media.Smtc;
using AFMediaBar.Classes.Services.Settings;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Components;
using AFMediaBar.ViewModels.Pages;
using AFMediaBar.Views.Pages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

internal static class SettingsReorderVisualChecks
{
    internal static void Verify(Application app)
    {
        var original = SettingsManager.Current;
        using var context = new SettingsPageContext();
        context.Initialize(SettingsContext.Initial);
        using var localization = new LocalizationService();
        SettingsManager.Current = new AppSettings();
        using var model = new LyricsViewModel(localization, new LegacySettingsConfiguration(context));
        var page = new LyricsPage(model);
        var window = SettingsLiveVisualChecks.CreateWindow(page);
        window.Height = 780;
        var writes = 0;
        void Changed(object? sender, SettingsChangedEventArgs args) { if (args.PropertyName == nameof(AppSettings.LyricsSecondaryLine)) writes++; }
        SettingsManager.SettingsChanged += Changed;
        try
        {
            window.Show();
            window.UpdateLayout();
            var expander = Descendants(page).OfType<Wpf.Ui.Controls.CardExpander>().First();
            expander.IsExpanded = true;
            SettingsLiveVisualChecks.Pump(TimeSpan.FromMilliseconds(100));
            var list = Descendants(expander).OfType<ItemsControl>().Single(control => ReferenceEquals(control.ItemsSource, model.SecondaryLineEntries));
            var scroll = (ScrollViewer)page.FindName("PageScroll");
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset + list.TranslatePoint(new Point(), scroll).Y - 120);
            window.UpdateLayout();
            var stateProperty = (DependencyProperty)typeof(SettingsReorderBehavior).GetField("StateProperty", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            var state = list.GetValue(stateProperty)!;
            var initial = model.SecondaryLineEntries.Select(entry => entry.Mode).ToArray();
            var first = (FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(0);
            var last = (FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(2);
            var handle = Descendants(first).OfType<SettingsReorderHandle>().Single();
            var start = new Point(list.ActualWidth - 20, first.TranslatePoint(new Point(), list).Y + first.ActualHeight / 2);
            var finish = new Point(start.X, last.TranslatePoint(new Point(), list).Y + last.ActualHeight / 2 + 2);
            SettingsLiveVisualChecks.CapturePreview(window, "LyricsReorderBefore");
            Call(state, "Start", handle, start);
            Call(state, "MovePointer", start + new Vector(0, 1));
            Assert.AreEqual(1d, first.Opacity, "A small movement must not begin a drag.");
            Call(state, "MovePointer", finish);
            Assert.AreEqual(0d, first.Opacity);
            Assert.AreEqual(0, writes, "Preview must not publish settings.");
            var timer = (System.Windows.Threading.DispatcherTimer)state.GetType().GetField("_scrollTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(state)!;
            Assert.IsFalse(timer.IsEnabled, "A list edge away from the page edge must not start scrolling.");
            Assert.IsNotNull(AdornerLayer.GetAdornerLayer(list).GetAdorners(list));
            SettingsLiveVisualChecks.Pump(TimeSpan.FromMilliseconds(100));
            SettingsLiveVisualChecks.CapturePreview(window, "LyricsReorderDragging");
            Call(state, "Complete", finish, false);
            Assert.AreEqual(1, writes);
            CollectionAssert.AreEqual(initial.Skip(1).Append(initial[0]).ToArray(), model.SecondaryLineEntries.Select(entry => entry.Mode).ToArray());
            Assert.AreEqual(1d, first.Opacity);
            Assert.IsNull(AdornerLayer.GetAdornerLayer(list).GetAdorners(list));
            window.UpdateLayout();
            SettingsLiveVisualChecks.CapturePreview(window, "LyricsReorderAfter");
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AFMB_SETTINGS_PREVIEW_DIR")))
            {
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Dark);
                foreach (var resource in new[] { "AfSettingsCardBrush", "AfSettingsInfoSurfaceBrush", "AfSettingsGroupSurfaceBrush", "AfSettingsSectionBrush" })
                    app.Resources[resource] = new SolidColorBrush(Color.FromRgb(44, 44, 44));
                app.Resources["AfSettingsInputBrush"] = new SolidColorBrush(Color.FromRgb(54, 54, 54));
                app.Resources["AfSettingsBorderBrush"] = new SolidColorBrush(Color.FromRgb(72, 72, 72));
                window.Background = new SolidColorBrush(Color.FromRgb(36, 36, 36));
                SettingsLiveVisualChecks.CapturePreview(window, "LyricsReorderDark");
            }

            first = (FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(0);
            handle = Descendants(first).OfType<SettingsReorderHandle>().Single();
            Call(state, "Start", handle, start);
            Call(state, "MovePointer", finish);
            handle.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(handle), 0, Key.Escape)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Assert.AreEqual(1d, first.Opacity);
            Assert.AreEqual(1, writes);
            Assert.IsFalse(timer.IsEnabled);
            Assert.IsNull(AdornerLayer.GetAdornerLayer(list).GetAdorners(list));

            Call(state, "Start", handle, start);
            Call(state, "MovePointer", finish);
            Call(state, "Complete", new Point(-10, -10), false);
            Assert.AreEqual(1, writes, "Releasing outside must cancel.");
            Call(state, "Start", handle, start);
            Call(state, "MovePointer", finish);
            model.SecondaryLineEntries.Move(0, 1);
            Assert.AreEqual(1d, first.Opacity, "An external collection change must cancel its preview.");
            Call(state, "Complete", finish, false);
            Assert.AreEqual(1, writes);

            window.Height = 480;
            window.UpdateLayout();
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset + list.TranslatePoint(new Point(), scroll).Y - (scroll.ActualHeight - 80));
            window.UpdateLayout();
            first = (FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(0);
            handle = Descendants(first).OfType<SettingsReorderHandle>().Single();
            Call(state, "Start", handle, start);
            Call(state, "MovePointer", start + new Vector(0, 8));
            var viewport = (Rect)state.GetType().GetMethod("ScrollBounds", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(state, null)!;
            Call(state, "MovePointer", new Point(start.X, viewport.Bottom - 2));
            Assert.IsTrue(timer.IsEnabled, "The page edge must start bounded scrolling.");
            Call(state, "Cancel");
            Assert.IsFalse(timer.IsEnabled, "Cancellation must stop edge scrolling.");
            window.Height = 780;
            window.UpdateLayout();
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset + list.TranslatePoint(new Point(), scroll).Y - 120);
            window.UpdateLayout();
            first = (FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(0);
            handle = Descendants(first).OfType<SettingsReorderHandle>().Single();
            var key = handle.ItemKey;
            Call(state, "MoveByKeyboard", handle, 1);
            Assert.AreEqual(2, writes);
            window.UpdateLayout();
            SettingsLiveVisualChecks.Pump(TimeSpan.FromMilliseconds(30));
            Assert.AreEqual(key, model.SecondaryLineEntries[1].Mode);
            Assert.IsFalse(((FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(1)).RenderTransform.HasAnimatedProperties, "Keyboard movement is immediate.");

            first = (FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(0);
            handle = Descendants(first).OfType<SettingsReorderHandle>().Single();
            Call(state, "Start", handle, start);
            Call(state, "MovePointer", finish);
            scroll.ScrollToTop();
            window.UpdateLayout();
            var ordinaryRow = Descendants(page).OfType<SettingsRow>().First(row => !row.IsNested);
            ordinaryRow.ApplyTemplate();
            var tint = (Border)ordinaryRow.Template.FindName("RowHoverTint", ordinaryRow);
            Assert.IsNull(tint.Effect, "Hover tint must not blur text or add another shadow.");
            tint.SetCurrentValue(UIElement.OpacityProperty, 1d);
            SettingsLiveVisualChecks.CapturePreview(window, "CardHoverTintPreview");
            tint.SetCurrentValue(UIElement.OpacityProperty, 0d);
            window.Close();
            Assert.AreEqual(1d, first.Opacity);
            Call(state, "Complete", finish, false);
            SettingsLiveVisualChecks.Pump(TimeSpan.FromMilliseconds(30));
            Assert.AreEqual(2, writes, "Closing the page must invalidate late input.");
        }
        finally
        {
            window.Close();
            SettingsManager.SettingsChanged -= Changed;
            SettingsManager.Current = original;
        }
        VerifyQuickLaunch(localization);
    }

    private static void VerifyQuickLaunch(LocalizationService localization)
    {
        var original = SettingsManager.Current;
        using var builder = new MediaSnapshotBuilder(null!);
        var registry = new MediaSourceRegistry([]);
        var catalog = new MediaSessionCatalog();
        using var selection = new MediaSessionSelectionService(catalog);
        using var media = new MediaSessionService(registry, catalog, selection, builder, [], null!,
            new MemoryPruneCoordinator(null!, null!, null!, []), null!);
        try
        {
            var entries = Enumerable.Range(0, 4).Select(index => new QuickLaunchEntry(index.ToString(), "Player " + index, QuickLaunchTargetKind.Executable, "player" + index + ".exe")).ToArray();
            SettingsManager.Current = new AppSettings { QuickLaunch = new QuickLaunchSettings(entries) };
            var model = new ExtraFeaturesViewModel(registry, media, new NoMonitors(), localization);
            var request = new SettingsReorderRequest(model.QuickLaunchEntries[0], 3, model.QuickLaunchEntries);
            model.ReorderQuickLaunchCommand.Execute(request);
            CollectionAssert.AreEqual(new[] { "1", "2", "3", "0" }, SettingsManager.Current.QuickLaunch.Entries!.Select(entry => entry.Id).ToArray());
            var before = SettingsManager.Current.QuickLaunch;
            model.ReorderQuickLaunchCommand.Execute(request);
            Assert.AreEqual(before, SettingsManager.Current.QuickLaunch);
        }
        finally { SettingsManager.Current = original; }
    }

    private static void Call(object state, string method, params object[] arguments) =>
        state.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(state, arguments);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private sealed class NoMonitors : IDisplayMonitorService
    {
        public event EventHandler? MonitorsChanged { add { } remove { } }
        public IReadOnlyList<DisplayMonitorInfo> GetMonitors() => [];
        public void Refresh() { }
        public DisplayMonitorInfo? ResolveFixedMonitor(string? deviceId) => null;
        public DisplayMonitorInfo? ResolveNotificationMonitor(NotificationTargetMode mode, string? fixedDeviceId) => null;
        public bool IsForegroundWindowFullscreen() => false;
    }
}
