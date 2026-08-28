using AFMediaBar.Services;
using AFMediaBar.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace AFMediaBar.Composition;

/// <summary>Application composition root for presentation services.</summary>
internal static class ServiceRegistration
{
    internal static ServiceProvider Build(SettingsCoordinator coordinator, UpdateService updateService)
    {
        var services = new ServiceCollection();
        services.AddSingleton(coordinator);
        services.AddSingleton(updateService);
        services.AddTransient<SettingsWindowViewModel>();
        return services.BuildServiceProvider();
    }
}
