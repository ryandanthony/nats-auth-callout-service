// -----------------------------------------------------------------------
// <copyright file="HealthTestHost.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using Alba;
using AuthCallout.Contracts.Seams;
using AuthCallout.Host.Health;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace AuthCallout.Host.Tests.Health;

/// <summary>
/// Builds a real <see cref="WebApplication"/> hosting the actual health-check registrations
/// and <see cref="HealthEndpointMapper"/> routes (doc 08 §Health &amp; readiness, D21), wired
/// to caller-supplied collaborators (a real <see cref="ServiceStateMachine"/> for state/
/// freshness, substituted/faked leaves for <see cref="INatsConnection"/>,
/// <see cref="IRoutingConfigProvider"/>, and <see cref="IJwksCacheManager"/>) — this exercises
/// the same DI-registered <c>IHealthCheck</c> implementations and JSON response writer
/// <c>Program.cs</c> wires, per the P13 testing bar ("Alba for health endpoints... through
/// the real host").
/// </summary>
internal static class HealthTestHost
{
    public static async Task<IAlbaHost> BuildAsync(HealthTestHostOptions? options = null)
    {
        var effective = options ?? new HealthTestHostOptions();

        var builder = WebApplication.CreateBuilder([]);
        builder.Services.AddSingleton(effective.StateMachine);
        builder.Services.AddSingleton<IServiceStateProvider>(effective.StateMachine);
        builder.Services.AddSingleton(effective.Connection);
        builder.Services.AddSingleton(effective.RoutingProvider);
        builder.Services.AddSingleton(effective.JwksCacheManager);
        builder.Services.AddSingleton(effective.StartupHealthCheck);

        builder.Services.AddHealthChecks()
            .AddCheck<SelfHealthCheck>("self", tags: ["live"])
            .AddCheck<NatsConnectionHealthCheck>("nats-connection", tags: ["ready"])
            .AddCheck<ConfigActiveHealthCheck>("config-active", tags: ["ready"])
            .AddCheck<ConfigSourceHealthCheck>("config-source", tags: ["ready"])
            .AddCheck<JwksHealthCheck>("jwks")
            .AddCheck<StartupHealthCheck>("started", tags: ["startup"]);

        return await AlbaHost.For(builder, app => app.MapAuthCalloutHealthEndpoints());
    }
}
