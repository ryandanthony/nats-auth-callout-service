// -----------------------------------------------------------------------
// <copyright file="ConfigSourceHealthCheck.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Seams;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AuthCallout.Host.Health;

/// <summary>
/// The <c>config-source</c> readiness check (doc 08 §Health &amp; readiness: "file watcher
/// alive, or the k8s CRD watch is connected"; ADR 0017 §2 staleness table), tagged
/// <c>ready</c>. Maps <see cref="IServiceStateProvider.Freshness"/> directly onto the ADR
/// 0017 table: <see cref="ConfigFreshness.Fresh"/> → Healthy, <see cref="ConfigFreshness.StaleDegraded"/>
/// → Degraded (still <c>/readyz</c> 200 — <see cref="HealthStatus.Degraded"/> does not fail a
/// health-check group), <see cref="ConfigFreshness.StaleUnready"/> → Unhealthy (
/// <c>/readyz</c> 503, "while still serving last-good", doc 08/ADR 0017). Reports the raw
/// <see cref="IRoutingConfigProvider.LastSuccessfulSync"/> and computed staleness in
/// <see cref="HealthCheckResult.Data"/> (doc 08: "operators can see drift directly") — no
/// secret material, just a timestamp.
/// </summary>
public sealed class ConfigSourceHealthCheck : IHealthCheck
{
    private readonly IServiceStateProvider _stateProvider;
    private readonly IRoutingConfigProvider _routingProvider;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="ConfigSourceHealthCheck"/> class.</summary>
    /// <param name="stateProvider">The service state provider to read the current freshness substate from.</param>
    /// <param name="routingProvider">The active routing provider, for the raw <see cref="IRoutingConfigProvider.LastSuccessfulSync"/> diagnostic value.</param>
    /// <param name="timeProvider">Time source for the reported staleness age. Defaults to <see cref="TimeProvider.System"/>.</param>
    public ConfigSourceHealthCheck(IServiceStateProvider stateProvider, IRoutingConfigProvider routingProvider, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(stateProvider);
        ArgumentNullException.ThrowIfNull(routingProvider);
        _stateProvider = stateProvider;
        _routingProvider = routingProvider;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var lastSync = _routingProvider.LastSuccessfulSync;
        var data = new Dictionary<string, object>
        {
            ["lastSuccessfulSync"] = lastSync?.ToString("O", System.Globalization.CultureInfo.InvariantCulture) ?? "never",
            ["stalenessSeconds"] = lastSync is { } sync ? (_timeProvider.GetUtcNow() - sync).TotalSeconds : double.PositiveInfinity,
        };

        var freshness = _stateProvider.Freshness;
        var result = freshness switch
        {
            ConfigFreshness.Fresh => HealthCheckResult.Healthy("Config source in sync.", data),
            ConfigFreshness.StaleDegraded => HealthCheckResult.Degraded("Config source stale beyond warnStaleness (ADR 0017).", data: data),
            ConfigFreshness.StaleUnready => HealthCheckResult.Unhealthy("Config source stale beyond maxStaleness; still serving last-good (ADR 0017).", data: data),
            _ => HealthCheckResult.Unhealthy($"Unrecognized freshness '{freshness}'.", data: data),
        };
        return Task.FromResult(result);
    }
}
