// -----------------------------------------------------------------------
// <copyright file="JwksHealthCheck.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Seams;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AuthCallout.Host.Health;

/// <summary>
/// The <c>jwks</c> informational check (doc 08 §Health &amp; readiness: "Per-issuer JWKS
/// reachable/cached; reported as Degraded on stale cache so it never fails readiness on a
/// transient IdP blip"). Deliberately untagged (neither <c>live</c>, <c>ready</c>, nor
/// <c>startup</c>) so it never gates any of the three probes on its own — doc 08 §JWKS cache
/// manager §Health &amp; observability: "Unhealthy only if an issuer has no usable keys at
/// all" is still surfaced for human/dashboard inspection via the raw health JSON, but per-
/// issuer freshness never flips a Kubernetes probe by itself. Per <see cref="JwksIssuerHealth"/>:
/// <see cref="JwksIssuerHealth.IsHealthy"/> true → Healthy; false but
/// <see cref="JwksIssuerHealth.HasUsableKeys"/> true (stale-but-cached) → Degraded; false and
/// no usable keys at all → Unhealthy for that issuer, and the aggregate reports the worst
/// case across all tracked issuers.
/// </summary>
public sealed class JwksHealthCheck : IHealthCheck
{
    private readonly IJwksCacheManager _jwksCacheManager;

    /// <summary>Initializes a new instance of the <see cref="JwksHealthCheck"/> class.</summary>
    /// <param name="jwksCacheManager">The JWKS cache manager to report per-issuer freshness from.</param>
    public JwksHealthCheck(IJwksCacheManager jwksCacheManager)
    {
        ArgumentNullException.ThrowIfNull(jwksCacheManager);
        _jwksCacheManager = jwksCacheManager;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var issuers = _jwksCacheManager.Health;
        var data = new Dictionary<string, object>
        {
            ["issuers"] = issuers.Select(static i => $"{i.Authority}: healthy={i.IsHealthy}, hasUsableKeys={i.HasUsableKeys}").ToArray(),
        };

        if (issuers.Count == 0)
        {
            return Task.FromResult(HealthCheckResult.Healthy("No issuers configured yet.", data));
        }

        if (issuers.Any(static i => !i.HasUsableKeys))
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("At least one issuer has no usable JWKS at all.", data: data));
        }

        var result = issuers.Any(static i => !i.IsHealthy)
            ? HealthCheckResult.Degraded("At least one issuer is serving a stale-but-usable JWKS cache.", data: data)
            : HealthCheckResult.Healthy("All issuers' JWKS are fresh.", data);
        return Task.FromResult(result);
    }
}
