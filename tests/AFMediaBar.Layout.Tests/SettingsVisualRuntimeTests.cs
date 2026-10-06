// Loads compiled settings templates and checks runtime enum literals, including symbols that the XAML compiler does not validate.
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AFMediaBar.Components;
using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Wpf.Ui.Markup;
using TextBox = System.Windows.Controls.TextBox;

namespace AFMediaBar.Layout.Tests;

[TestClass]
[DoNotParallelize]
public sealed class SettingsVisualRuntimeTests
{
    [TestMethod]
    public void CompiledCardTemplatesKeepEffectsOffTextAndUseSeparateEffects()
    {
        SettingsMotionTests.RunSta(() =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try
            {
                app.Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Light });
                app.Resources.MergedDictionaries.Add(new ControlsDictionary());
                foreach (var file in new[] { "MotionResources", "SettingsComponentResources" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("/AFMediaBar;component/Resources/" + file + ".xaml", UriKind.Relative)
                    });
                var rows = new[] { new SettingsRow { Title = "First", Content = new TextBox() }, new SettingsRow { Title = "Second" } };
                foreach (var row in rows)
                {
                    row.Style = (Style)app.Resources[typeof(SettingsRow)];
                    row.ApplyTemplate();
                    var surface = (Border)row.Template.FindName("RowSurface", row);
                    Assert.IsNull(surface.Child, "Text and controls must be siblings of the shadow surface.");
                    surface.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                }
                var first = (Border)rows[0].Template.FindName("RowSurface", rows[0]);
                var second = (Border)rows[1].Template.FindName("RowSurface", rows[1]);
                if (MotionPolicy.ResolveCurrent().UseDecorativeEffects)
                    Assert.AreNotSame(first.Effect, second.Effect, "Card effects must never be shared Freezables.");
                foreach (var surface in new[] { first, second })
                {
                    surface.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                    Assert.IsNull(surface.Effect, "Unloading releases the effect and its environment subscriptions.");
                }
                var expander = new CardExpander { Style = (Style)app.Resources["AfSettingsExpanderStyle"], Header = "Details" };
                expander.ApplyTemplate();
                Assert.IsNotNull(expander.Template.FindName("ExpandedContent", expander));
                var choice = new RadioButton { Style = (Style)app.Resources["AfSettingsChoiceRadioStyle"], Content = "Mode" };
                choice.ApplyTemplate();
                Assert.IsNull(((Border)choice.Template.FindName("ChoiceBorder", choice)).Child);
            }
            finally { app.Shutdown(); }
        });
    }

    [TestMethod]
    public void SettingsSymbolsAndDiagramKindsAreValidRuntimeLiterals()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "AFMediaBar"))) root = root.Parent;
        Assert.IsNotNull(root);
        var sources = Directory.EnumerateFiles(Path.Combine(root.FullName, "src", "AFMediaBar", "Views"), "*.xaml", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root.FullName, "src", "AFMediaBar", "Resources"), "Settings*.xaml"));
        var symbolType = typeof(SymbolIcon).GetProperty(nameof(SymbolIcon.Symbol))!.PropertyType;
        foreach (var file in sources)
        {
            var source = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(source, "Symbol=\"([A-Za-z0-9]+)\"|\\{ui:SymbolIcon ([A-Za-z0-9]+)\\}"))
            {
                var literal = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                Assert.IsTrue(Enum.IsDefined(symbolType, Enum.Parse(symbolType, literal)), file + ": " + literal);
            }
            foreach (Match match in Regex.Matches(source, "<components:SettingsDiagram[^>]*Kind=\"([A-Za-z0-9]+)\""))
                Assert.IsTrue(Enum.IsDefined(Enum.Parse<SettingsDiagramKind>(match.Groups[1].Value)), file);
        }
    }
}
