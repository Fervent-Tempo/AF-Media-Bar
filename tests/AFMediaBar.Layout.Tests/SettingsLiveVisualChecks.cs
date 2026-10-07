// Exercises loaded WPF visuals in an offscreen window and captures real animation frames without starting application services.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AFMediaBar.Classes.Services;
using AFMediaBar.Components;
using AFMediaBar.Classes.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Animations;
using NavigationView = Wpf.Ui.Controls.NavigationView;
using NavigationViewItem = Wpf.Ui.Controls.NavigationViewItem;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;

namespace AFMediaBar.Layout.Tests;

internal static class SettingsLiveVisualChecks
{
    internal static void VerifyNavigationMotion(Application app)
    {
        var provider = new SceneProvider();
        var navigation = new NavigationView { Transition = Transition.None, IsPaneVisible = true, IsPaneOpen = true };
        navigation.MenuItems.Add(new NavigationViewItem { Content = "First", Icon = new SymbolIcon { Symbol = SymbolRegular.Desktop24 }, TargetPageType = typeof(FirstScene) });
        navigation.MenuItems.Add(new NavigationViewItem { Content = "Second", Icon = new SymbolIcon { Symbol = SymbolRegular.Settings24 }, TargetPageType = typeof(SecondScene) });
        navigation.FooterMenuItems.Add(new NavigationViewItem { Content = "About", Icon = new SymbolIcon { Symbol = SymbolRegular.Info24 }, TargetPageType = typeof(FooterScene) });
        navigation.SetPageProviderService(provider);
        Panel? arrived = null;
        navigation.Navigated += (_, args) =>
        {
            Assert.IsInstanceOfType<Page>(args.Page, "The real NavigationView must supply the destination page instance.");
            var page = (Page)args.Page;
            SettingsRevealAnimator.Cancel(arrived);
            arrived = (Panel)((ScrollViewer)page.FindName("PageScroll")).Content;
            SettingsRevealAnimator.Replay(arrived);
        };
        var overlay = new Canvas { IsHitTestVisible = false };
        var shell = new Grid { Background = new SolidColorBrush(Color.FromRgb(243, 243, 243)), Children = { navigation, overlay } };
        using var selection = new SettingsSidebarSelectionAnimator(navigation, overlay);
        var window = CreateWindow(shell);
        try
        {
            window.Show();
            Pump(TimeSpan.FromMilliseconds(60));
            Assert.IsTrue(navigation.Navigate(typeof(FirstScene)));
            Pump(TimeSpan.FromMilliseconds(400));
            var indicator = (Border)overlay.Children[0];
            Assert.AreEqual(Visibility.Visible, indicator.Visibility);
            var indicatorTransform = (TransformGroup)indicator.RenderTransform;
            var indicatorPosition = indicatorTransform.Children.OfType<TranslateTransform>().Single();
            var startY = indicatorPosition.Y;
            var seenEntrance = false;
            var seenSelectionMove = false;
            EventHandler sample = (_, _) =>
            {
                if (arrived?.RenderTransform is not TransformGroup transform) return;
                var offset = transform.Children.OfType<TranslateTransform>().LastOrDefault();
                if (arrived.Opacity > 0d && arrived.Opacity < 1d && offset?.Y > 1d) seenEntrance = true;
                if (indicatorPosition.HasAnimatedProperties && Math.Abs(indicatorPosition.Y - startY) > 0.1d) seenSelectionMove = true;
            };
            CompositionTarget.Rendering += sample;
            try
            {
                Assert.IsTrue(navigation.Navigate(typeof(SecondScene)));
                Assert.IsNotNull(arrived);
                Pump(TimeSpan.FromMilliseconds(360));
                if (MotionPolicy.ResolveCurrent().Mode == MotionMode.Full)
                {
                    Assert.IsTrue(seenEntrance, "Actual navigation must produce a rendered intermediate frame with visible motion and opacity.");
                    Assert.IsTrue(seenSelectionMove, "The shared sidebar selection surface must move between items.");
                }
                var selected = (NavigationViewItem)navigation.SelectedItem!;
                var expected = selected.TranslatePoint(new Point(2d, 2d), overlay);
                Assert.AreEqual(expected.Y, indicatorPosition.Y, 0.5d);
                Assert.AreEqual(FontWeights.SemiBold, selected.FontWeight);
                Assert.AreSame(selected.Foreground, selected.Icon!.Foreground, "The selected icon and label must use the same live accent brush.");
                var inactive = (NavigationViewItem)navigation.MenuItems[0]!;
                Assert.AreSame(inactive.Foreground, inactive.Icon!.Foreground, "An inactive icon must return to the normal foreground.");
                Assert.IsFalse(indicatorPosition.HasAnimatedProperties);
                Save(Capture(shell), "sidebar-selection");
                Assert.AreEqual(1d, arrived.Opacity);
                Assert.IsFalse(arrived.HasAnimatedProperties);
                // Replace a page before its entrance finishes, then close during the next entrance.
                navigation.Navigate(typeof(FirstScene));
                Pump(TimeSpan.FromMilliseconds(25));
                navigation.Navigate(typeof(SecondScene));
                Pump(TimeSpan.FromMilliseconds(25));
                SettingsRevealAnimator.Cancel(arrived);
                Assert.AreEqual(1d, arrived.Opacity);
                Assert.IsFalse(arrived.HasAnimatedProperties);
            }
            finally { CompositionTarget.Rendering -= sample; }
            navigation.Navigate(typeof(FooterScene));
            Pump(TimeSpan.FromMilliseconds(360));
            var footer = (NavigationViewItem)navigation.SelectedItem!;
            var footerOrigin = footer.TranslatePoint(new Point(2d, 2d), overlay);
            Assert.AreEqual(footerOrigin.Y, indicatorPosition.Y, 0.5d, "Footer destinations use the same moving selection surface.");
            navigation.IsPaneOpen = false;
            Pump(TimeSpan.FromMilliseconds(100));
            Assert.IsFalse(indicatorPosition.HasAnimatedProperties, "Pane changes must realign without replaying selection.");
            selection.Dispose();
            Assert.AreEqual(0, overlay.Children.Count, "Disposal removes the shared selection surface.");
        }
        finally { SettingsRevealAnimator.Cancel(arrived); window.Close(); Pump(TimeSpan.FromMilliseconds(30)); }
    }


    internal static void VerifyCardHover(Application app)
    {
        var first = new SettingsRow { Style = (Style)app.Resources[typeof(SettingsRow)], Title = "A normal setting", Content = new CheckBox() };
        var second = new SettingsRow { Style = (Style)app.Resources[typeof(SettingsRow)], Title = "Another setting", Content = new CheckBox() };
        var content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(240, 240, 240)),
            Padding = new Thickness(24),
            Child = new StackPanel { Children = { first, second } }
        };
        var window = CreateWindow(content);
        try
        {
            window.Show();
            Pump(TimeSpan.FromMilliseconds(80));
            Assert.IsTrue(first.IsLoaded);
            var hit = first.InputHitTest(new Point(first.ActualWidth / 2d, first.ActualHeight / 2d)) as DependencyObject;
            Assert.IsNotNull(hit, "Empty space inside a normal card must accept pointer input.");
            Assert.IsTrue(ReferenceEquals(hit, first) || first.IsAncestorOf(hit));
            var position = first.TranslatePoint(new Point(), content);
            var size = first.RenderSize;
            var before = Capture(content);
            Save(before, "card-rest");
            first.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseEnterEvent });
            Pump(TimeSpan.FromMilliseconds(80));
            var surface = (Border)first.Template.FindName("RowSurface", first);
            if (MotionPolicy.ResolveCurrent().UseDecorativeEffects)
            {
                var shadow = (DropShadowEffect)surface.Effect;
                Assert.IsTrue(shadow.Opacity > 0.10d, "A normal card must have a visible hover shadow during its transition.");
                Assert.IsTrue(shadow.ShadowDepth > 2d);
                Assert.AreEqual(1, Panel.GetZIndex(first), "A following card must not cover the hovered shadow.");
                Assert.IsTrue(ChangedPixels(before, Capture(content)) > 40, "The rendered image must change, not just effect parameters.");
                Save(Capture(content), "card-hover");
            }
            Assert.AreEqual(position, first.TranslatePoint(new Point(), content), "Hover must not move the card.");
            Assert.AreEqual(size, first.RenderSize, "Hover must not change the card's layout.");
            first.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseLeaveEvent });
            Pump(TimeSpan.FromMilliseconds(160));
            Assert.AreEqual(0, Panel.GetZIndex(first));
            if (surface.Effect is DropShadowEffect settled) Assert.AreEqual(0d, settled.Opacity, 0.001d);
            window.Close();
            Pump(TimeSpan.FromMilliseconds(30));
            Assert.IsNull(surface.Effect, "Closing the window must release its shadow and subscriptions.");
        }
        finally { window.Close(); }
    }

    internal static Window CreateWindow(UIElement content) => new()
    {
        Content = content,
        Width = 720d,
        Height = 480d,
        Left = -10000d,
        Top = -10000d,
        ShowInTaskbar = false,
        ShowActivated = false,
        WindowStyle = WindowStyle.None
    };

    internal static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    internal static RenderTargetBitmap Capture(FrameworkElement element)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight),
            96d, 96d, PixelFormats.Pbgra32);
        bitmap.Render(element);
        bitmap.Freeze();
        return bitmap;
    }

    internal static void Save(BitmapSource bitmap, string name)
    {
        var directory = Environment.GetEnvironmentVariable("AFMB_SETTINGS_PREVIEW_DIRECTORY");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(output);
    }

    private static int ChangedPixels(BitmapSource first, BitmapSource second)
    {
        var stride = first.PixelWidth * 4;
        var before = new byte[stride * first.PixelHeight];
        var after = new byte[before.Length];
        first.CopyPixels(before, stride, 0);
        second.CopyPixels(after, stride, 0);
        var changed = 0;
        for (var pixel = 0; pixel < before.Length; pixel += 4)
            if (before[pixel] != after[pixel] || before[pixel + 1] != after[pixel + 1] || before[pixel + 2] != after[pixel + 2]) changed++;
        return changed;
    }

    private sealed class SceneProvider : INavigationViewPageProvider
    {
        private readonly FirstScene _first = new();
        private readonly SecondScene _second = new();
        private readonly FooterScene _footer = new();
        public object GetPage(Type pageType) => pageType == typeof(FirstScene) ? _first : pageType == typeof(SecondScene) ? _second : _footer;
    }

    private class Scene : Page
    {
        protected Scene(string title, Brush background)
        {
            NameScope.SetNameScope(this, new NameScope());
            var content = new StackPanel { Background = background };
            content.Children.Add(new TextBlock { Text = title, FontSize = 28d, Margin = new Thickness(28d) });
            for (var index = 0; index < 3; index++)
                content.Children.Add(new SettingsRow { Title = "Setting " + index, Content = new CheckBox() });
            var scroll = new ScrollViewer { Name = "PageScroll", Content = content };
            RegisterName("PageScroll", scroll);
            Content = scroll;
        }
    }

    private sealed class FirstScene() : Scene("First page", Brushes.LightGray);
    private sealed class SecondScene() : Scene("Second page", Brushes.AliceBlue);
    private sealed class FooterScene() : Scene("Footer page", Brushes.WhiteSmoke);
}
