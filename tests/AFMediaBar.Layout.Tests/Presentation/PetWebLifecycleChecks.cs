// 复用隔离 WPF 进程，以可控浏览器验证初始化取消、旧回调和恢复边界；不安装 Hook。
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AFMediaBar.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

internal static class PetWebLifecycleChecks
{
    internal static void Verify()
    {
        var created = new List<Renderer>();
        using var pet = new PetView(() =>
        {
            var renderer = new Renderer();
            created.Add(renderer);
            return renderer;
        });
        var window = new Window { Content = pet, Width = 60, Height = 60, Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            pet.SetPresentation(true, true);
            window.Show();
            Pump(window);
            var first = created.Single();
            pet.Present(new(new Point(20, 20), true));
            Assert.AreEqual(1L, first.States[^1].BlinkSequence);
            Assert.AreEqual(0L, first.States[^1].PressSequence, "全局点击不能压缩身体。");
            pet.Press();
            Assert.AreEqual(1L, first.States[^1].PressSequence);
            var staleReady = first.CaptureReady();
            pet.SetPresentation(false, false);
            Assert.IsTrue(first.Disposed);
            Assert.IsFalse(pet.HasBrowser);
            pet.SetPresentation(true, true);
            first.Complete();
            staleReady?.Invoke(first, EventArgs.Empty);
            Pump(window);
            Assert.IsFalse(pet.IsWebReady, "旧浏览器不能把新实例标记为就绪。");
            var second = created[^1];
            second.Complete();
            second.RaiseReady();
            Pump(window);
            Assert.IsTrue(pet.IsWebReady);
            pet.SetPresentation(false, true);
            Assert.IsFalse(second.Active);
            Assert.IsFalse(second.Disposed);
            pet.SetPresentation(true, true);
            Assert.IsTrue(second.Active);
            Assert.AreEqual(2, created.Count, "短时挂起不能重建浏览器。");
            second.RaiseFailed();
            Assert.IsFalse(pet.HasBrowser);
            Assert.IsFalse(pet.IsWebReady);
            pet.SetPresentation(true, true);
            Assert.AreEqual(2, created.Count, "故障不能造成反复初始化。");
            pet.SetPresentation(false, false);
            pet.SetPresentation(true, true);
            var third = created[^1];
            third.Complete();
            third.RaiseReady();
            Assert.IsTrue(PetGraphicsRecovery.Request());
            Pump(window);
            Assert.IsTrue(third.Disposed);
            Assert.IsFalse(pet.HasBrowser);
            Assert.IsFalse(PetGraphicsRecovery.Request(), "释放后须移除全局恢复订阅。");
            pet.Dispose();
            pet.Dispose();
            pet.SetPresentation(true, true);
            Assert.AreEqual(3, created.Count);
        }
        finally { window.Close(); }
    }

    private static void Pump(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private sealed class Renderer : IPetWebRenderer
    {
        private readonly TaskCompletionSource _initialization = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public FrameworkElement View { get; } = new Border();
        public event EventHandler? Ready;
        public event EventHandler? Failed;
        public List<PetWebState> States { get; } = [];
        public bool Active { get; private set; } = true;
        public bool Disposed { get; private set; }
        public Task InitializeAsync() => _initialization.Task;
        public void Present(PetWebState state) => States.Add(state);
        public void SetActive(bool active) => Active = active;
        public void Dispose() => Disposed = true;
        internal void Complete() => _initialization.TrySetResult();
        internal EventHandler? CaptureReady() => Ready;
        internal void RaiseReady() => Ready?.Invoke(this, EventArgs.Empty);
        internal void RaiseFailed() => Failed?.Invoke(this, EventArgs.Empty);
    }
}
