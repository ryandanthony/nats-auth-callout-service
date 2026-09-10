// -----------------------------------------------------------------------
// <copyright file="ComponentGraphResult.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Seams;
using AuthCallout.Host.Lifecycle;
using AuthCallout.Host.Observability;
using AuthCallout.Identity.Jwks;
using NATS.Client.Core;

namespace AuthCallout.Host.Wiring;

/// <summary>The component graph <see cref="ComponentGraph.Build"/> produces.</summary>
/// <param name="IdentityStoreWriter">The identity-store writer (P08 stand-in until it lands).</param>
/// <param name="JwksCacheManager">The JWKS cache manager, registered as both <see cref="IJwksCacheManager"/> and the hosted service's prewarm dependency.</param>
/// <param name="Connection">The service-user NATS connection.</param>
/// <param name="ConnectionManager">Owns the connection's lifecycle and the callout responder loop.</param>
/// <param name="Metrics">The Host-owned OTel metrics.</param>
public sealed record ComponentGraphResult(
    IIdentityStoreWriter IdentityStoreWriter,
    JwksCacheManager JwksCacheManager,
    NatsConnection Connection,
    NatsConnectionManager ConnectionManager,
    HostMetrics Metrics);
