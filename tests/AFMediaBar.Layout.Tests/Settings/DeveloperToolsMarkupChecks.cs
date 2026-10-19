// 在已有隔离 WPF 测试进程加载工具窗口；平台操作替身不发送通知、不读取播放器或重启 AF。
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Xml.Linq;
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.ViewModels.Windows;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

internal static class DeveloperToolsMarkupChecks
{
    internal static void Verify(Application app, string repository)
    {
        var document = XDocument.Load(Path.Combine(repository, "src", "AFMediaBar", "Views", "Windows", "DeveloperToolsWindow.xaml"));
        document.Root!.Attribute(XName.Get("Class", "http://schemas.microsoft.com/winfx/2006/xaml"))!.Remove();
        using var localization = new LocalizationService();
        using var viewModel = new DeveloperToolsViewModel(new Mode(), new Scenarios(), localization);
        var window = (Window)XamlReader.Parse(document.ToString());
        using var messages = new StringWriter();
        using var listener = new TextWriterTraceListener(messages);
        var trace = PresentationTraceSources.DataBindingSource;
        var previous = trace.Switch.Level;
        trace.Switch.Level = SourceLevels.Error;
        trace.Listeners.Add(listener);
        try
        {
            window.DataContext = viewModel;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -20000; window.Top = -20000;
            window.ShowInTaskbar = false; window.Opacity = 0;
            window.Show();
            var tabs = Find<TabControl>((DependencyObject)window.Content).Single();
            foreach (var index in new[] { 0, 1, 2 })
            {
                tabs.SelectedIndex = index;
                window.UpdateLayout();
            }
            Assert.IsTrue(viewModel.Actions.Where(item => item.Action.RequiresRenderer).All(item => !item.IsAvailable));
            listener.Flush();
            Assert.AreEqual(string.Empty, messages.ToString(), "Tool window binding errors");
            Assert.IsTrue(app.Resources.Contains("Loc.Developer.Window.Title"));
        }
        finally
        {
            window.Close();
            trace.Listeners.Remove(listener);
            trace.Switch.Level = previous;
        }
    }

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in Find<T>(child)) yield return descendant;
    }

    private sealed class Mode : IDeveloperModeService
    {
        public bool IsEnabled => true;
        public int Generation => 0;
        public CancellationToken SessionToken => CancellationToken.None;
        public event EventHandler? EnabledChanged { add { } remove { } }
        public event EventHandler? OpenRequested { add { } remove { } }
        public void SetEnabled(bool enabled) { }
        public void OpenTools() { }
    }

    private sealed class Scenarios : IDeveloperScenarioService
    {
        public event Action<string, DeveloperActionResult>? ResultObserved { add { } remove { } }
        public IReadOnlyList<DeveloperLyricsHostState> GetLyricsHosts() => [];
        public Task<DeveloperActionResult> ExecuteAsync(string command, string? hostId, CancellationToken cancellationToken) =>
            Task.FromResult(new DeveloperActionResult(DeveloperActionStatus.Unavailable));
        public void ClosePreviews() { }
    }
}
