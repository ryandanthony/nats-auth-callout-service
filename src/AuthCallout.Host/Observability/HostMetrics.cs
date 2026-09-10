// -----------------------------------------------------------------------
// <copyright file="HostMetrics.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Diagnostics.Metrics;
using AuthCallout.Contracts.Seams;

namespace AuthCallout.Host.Observability;

/// <summary>
/// The <see cref="Meter"/> and instruments <c>AuthCallout.Host</c> itself owns (doc 05
/// §Observability, ADR 0017 §Consequences): config reload outcomes, the ADR 0017 staleness
/// pair, watch-recovery counters, and the current lifecycle state as an
/// <see cref="ObservableGauge{T}"/>. Component-owned metrics (e.g.
/// <c>AuthCallout.Nats.Events.EventEmitterMetrics</c>) live in their own packet and are
/// registered with the <c>MeterProvider</c> by name in <c>Wiring/OpenTelemetryExtensions</c>
/// rather than re-declared here.
/// </summary>
public sealed class HostMetrics : IDisposable
{
    /// <summary>Gets the meter name registered with the OTel <c>MeterProvider</c>.</summary>
    /// <value>The meter name, <c>AuthCallout.Host</c>.</value>
    public static string MeterName => "AuthCallout.Host";

    private readonly Meter _meter;

    /// <summary>Initializes a new instance of the <see cref="HostMetrics"/> class.</summary>
    /// <param name="stateProvider">The service state provider the current-state gauge observes.</param>
    public HostMetrics(IServiceStateProvider stateProvider)
    {
        ArgumentNullException.ThrowIfNull(stateProvider);
        _meter = new Meter(MeterName);

        ConfigReloadsApplied = _meter.CreateCounter<long>(
            "config_reloads_applied_total", description: "Config batches accepted and swapped in as the active config (ADR 0006).");
        ConfigReloadsRejected = _meter.CreateCounter<long>(
            "config_reloads_rejected_total", description: "Config batches rejected, keeping last-good (ADR 0006).");
        ConfigLastSyncTimestamp = _meter.CreateGauge<double>(
            "config_last_sync_timestamp", unit: "s", description: "Unix timestamp of the active source's most recent successful sync (ADR 0017).");
        ConfigStalenessSeconds = _meter.CreateGauge<double>(
            "config_staleness_seconds", unit: "s", description: "Seconds since the active source's most recent successful sync (ADR 0017).");
        WatchReconnectsTotal = _meter.CreateCounter<long>(
            "watch_reconnects_total", description: "NATS connection reconnect attempts observed (ADR 0017 §1).");

        _meter.CreateObservableGauge(
            "service_state",
            () => new Measurement<int>((int)stateProvider.Current, new KeyValuePair<string, object?>("state", stateProvider.Current.ToString())),
            description: "The current ServiceState (0=Starting,1=Idle,2=Serving,3=Stopping), doc 05.");
    }

    /// <summary>Gets the counter incremented on every accepted config reload.</summary>
    /// <value>The accepted-reload counter.</value>
    public Counter<long> ConfigReloadsApplied { get; }

    /// <summary>Gets the counter incremented on every rejected config reload.</summary>
    /// <value>The rejected-reload counter.</value>
    public Counter<long> ConfigReloadsRejected { get; }

    /// <summary>Gets the gauge reporting the active source's last successful sync as a Unix timestamp.</summary>
    /// <value>The last-sync-timestamp gauge.</value>
    public Gauge<double> ConfigLastSyncTimestamp { get; }

    /// <summary>Gets the gauge reporting seconds since the active source's last successful sync.</summary>
    /// <value>The staleness-seconds gauge.</value>
    public Gauge<double> ConfigStalenessSeconds { get; }

    /// <summary>Gets the counter incremented on every NATS reconnect attempt.</summary>
    /// <value>The watch-reconnects counter.</value>
    public Counter<long> WatchReconnectsTotal { get; }

    /// <summary>Disposes the underlying meter.</summary>
    public void Dispose() => _meter.Dispose();
}
