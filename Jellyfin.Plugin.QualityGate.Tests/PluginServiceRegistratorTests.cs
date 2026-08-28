using System;
using Jellyfin.Plugin.QualityGate.Filters;
using Jellyfin.Plugin.QualityGate.Providers;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace Jellyfin.Plugin.QualityGate.Tests;

public class PluginServiceRegistratorTests
{
    private static IServiceCollection Register()
    {
        var registrator = new PluginServiceRegistrator();
        var services = new ServiceCollection();
        var appHost = new Mock<IServerApplicationHost>();

        registrator.RegisterServices(services, appHost.Object);
        return services;
    }

    [Fact]
    public void RegisterServices_AddsIntroProvider()
    {
        var services = Register();

        var introDescriptor = Assert.Single(services, s => s.ServiceType == typeof(IIntroProvider));
        Assert.Equal(ServiceLifetime.Singleton, introDescriptor.Lifetime);
        Assert.Equal(typeof(QualityGateIntroProvider), introDescriptor.ImplementationType);
    }

    [Fact]
    public void RegisterServices_AddsResolutionCapFilter()
    {
        var services = Register();

        var filterDescriptor = Assert.Single(services, s => s.ServiceType == typeof(ResolutionCapFilter));
        Assert.Equal(ServiceLifetime.Scoped, filterDescriptor.Lifetime);
    }

    /// <summary>
    /// PostConfigure, never plain Configure. A plugin's RegisterServices runs from
    /// ApplicationHost.Init, before the web host reaches Startup.ConfigureServices and its
    /// AddJellyfinApi, so a Configure delegate registered here is queued ahead of Jellyfin's
    /// own MVC setup.
    /// </summary>
    [Fact]
    public void RegisterServices_RegistersTheFilterWithPostConfigure()
    {
        var services = Register();

        Assert.Contains(services, s => s.ServiceType == typeof(IPostConfigureOptions<MvcOptions>));
        Assert.DoesNotContain(services, s => s.ServiceType == typeof(IConfigureOptions<MvcOptions>));
    }

    /// <summary>
    /// The PostConfigure delegate must actually put the filter on the collection.
    /// </summary>
    [Fact]
    public void PostConfigureDelegate_AddsTheFilterToMvcOptions()
    {
        var services = Register();
        var provider = services.BuildServiceProvider();

        var options = new MvcOptions();
        foreach (var postConfigure in provider.GetServices<IPostConfigureOptions<MvcOptions>>())
        {
            postConfigure.PostConfigure(Options.DefaultName, options);
        }

        Assert.Contains(options.Filters, f =>
            f is ServiceFilterAttribute service && service.ServiceType == typeof(ResolutionCapFilter));
    }

    /// <summary>
    /// The filename-pattern result filter stays out of the pipeline: its rewriting of item and
    /// listing responses has not been re-validated against the Jellyfin 12 ABI.
    /// </summary>
    [Fact]
    public void RegisterServices_DoesNotRegisterMediaSourceResultFilter()
    {
        var services = Register();

        Assert.DoesNotContain(services, s => s.ServiceType == typeof(MediaSourceResultFilter));
    }
}
