// -----------------------------------------------------------------------
// <copyright file="HealthAppBuilder.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Seams;
using AuthCallout.Host.Health;
using AuthCallout.Host.Lifecycle;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace AuthCallout.Host.Wiring;

/// <summary>
/// Builds and runs the minimal <see cref="WebApplication"/> that hosts both the health
/// endpoints (D21) and the top-level <see cref="AuthCalloutHostedService"/> (doc 08 §Health &amp;
/// readiness: "a minimal WebApplication on a dedicated port... CalloutResponder, config
/// providers, connection manager run as hosted services on the same host"). Factored out of
/// <c>Program.cs</c> purely to keep that entry point's own class-coupling count under the
/// repo's analyzer limit.
/// </summary>
public static class HealthAppBuilder
{
    /// <summary>Builds and runs the host until shutdown.</summary>
    /// <param name="args">The process command-line arguments.</param>
    /// <param name="bootstrap">The validated bootstrap config.</param>
    /// <param name="stateMachine">The service state machine.</param>
    /// <param name="activeConfigSource">The active-config source.</param>
    /// <param name="configApplyLoop">The config-apply loop.</param>
    /// <param name="routingProvider">The active routing provider.</param>
    /// <param name="components">The component graph.</param>
    /// <returns>A task that completes when the host stops.</returns>
    public static Task RunAsync(
        string[] args,
        BootstrapConfig bootstrap,
        ServiceStateMachine stateMachine,
        ActiveConfigSource activeConfigSource,
        ConfigApplyLoop configApplyLoop,
        IRoutingConfigProvider routingProvider,
        ComponentGraphResult components)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.ListenAnyIP(HostPorts.Health));
        builder.Services.AddAuthCalloutHost(bootstrap, stateMachine, activeConfigSource, configApplyLoop, routingProvider, components);

        var app = builder.Build();
        app.MapAuthCalloutHealthEndpoints();
        return app.RunAsync();
    }
}
