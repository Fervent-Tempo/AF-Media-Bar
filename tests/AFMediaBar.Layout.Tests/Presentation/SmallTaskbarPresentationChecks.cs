// 实际控件验证副信息恢复、有效栏长和保存偏好隔离，不将渲染截图当成原生验收。
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using AFMediaBar.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

internal static class SmallTaskbarPresentationChecks
{
    internal static void Verify(TaskBarMediaControl control)
    {
        var previous = SettingsManager.Current;
        try
        {
            SettingsManager.Current = new AppSettings
            {
                TwoLineLyricsEnabled = true,
                LyricsSyllableHighlightEnabled = true,
                TaskbarExperience = new()
                {
                    MediaFontSizePercent = 100,
                    ContentLayout = TaskbarContentLayout.CompactInline,
                    HoverControls = TaskbarHoverControlsSettings.Default with { PlayPauseVisible = true, PreviousNextVisible = true },
                    LengthMode = TaskbarLengthMode.FollowContent
                }
            };
            var snapshot = MediaSnapshot.Disconnected with { IsConnected = true, CanPlayPause = true, CanSkipPrevious = true, CanSkipNext = true, Title = "主标题 Mixed 🎵", Artist = "副信息 Artist" };
            control.UpdateSongInfo(snapshot);
            control.ApplyLayout(WindowMode.Taskbar, LayoutOrientation.Horizontal, 100, 100, 24);
            control.ApplyPrimaryLength(500);
            var title = (TextBlock)control.FindName("SongTitle");
            var artist = (FrameworkElement)control.FindName("SongArtistContainer");
            Assert.AreEqual(24d, control.CurrentLayout!.Canvas.Height, .01);
            Assert.AreEqual(14d, title.FontSize, "Actual thickness must not shrink the saved media font twice.");
            Assert.AreEqual(snapshot.Title, title.Text);
            Assert.AreEqual(Visibility.Collapsed, artist.Visibility);
            Assert.IsTrue(control.HasAutomaticallyHiddenInformation);
            control.UpdateSongInfo(snapshot with { Artist = string.Empty });
            Assert.IsFalse(control.HasAutomaticallyHiddenInformation, "No secondary information means no notification.");
            control.UpdateSongInfo(snapshot);
            SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with { SpectrumVisible = true, PerformanceVisible = true };
            SettingsManager.Current.PerformanceComponent = new([MetricKind.SystemNetwork], 500, false);
            SettingsManager.Current.SpectrumComponent = SettingsManager.Current.SpectrumComponent with { ContentHeightDip = 44 };
            control.ApplyTaskbarExperienceSettings();
            control.ApplySpectrum([.2f, .6f, .9f]);
            var spectrum = (FrameworkElement)control.FindName("TaskbarSpectrum");
            var transportButton = (FrameworkElement)control.FindName("TaskbarPlayPauseButton");
            Assert.IsTrue(spectrum.Height <= 20);
            Assert.AreEqual(Visibility.Visible, transportButton.Visibility);
            Assert.IsTrue(transportButton.Height <= 22);
            var metric = ((StackPanel)control.FindName("TaskbarPerformanceItems")).Children.OfType<TextBlock>().Single();
            Assert.IsFalse(metric.Text.Contains('\n'), "A narrow horizontal taskbar keeps network values on one row.");
            var saved = SettingsManager.Current.Clone();
            control.ApplyLayout(WindowMode.Taskbar, LayoutOrientation.Horizontal, 100, 100);
            control.ApplyPrimaryLength(500);
            Assert.IsTrue(title.Text.Contains(snapshot.Artist));
            Assert.IsFalse(control.HasAutomaticallyHiddenInformation);

            SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with { MediaFontSizePercent = 125, ContentLayout = TaskbarContentLayout.AdaptiveStack };
            control.ApplyLayout(WindowMode.Taskbar, LayoutOrientation.Vertical, 100, 100, 32);
            control.ApplyPrimaryLength(600);
            Assert.AreEqual(22.5, title.FontSize, .01);
            Assert.AreEqual(Visibility.Collapsed, artist.Visibility);
            control.ApplyLayout(WindowMode.Taskbar, LayoutOrientation.Vertical, 100, 100, 64);
            control.ApplyPrimaryLength(600);
            Assert.AreEqual(Visibility.Visible, artist.Visibility);

            // 强制相同当前行、不同下一行；小任务栏栏长不得再被隐藏副信息撑长。
            SettingsManager.Current.TaskbarExperience = SettingsManager.Current.TaskbarExperience with { MediaFontSizePercent = 100 };
            control.ApplyLayout(WindowMode.Taskbar, LayoutOrientation.Horizontal, 100, 100, 24);
            var raw = new LyricsPresentationFrame(true, "track", 0, "Current", new string('W', 180), "", "", false, .2, .3, null, false);
            var field = MediaBarTestAccess.GetField("_lyricsFrame", BindingFlags.Instance | BindingFlags.NonPublic)!;
            field.SetValue(control, raw);
            var refresh = MediaBarTestAccess.GetMethod("RefreshTaskbarTextBudget", BindingFlags.Instance | BindingFlags.NonPublic)!;
            refresh.Invoke(control, [raw]);
            MediaBarSizeRequest? request = null;
            void OnSize(object? sender, MediaBarSizeRequestEventArgs args) => request = args.Request;
            control.DesiredSizeChanged += OnSize;
            try
            {
                control.RefreshDesiredSize();
                var narrow = request!.Width;
                Assert.AreSame(raw, field.GetValue(control));
                field.SetValue(control, raw with { Next = "Short" });
                control.RefreshDesiredSize();
                Assert.AreEqual(narrow, request!.Width, .01);
                control.ApplyLayout(WindowMode.Taskbar, LayoutOrientation.Horizontal, 100, 100);
                field.SetValue(control, raw);
                control.RefreshDesiredSize();
                Assert.IsTrue(request!.Width > narrow + 100);
                Assert.IsTrue(SettingsManager.Current.TwoLineLyricsEnabled);
                Assert.IsTrue(SettingsManager.Current.LyricsSyllableHighlightEnabled);
                Assert.AreEqual(saved.LyricsSecondaryLine, SettingsManager.Current.LyricsSecondaryLine);
            }
            finally { control.DesiredSizeChanged -= OnSize; }
            VerifyCenters(control);
        }
        finally { SettingsManager.Current = previous; control.ApplyPresentationSettings(null); }
    }

    private static void VerifyCenters(TaskBarMediaControl control)
    {
        SettingsManager.Current.LyricsEnabled = false;
        SettingsManager.Current.TaskbarExperience = new()
        {
            SpectrumVisible = true, PerformanceVisible = true, OutputDeviceVisible = true, VolumeVisible = true
        };
        control.UpdateSongInfo(MediaSnapshot.Disconnected with { IsConnected = true, Title = "Alignment" });
        var border = (FrameworkElement)control.FindName("MainBorder");
        foreach (var mode in new[] { WindowMode.Taskbar, WindowMode.FloatingBar })
        foreach (var orientation in new[] { LayoutOrientation.Horizontal, LayoutOrientation.Vertical })
        foreach (var cross in orientation == LayoutOrientation.Horizontal ? new[] { 24d, 44d } : new[] { 32d, 64d })
        {
            control.ApplyLayout(mode, orientation, 100, 100, cross);
            control.ApplyPrimaryLength(500);
            control.Measure(new Size(orientation == LayoutOrientation.Horizontal ? 500 : cross,
                orientation == LayoutOrientation.Horizontal ? cross : 500));
            control.Arrange(new Rect(control.DesiredSize));
            control.UpdateLayout();
            foreach (var name in new[] { "SongImageBorder", "SongInfoStackPanel", "TaskbarSpectrum", "TaskbarPerformanceSurface", "TaskbarOutputDeviceSurface", "TaskbarVolumeSurface" })
            {
                var element = (FrameworkElement)control.FindName(name);
                var center = element.TranslatePoint(new Point(element.ActualWidth / 2, element.ActualHeight / 2), border);
                var actual = orientation == LayoutOrientation.Horizontal ? center.Y : center.X;
                var expected = (orientation == LayoutOrientation.Horizontal ? border.ActualHeight : border.ActualWidth) / 2;
                Assert.AreEqual(expected, actual, .75, $"{mode}/{orientation}/{cross}: {name} must share the cross-axis center.");
                if (element.Parent is Border parent && name.StartsWith("Taskbar", StringComparison.Ordinal))
                {
                    var bounds = element.TransformToAncestor(parent).TransformBounds(new Rect(new Size(element.ActualWidth, element.ActualHeight)));
                    Assert.IsTrue(bounds.Left >= -.75 && bounds.Top >= -.75 &&
                        bounds.Right <= parent.ActualWidth + .75 && bounds.Bottom <= parent.ActualHeight + .75,
                        $"{mode}/{orientation}/{cross}: {name} must fit its hover surface.");
                }
            }
        }
    }
}
