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
    [Fact]
    public void RegisterServices_AddsIntroProvider()
    {
        var registrator = new PluginServiceRegistrator();
        var services = new ServiceCollection();
        var appHost = new Mock<IServerApplicationHost>();

        registrator.RegisterServices(services, appHost.Object);

        var introDescriptor = Assert.Single(services, s => s.ServiceType == typeof(IIntroProvider));
        Assert.Equal(ServiceLifetime.Singleton, introDescriptor.Lifetime);
        Assert.Equal(typeof(QualityGateIntroProvider), introDescriptor.ImplementationType);
    }

    [Fact]
    public void RegisterServices_DoesNotRegisterMediaSourceResultFilter()
    {
        // Intro-only build: the media source filtering path must stay unregistered
        // until it has been re-validated against the Jellyfin 12 ABI.
        var registrator = new PluginServiceRegistrator();
        var services = new ServiceCollection();
        var appHost = new Mock<IServerApplicationHost>();

        registrator.RegisterServices(services, appHost.Object);

        Assert.DoesNotContain(services, s => s.ServiceType == typeof(MediaSourceResultFilter));
        Assert.DoesNotContain(services, s => s.ServiceType == typeof(IPostConfigureOptions<MvcOptions>));
    }

    [Fact]
    public void RegisterServices_RegistersOnlyTheIntroProvider()
    {
        var registrator = new PluginServiceRegistrator();
        var services = new ServiceCollection();
        var appHost = new Mock<IServerApplicationHost>();

        registrator.RegisterServices(services, appHost.Object);

        var descriptor = Assert.Single(services);
        Assert.Equal(typeof(IIntroProvider), descriptor.ServiceType);
    }
}
