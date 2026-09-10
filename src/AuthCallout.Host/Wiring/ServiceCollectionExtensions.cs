// -----------------------------------------------------------------------
// <copyright file="ServiceCollectionExtensions.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Seams;
using AuthCallout.Host.Health;
using AuthCallout.Host.Lifecycle;
using Microsoft.Extensions.Hosting;
using NATS.Client.Core;

namespace AuthCallout.Host.Wiring;

/// <summary>
/// Registers the already-constructed lifecycle/component graph into the DI container (doc 08
/// §Process composition). Every collaborator is built once, outside the container, by
/// <c>Program.cs</c> and <see cref="ComponentGraph.Build"/> — this extension only exposes
/// those existing instances to health checks and the top-level hosted service via DI, it does
/// not construct anything new itself.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers the lifecycle primitives, component graph, health checks, and the top-level hosted service.</summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="bootstrap">The validated bootstrap config.</param>
    /// <param name="stateMachine">The service state machine.</param>
    /// <param name="activeConfigSource">The active-config source.</param>
    /// <param name="configApplyLoop">The config-apply loop.</param>
    /// <param name="routingProvider">The active routing provider.</param>
    /// <param name="components">The component graph built by <see cref="ComponentGraph.Build"/>.</param>
    /// <returns><paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddAuthCalloutHost(
        this IServiceCollection services,
        BootstrapConfig bootstrap,
        ServiceStateMachine stateMachine,
        ActiveConfigSource activeConfigSource,
        ConfigApplyLoop configApplyLoop,
        IRoutingConfigProvider routingProvider,
        ComponentGraphResult components)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(bootstrap);
        ArgumentNullException.ThrowIfNull(stateMachine);
        ArgumentNullException.ThrowIfNull(activeConfigSource);
        ArgumentNullException.ThrowIfNull(configApplyLoop);
        ArgumentNullException.ThrowIfNull(routingProvider);
        ArgumentNullException.ThrowIfNull(components);

        services.AddSingleton(bootstrap);
        services.AddSingleton(stateMachine);
        services.AddSingleton<IServiceStateProvider>(stateMachine);
        services.AddSingleton(activeConfigSource);
        services.AddSingleton<IActiveConfigSource>(activeConfigSource);
        services.AddSingleton(configApplyLoop);
        services.AddSingleton(routingProvider);
        services.AddSingleton(components.JwksCacheManager);
        services.AddSingleton<IJwksCacheManager>(components.JwksCacheManager);
        services.AddSingleton<INatsConnection>(components.Connection);
        services.AddSingleton(components.ConnectionManager);
        services.AddSingleton(components.Metrics);
        services.AddSingleton<StartupHealthCheck>();

        services.AddAuthCalloutOpenTelemetry();

        services.AddHealthChecks()
            .AddCheck<SelfHealthCheck>("self", tags: ["live"])
            .AddCheck<NatsConnectionHealthCheck>("nats-connection", tags: ["ready"])
            .AddCheck<ConfigActiveHealthCheck>("config-active", tags: ["ready"])
            .AddCheck<ConfigSourceHealthCheck>("config-source", tags: ["ready"])
            .AddCheck<JwksHealthCheck>("jwks")
            .AddCheck<StartupHealthCheck>("started", tags: ["startup"]);

        services.AddSingleton<IHostedService>(sp => new AuthCalloutHostedService(new AuthCalloutHostedServiceDependencies(
            stateMachine,
            configApplyLoop,
            components.JwksCacheManager,
            bootstrap.Jwks.PrewarmRequired,
            components.ConnectionManager,
            routingProvider,
            sp.GetRequiredService<StartupHealthCheck>(),
            sp.GetRequiredService<ILogger<AuthCalloutHostedService>>())));

        return services;
    }
}
