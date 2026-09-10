// -----------------------------------------------------------------------
// <copyright file="ConfigActiveHealthCheck.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Seams;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AuthCallout.Host.Health;

/// <summary>
/// The <c>config-active</c> readiness check (doc 08 §Health &amp; readiness: "A valid
/// routing config is loaded (state is Serving, not Idle/Starting)"), tagged <c>ready</c>.
/// Reads <see cref="IServiceStateProvider.Current"/> directly: <see cref="ServiceState.Serving"/>
/// is <see cref="HealthStatus.Healthy"/>, every other state is <see cref="HealthStatus.Unhealthy"/>
/// — there is no degraded middle state for "do we have an active config", matching doc 06's
/// "runs on a fully valid config, or nothing" invariant.
/// </summary>
public sealed class ConfigActiveHealthCheck : IHealthCheck
{
    private readonly IServiceStateProvider _stateProvider;

    /// <summary>Initializes a new instance of the <see cref="ConfigActiveHealthCheck"/> class.</summary>
    /// <param name="stateProvider">The service state provider to read the current lifecycle state from.</param>
    public ConfigActiveHealthCheck(IServiceStateProvider stateProvider)
    {
        ArgumentNullException.ThrowIfNull(stateProvider);
        _stateProvider = stateProvider;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var state = _stateProvider.Current;
        var data = new Dictionary<string, object>(StringComparer.Ordinal) { ["state"] = state.ToString() };

        var result = state == ServiceState.Serving
            ? HealthCheckResult.Healthy("Serving on a valid, active config.", data)
            : HealthCheckResult.Unhealthy($"Service state is '{state}', not Serving.", data: data);
        return Task.FromResult(result);
    }
}
