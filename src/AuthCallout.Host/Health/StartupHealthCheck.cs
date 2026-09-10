// -----------------------------------------------------------------------
// <copyright file="StartupHealthCheck.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AuthCallout.Host.Health;

/// <summary>
/// The <c>started</c> startup check (doc 08 §Health &amp; readiness: "One-time: bootstrap
/// loaded, secrets resolved, first config decision made"), tagged <c>startup</c>. Gates the
/// Kubernetes <c>startupProbe</c> so a slow first JWKS prewarm or first config fetch doesn't
/// trip liveness during boot (doc 08: "so slow first-config/secret resolution doesn't trip
/// liveness during boot"). <see cref="MarkStarted"/> is called exactly once, by
/// <c>Program.cs</c>, the moment the first config decision has been made (accepted into
/// <see cref="AuthCallout.Contracts.Seams.ServiceState.Serving"/> or explicitly settled into
/// <see cref="AuthCallout.Contracts.Seams.ServiceState.Idle"/> with no config) — not on every
/// subsequent reload, since this check's whole purpose is bounding cold start, not ongoing
/// health.
/// </summary>
public sealed class StartupHealthCheck : IHealthCheck
{
    private volatile bool _started;

    /// <summary>Marks startup complete (idempotent; safe to call more than once, only the first call matters).</summary>
    public void MarkStarted() => _started = true;

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var result = _started
            ? HealthCheckResult.Healthy("Bootstrap loaded, secrets resolved, first config decision made.")
            : HealthCheckResult.Unhealthy("Still starting: bootstrap/secrets/first config decision not yet complete.");
        return Task.FromResult(result);
    }
}
