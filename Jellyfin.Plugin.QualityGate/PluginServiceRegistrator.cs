using Jellyfin.Plugin.QualityGate.Providers;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.QualityGate;

/// <summary>
/// Registers plugin services with the dependency injection container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // Intro-only build: the policy-based intro provider is the only registered
        // service. MediaSourceResultFilter stays in the assembly but is intentionally
        // NOT registered — media source filtering stays inactive on Jellyfin 12 until
        // it has been re-validated against the new ABI.
        serviceCollection.AddSingleton<IIntroProvider, QualityGateIntroProvider>();
    }
}
