// -----------------------------------------------------------------------
// <copyright file="SelfHealthCheck.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AuthCallout.Host.Health;

/// <summary>
/// The <c>self</c> liveness check (doc 08 §Health &amp; readiness: "Process is up and the
/// host is not faulted"), tagged <c>live</c>. Always <see cref="HealthStatus.Healthy"/> when
/// invoked at all — the host process responding to the health request is itself the signal;
/// there is nothing further to inspect. Deliberately independent of config/NATS state
/// (doc 08 §Probe mapping: "independent of config/NATS so a transient Idle never restarts
/// the pod").
/// </summary>
public sealed class SelfHealthCheck : IHealthCheck
{
    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(HealthCheckResult.Healthy("Process is up."));
}
