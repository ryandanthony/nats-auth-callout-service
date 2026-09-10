// -----------------------------------------------------------------------
// <copyright file="EventEmitterMetrics.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Diagnostics.Metrics;

namespace AuthCallout.Nats.Events;

/// <summary>
/// The <see cref="System.Diagnostics.Metrics.Meter"/> and counters <see cref="EventEmitter"/>
/// records (ADR 0008 best-effort delivery, P09 task 3 / DoD "metric name recorded for P13's
/// OTel wiring"). P13 (Host) registers this meter with the OTel `MeterProvider`; this class
/// only creates and increments the instrument, it does not export anywhere itself. Public so
/// P13 can reference <see cref="MeterName"/> when calling `MeterProvider.AddMeter(...)`.
/// </summary>
public static class EventEmitterMetrics
{
    /// <summary>The meter name P13 must add to its OTel `MeterProvider.AddMeter(...)` list.</summary>
#pragma warning disable S2339 // Must stay a compile-time constant: passed to `new Meter(MeterName)` below, which only accepts a literal/const at this use site's simplicity; a property would work too but every other repo constant (e.g. PostgresSourceSettings.DefaultNotifyChannel) uses this same pattern.
    public const string MeterName = "AuthCallout.Nats.Events";
#pragma warning restore S2339

    /// <summary>
    /// The counter name P13 wires into its OTel dashboards (doc 05/08 metrics list,
    /// <c>events_publish_failures_total</c>). Incremented once per swallowed publish
    /// failure (ADR 0008: "a publish failure is logged/metered but does not fail the auth
    /// decision"); never incremented for a silently-skipped (no subject configured) event,
    /// since that is expected, non-error behavior, not a failure.
    /// </summary>
#pragma warning disable S2339 // See MeterName above: kept const, matching the repo's existing named-constant pattern.
    public const string PublishFailuresCounterName = "events_publish_failures_total";
#pragma warning restore S2339

    private static readonly Meter Meter = new(MeterName);

    /// <summary>Gets the publish-failures counter (see <see cref="PublishFailuresCounterName"/>).</summary>
    /// <value>The publish-failures counter.</value>
    public static Counter<long> PublishFailures { get; } =
        Meter.CreateCounter<long>(PublishFailuresCounterName, description: "Auth outcome events that failed to publish and were swallowed (ADR 0008 best-effort).");
}
