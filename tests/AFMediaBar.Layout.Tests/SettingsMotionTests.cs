// Covers motion degradation and cleanup of a prepared entrance interrupted before its first frame.
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class SettingsMotionTests
{
    [TestMethod]
    public void ReducedMotionRemovesMovementAndInstantMotionCreatesNoEntrance()
    {
        var full = SettingsRevealPolicy.Resolve(999, MotionPolicy.Resolve(true, false, false));
        Assert.AreEqual(TimeSpan.Zero, full.Delay);
        Assert.IsTrue(full.OffsetY > 0d);
        var reduced = SettingsRevealPolicy.Resolve(999, MotionPolicy.Resolve(true, true, false));
        Assert.IsTrue(reduced.ShouldAnimate);
        Assert.AreEqual(0d, reduced.OffsetY);
        Assert.AreEqual(TimeSpan.Zero, reduced.Delay);
        Assert.IsFalse(SettingsRevealPolicy.Resolve(0, MotionPolicy.Resolve(false, false, false)).ShouldAnimate);
    }

    [TestMethod]
    public void UnloadSettlesPreparedContentAndRestoresItsTransform()
    {
        RunSta(() =>
        {
            var original = new ScaleTransform(0.98d, 0.98d);
            var panel = new StackPanel { RenderTransform = original };
            SettingsRevealAnimator.Play(panel);
            SettingsRevealAnimator.Replay(panel);
            panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            Assert.AreEqual(1d, panel.Opacity);
            Assert.AreSame(original, panel.RenderTransform);
            Assert.IsFalse(panel.HasAnimatedProperties);
        });
    }

    [TestMethod]
    public void ContentThatNeverLoadsDoesNotRemainHidden()
    {
        RunSta(() =>
        {
            var panel = new StackPanel();
            SettingsRevealAnimator.Play(panel);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.AreEqual(1d, panel.Opacity);
            Assert.IsFalse(panel.HasAnimatedProperties);
        });
    }

    internal static void RunSta(Action action, TimeSpan? timeout = null)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(timeout ?? TimeSpan.FromSeconds(10)), "The WPF check did not complete.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
