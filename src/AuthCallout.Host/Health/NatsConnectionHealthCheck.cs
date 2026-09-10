// -----------------------------------------------------------------------
// <copyright file="NatsConnectionHealthCheck.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using Microsoft.Extensions.Diagnostics.HealthChecks;
using NATS.Client.Core;

namespace AuthCallout.Host.Health;

/// <summary>
/// The <c>nats-connection</c> readiness check (doc 08 §Health &amp; readiness: "NATS
/// connection is open and the callout subscription... is active"), tagged <c>ready</c>.
/// Reads <see cref="INatsConnection.ConnectionState"/> directly rather than a cached flag, so
/// it reflects the client's own view of the connection (including mid-reconnect) without a
/// second source of truth. <see cref="NatsConnectionState.Open"/> is
/// <see cref="HealthStatus.Healthy"/>; every other state (<c>Closed</c>, <c>Connecting</c>,
/// <c>Reconnecting</c>, <c>Failed</c>) is <see cref="HealthStatus.Unhealthy"/> — ADR 0017 §1's
/// "fail closed on unreachability" leaves no degraded middle ground here, since the
/// subscription genuinely answers nothing while disconnected.
/// </summary>
public sealed class NatsConnectionHealthCheck : IHealthCheck
{
    private readonly INatsConnection _connection;

    /// <summary>Initializes a new instance of the <see cref="NatsConnectionHealthCheck"/> class.</summary>
    /// <param name="connection">The callout's own service-user NATS connection.</param>
    public NatsConnectionHealthCheck(INatsConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connection = connection;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var state = _connection.ConnectionState;
        var data = new Dictionary<string, object> { ["connectionState"] = state.ToString() };

        var result = state == NatsConnectionState.Open
            ? HealthCheckResult.Healthy("NATS connection open.", data)
            : HealthCheckResult.Unhealthy($"NATS connection is '{state}', not Open (ADR 0017 §1: fail closed).", data: data);
        return Task.FromResult(result);
    }
}
