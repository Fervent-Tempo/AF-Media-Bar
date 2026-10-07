// Loads compiled settings templates and checks runtime enum literals, including symbols that the XAML compiler does not validate.
using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using AFMediaBar.Resources;
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
                app.Resources["AfAccentBrush"] = new SolidColorBrush(Color.FromRgb(0, 120, 212));
                app.Resources["AfOnAccentBrush"] = Brushes.White;
                app.Resources["AfAccentTintBrush"] = new SolidColorBrush(Color.FromArgb(25, 0, 120, 212));
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
                choice.IsChecked = true;
                var tint = (Border)choice.Template.FindName("ChoiceTint", choice);
                Assert.AreEqual(0.10d, tint.Opacity);
                Assert.IsFalse(tint.HasAnimatedProperties, "Selection feedback must also work with all system animation disabled.");
                VerifyNumericBinding(app);
                VerifyNarrowRow(app);
                SettingsLiveVisualChecks.VerifyCardHover(app);
                VerifyPageMarkup(app);
                SettingsLiveVisualChecks.VerifyNavigationMotion(app);
            }
            finally { app.Shutdown(); }
        });
    }

    private static void VerifyPageMarkup(Application app)
    {
        foreach (var key in Translations.Keys) app.Resources["Loc." + key] = Translations.Get(key);
        app.Resources["AfBooleanToVisibilityConverter"] = new BooleanToVisibilityConverter();
        app.Resources["BooleanToVisibilityConverter"] = new BooleanToVisibilityConverter();
        app.Resources["AppTextFontFamily"] = new FontFamily("Segoe UI");
        app.Resources["AppTextFontWeight"] = FontWeights.Normal;
        app.Resources["AppTextMediumFontWeight"] = FontWeights.Medium;
        app.Resources["AppTextStrongFontWeight"] = FontWeights.SemiBold;
        var root = FindRepository();
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var eventNames = new HashSet<string> { "Loaded", "Unloaded", "Click", "SelectionChanged", "Checked", "Unchecked", "SizeChanged" };
        foreach (var name in new[] { "DisplayModes", "ScreenAndPlacement", "Appearance", "Interaction", "Lyrics", "ComponentsSettings", "ExtraFeatures", "Application", "ReleaseHighlights", "About" })
        {
            var document = XDocument.Load(Path.Combine(root, "src", "AFMediaBar", "Views", "Pages", name + "Page.xaml"));
            document.Root!.Attribute(x + "Class")!.Remove();
            foreach (var element in document.Root.DescendantsAndSelf())
            {
                foreach (var attribute in element.Attributes().ToArray())
                {
                    if (eventNames.Contains(attribute.Name.LocalName)) attribute.Remove();
                }
            }
            // Business dependencies and event handlers are excluded: this checks markup, styles, and runtime literals.
            var markup = Regex.Replace(document.ToString(), "clr-namespace:AFMediaBar[.A-Za-z]+",
                match => match.Value + ";assembly=AFMediaBar");
            var page = (Page)XamlReader.Parse(markup);
            page.Background = new SolidColorBrush(Color.FromRgb(243, 243, 243));
            foreach (var width in new[] { 344d, 800d })
            {
                page.Measure(new Size(width, 550d));
                page.Arrange(new Rect(0d, 0d, width, 550d));
                page.UpdateLayout();
            }
            var previewDirectory = Environment.GetEnvironmentVariable("AFMB_SETTINGS_PREVIEW_DIRECTORY");
            if (!string.IsNullOrEmpty(previewDirectory) && name is "Appearance" or "Lyrics")
            {
                Directory.CreateDirectory(previewDirectory);
                var bitmap = new RenderTargetBitmap(800, 550, 96d, 96d, PixelFormats.Pbgra32);
                bitmap.Render(page);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.Combine(previewDirectory, name + ".png"));
                encoder.Save(output);
            }
        }
    }

    private static void VerifyNarrowRow(Application app)
    {
        var row = new SettingsRow
        {
            Style = (Style)app.Resources[typeof(SettingsRow)],
            Title = "A setting with an exact numeric value",
            Content = new SettingsNumericEditor
            {
                Style = (Style)app.Resources[typeof(SettingsNumericEditor)],
                Value = 200d,
                Maximum = 4096d,
                Unit = "DIP"
            }
        };
        row.ApplyTemplate();
        var controls = (ContentPresenter)row.Template.FindName("ControlHost", row);
        row.Measure(new Size(344d, 200d));
        row.Arrange(new Rect(0d, 0d, 344d, 200d));
        row.UpdateLayout();
        Assert.IsTrue(row.IsCompact);
        Assert.AreEqual(1, Grid.GetRow(controls), "A narrow row must place controls below its title.");
        Assert.IsTrue(controls.TranslatePoint(new Point(controls.ActualWidth, 0d), row).X <= row.ActualWidth,
            "The numeric editor must fit beside an expanded sidebar.");
        row.Measure(new Size(800d, 200d));
        row.Arrange(new Rect(0d, 0d, 800d, 200d));
        row.UpdateLayout();
        Assert.IsFalse(row.IsCompact);
        Assert.AreEqual(0, Grid.GetRow(controls));
    }

    private static string FindRepository()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "AFMediaBar"))) root = root.Parent;
        return root?.FullName ?? throw new DirectoryNotFoundException("Settings markup was not found.");
    }

    private static void VerifyNumericBinding(Application app)
    {
        var fixture = new NumericFixture();
        var editor = new SettingsNumericEditor
        {
            Style = (Style)app.Resources[typeof(SettingsNumericEditor)],
            Minimum = 129.25d,
            Maximum = 4096d,
            Step = 1d,
            IsSnapToTickEnabled = false
        };
        BindingOperations.SetBinding(editor, SettingsNumericEditor.ValueProperty,
            new Binding(nameof(NumericFixture.Value)) { Source = fixture, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        editor.ApplyTemplate();
        var input = (TextBox)editor.Template.FindName("PART_Input", editor);
        Assert.AreEqual(0, fixture.Writes, "Applying a template must not write a coerced slider default.");
        input.Text = "200";
        Assert.AreEqual(300d, fixture.Value, "An unfinished draft must not change settings.");
        Assert.IsTrue(editor.TryCommit());
        Assert.AreEqual(200d, fixture.Value);
        Assert.AreEqual(1, fixture.Writes);
        Assert.AreEqual("200", input.Text);
        input.Text = "NaN";
        Assert.IsFalse(editor.TryCommit());
        Assert.IsTrue(editor.HasInputError);
        Assert.AreEqual(200d, fixture.Value);
        Assert.AreEqual(1, fixture.Writes);
        input.Text = "200";
        editor.Maximum = 180d;
        Assert.IsFalse(editor.TryCommit(), "A stale draft must use the latest taskbar limit.");
        Assert.AreEqual(1, fixture.Writes);
        fixture.Value = 160d;
        Assert.AreEqual(160d, editor.Value);
        Assert.AreEqual("160", input.Text, "External resets refresh the displayed value.");
        Assert.IsFalse(editor.HasInputError, "An external reset also clears stale validation feedback.");
    }

    private sealed class NumericFixture : INotifyPropertyChanged
    {
        private double _value = 300d;
        public int Writes { get; private set; }
        public double Value
        {
            get => _value;
            set { _value = value; Writes++; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
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
