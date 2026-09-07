// -----------------------------------------------------------------------
// <copyright file="IJwksCacheManager.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Contracts.Seams;

/// <summary>
/// Keeps every configured issuer's JWKS warm so the IdP stays off the hot path (doc 08
/// §JWKS cache manager). Implemented by <c>AuthCallout.Identity</c> (P06); registered as a
/// hosted service and consumed for startup gating and health reporting by
/// <c>AuthCallout.Host</c> (P13).
/// </summary>
public interface IJwksCacheManager
{
    /// <summary>
    /// Fetches discovery + JWKS for every issuer in the current <see cref="Config.RoutingConfig"/>
    /// at least once. The <c>started</c> startup health check (doc 08) gates <c>Serving</c>
    /// on this completing when <see cref="Config.JwksSettings.PrewarmRequired"/> is true; no
    /// auth request is answered against a cold cache in that mode.
    /// </summary>
    /// <param name="cancellationToken">Cancellation for the prewarm attempt.</param>
    /// <returns>A task that completes when the prewarm/refresh attempt finishes.</returns>
    Task PrewarmAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Fetches (or triggers a single-flight refresh for) the JWKS for one issuer, used when
    /// validation encounters a <c>kid</c> not currently cached (doc 08: "an unknown <c>kid</c>
    /// fails closed and triggers a single-flight JWKS refresh"). Concurrent calls for the
    /// same issuer collapse into one upstream fetch.
    /// </summary>
    /// <param name="issuerAuthority">The issuer authority to refresh.</param>
    /// <param name="cancellationToken">Cancellation for the refresh attempt.</param>
    /// <returns>A task that completes when the prewarm/refresh attempt finishes.</returns>
    Task RefreshAsync(string issuerAuthority, CancellationToken cancellationToken);

    /// <summary>Gets per-issuer freshness snapshot, for the <c>jwks</c> health check.</summary>
    /// <value>The per-issuer freshness snapshot.</value>
    IReadOnlyList<JwksIssuerHealth> Health { get; }
}
