using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace RussianMetadata;

public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(
        IServiceCollection serviceCollection,
        IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHostedService<StalePluginVersionCleanupService>();
        serviceCollection.AddSingleton<LibraryConfigurationService>();
        serviceCollection.AddSingleton<ArtworkPreferenceRefreshService>();
    }
}
