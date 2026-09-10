// -----------------------------------------------------------------------
// <copyright file="AuthCalloutHostedServiceDependencies.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Seams;
using AuthCallout.Host.Health;
using AuthCallout.Host.Lifecycle;
using Microsoft.Extensions.Logging;

namespace AuthCallout.Host.Wiring;

/// <summary>
/// The collaborators <see cref="AuthCalloutHostedService"/> orchestrates, grouped into one
/// type so its constructor stays within the repo's parameter-count analyzer limit (mirroring
/// <c>AuthCallout.Nats.Callout.CalloutResponderDependencies</c>'s pattern) — purely a
/// parameter-list ergonomics grouping, not a new abstraction.
/// </summary>
/// <param name="StateMachine">The service state machine.</param>
/// <param name="ConfigApplyLoop">The config-apply loop to run for the process lifetime.</param>
/// <param name="ActiveConfigSource">The active-config source, awaited before the first connect (doc 05: Idle holds no NATS connection until a valid config exists).</param>
/// <param name="JwksCacheManager">The JWKS cache manager to prewarm before connecting.</param>
/// <param name="PrewarmRequired">Bootstrap <c>jwks.prewarmRequired</c>.</param>
/// <param name="ConnectionManager">The NATS connection manager to start after prewarm.</param>
/// <param name="RoutingProvider">The active routing provider, disposed on shutdown.</param>
/// <param name="StartupHealthCheck">Marked complete once the config-apply loop has made its first decision.</param>
/// <param name="Logger">Logger for lifecycle diagnostics.</param>
public sealed record AuthCalloutHostedServiceDependencies(
    ServiceStateMachine StateMachine,
    ConfigApplyLoop ConfigApplyLoop,
    ActiveConfigSource ActiveConfigSource,
    IJwksCacheManager JwksCacheManager,
    bool PrewarmRequired,
    NatsConnectionManager ConnectionManager,
    IRoutingConfigProvider RoutingProvider,
    StartupHealthCheck StartupHealthCheck,
    ILogger<AuthCalloutHostedService> Logger)
{
    /// <summary>Validates that every reference-type collaborator is non-null, throwing <see cref="ArgumentNullException"/> otherwise.</summary>
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(StateMachine);
        ArgumentNullException.ThrowIfNull(ConfigApplyLoop);
        ArgumentNullException.ThrowIfNull(ActiveConfigSource);
        ArgumentNullException.ThrowIfNull(JwksCacheManager);
        ArgumentNullException.ThrowIfNull(ConnectionManager);
        ArgumentNullException.ThrowIfNull(RoutingProvider);
        ArgumentNullException.ThrowIfNull(StartupHealthCheck);
        ArgumentNullException.ThrowIfNull(Logger);
    }
}
