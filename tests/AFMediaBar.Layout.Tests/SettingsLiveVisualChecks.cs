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
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

internal static class SettingsLiveVisualChecks
{
    internal static void VerifyCardHover(Application app)
    {
        var first = new SettingsRow { Style = (Style)app.Resources[typeof(SettingsRow)], Title = "A normal setting", Content = new CheckBox() };
        var second = new SettingsRow { Style = (Style)app.Resources[typeof(SettingsRow)], Title = "Another setting", Content = new CheckBox() };
        var content = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(240, 240, 240)), Padding = new Thickness(24),
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
        Content = content, Width = 720d, Height = 480d, Left = -10000d, Top = -10000d,
        ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None
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
}
