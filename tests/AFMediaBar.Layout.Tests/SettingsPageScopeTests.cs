// Verifies page-state isolation and cancellation using disposable DI probes, without opening taskbar hosts or saving user settings.
using AFMediaBar.Classes.Models.Settings;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class SettingsPageScopeTests
{
    [TestMethod]
    public void EditorsAreIsolatedByDisplayAndOrientationWhileGlobalPagesAreRetained()
    {
        var services = new ServiceCollection();
        services.AddScoped<SettingsPageContext>();
        services.AddScoped<Probe>();
        using var provider = services.BuildServiceProvider();
        using var cache = new SettingsPageScopeCache(provider.GetRequiredService<IServiceScopeFactory>());
        var horizontal = new SettingsContext(SettingsMode.Taskbar, "primary", LayoutOrientation.Horizontal, false, true);
        var definition = SettingsPageCatalog.Find(SettingsPageKey.Components, SettingsMode.Taskbar)!;
        var global = SettingsPageCatalog.Find(SettingsPageKey.ApplicationAppearance, SettingsMode.Taskbar)!;
        cache.SetContext(horizontal);
        var first = (Probe)cache.Get(typeof(Probe), definition);
        var application = (Probe)cache.Get(typeof(Probe), global);
        var token = first.Context.CancellationToken;
        var vertical = horizontal with { Orientation = LayoutOrientation.Vertical };
        cache.SetContext(vertical);
        var second = (Probe)cache.Get(typeof(Probe), definition);
        Assert.AreNotSame(first, second);
        Assert.IsTrue(token.IsCancellationRequested);
        Assert.IsFalse(first.Context.IsActive);
        Assert.AreSame(application, cache.Get(typeof(Probe), global));
        Assert.IsTrue(application.Context.IsActive);
        cache.SetContext(vertical with { MonitorDeviceId = "secondary" });
        Assert.AreNotSame(second, cache.Get(typeof(Probe), definition));
        cache.SetContext(horizontal);
        Assert.AreSame(first, cache.Get(typeof(Probe), definition));
        Assert.IsTrue(first.Context.IsActive);
        Assert.IsFalse(first.Context.CancellationToken.IsCancellationRequested);
        cache.Dispose();
        cache.Dispose();
        Assert.AreEqual(1, first.DisposeCount);
        Assert.AreEqual(1, second.DisposeCount);
        Assert.AreEqual(1, application.DisposeCount);
        Assert.IsFalse(first.Context.IsActive);
        Assert.ThrowsException<ObjectDisposedException>(() => cache.Get(typeof(Probe), definition));
    }

    [TestMethod]
    public void UnimplementedRuntimeCannotResolveTaskbarEditorScopes()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var cache = new SettingsPageScopeCache(provider.GetRequiredService<IServiceScopeFactory>());
        cache.SetContext(SettingsContext.Initial with { Mode = SettingsMode.DynamicIsland });
        Assert.ThrowsException<InvalidOperationException>(() => cache.Get(typeof(Probe),
            SettingsPageCatalog.Find(SettingsPageKey.Components, SettingsMode.Taskbar)!));
    }

    private sealed class Probe(SettingsPageContext context) : IDisposable
    {
        public SettingsPageContext Context { get; } = context;
        public int DisposeCount { get; private set; }
        public void Dispose() => DisposeCount++;
    }
}
