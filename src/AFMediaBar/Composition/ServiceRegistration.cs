using AFMediaBar.Services;
using AFMediaBar.Settings;
using AFMediaBar.Components.Abstractions;
using AFMediaBar.Components.BuiltIn;
using AFMediaBar.Layout.Widgets;
using AFMediaBar.Components.Wpf;
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
        services.AddSingleton<IComponentRegistry, BuiltInComponentRegistry>();
        services.AddSingleton<IComponentSettingsMapper>(serviceProvider =>
            new Schema5ComponentSettingsMapper(serviceProvider.GetRequiredService<IComponentRegistry>()));
        services.AddSingleton<IComponentViewFactory, DefaultComponentViewFactory>();
        services.AddTransient<MainWindow>();
        services.AddTransient<SettingsWindow>(serviceProvider => new SettingsWindow(
            serviceProvider.GetRequiredService<SettingsCoordinator>(),
            serviceProvider.GetRequiredService<UpdateService>(),
            serviceProvider.GetRequiredService<SettingsWindowViewModel>(),
            serviceProvider.GetRequiredService<IComponentRegistry>(),
            serviceProvider.GetRequiredService<IComponentSettingsMapper>(),
            serviceProvider.GetRequiredService<IComponentViewFactory>()));
        services.AddTransient<SettingsWindowViewModel>();
        return services.BuildServiceProvider();
    }
}
