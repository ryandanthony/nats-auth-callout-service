// -----------------------------------------------------------------------
// <copyright file="JwksIssuerHealth.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Contracts.Seams;

/// <summary>
/// Per-issuer JWKS freshness, for the <c>jwks</c> health check (doc 08 §Health &amp;
/// readiness): informational, never fails readiness on its own even when
/// <see cref="IsHealthy"/> is false (a stale-but-usable cache is <c>Degraded</c>, not
/// <c>Unhealthy</c>, per doc 08 §JWKS cache manager §Health &amp; observability).
/// </summary>
/// <param name="Authority">The issuer authority this status describes.</param>
/// <param name="IsHealthy">True when the cache refreshed within its configured interval.</param>
/// <param name="HasUsableKeys">True when at least one still-valid key is cached, even if stale.</param>
/// <param name="LastRefreshed">When this issuer's JWKS was last successfully fetched, or null if never.</param>
public sealed record JwksIssuerHealth(string Authority, bool IsHealthy, bool HasUsableKeys, DateTimeOffset? LastRefreshed);
