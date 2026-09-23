// -----------------------------------------------------------------------
// <copyright file="CalloutResponderMetrics.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Diagnostics.Metrics;

namespace AuthCallout.Nats.Callout;

/// <summary>
/// The <see cref="Meter"/> and counters <see cref="CalloutResponder"/> records for every
/// auth-callout request it handles (doc 05/08 §Observability): the success/failure outcome of
/// the callout method being invoked. Mirrors <c>EventEmitterMetrics</c>: this class only
/// creates and increments the instruments, it does not export anywhere itself. Public so the
/// Host (P13) can add <see cref="MeterName"/> to its OTel <c>MeterProvider.AddMeter(...)</c>
/// list. Every counter is value-free (no principal/token/subject tags), matching the callout's
/// no-secret-in-telemetry stance (ADR 0008).
/// </summary>
public static class CalloutResponderMetrics
{
    /// <summary>The meter name the Host must add to its OTel <c>MeterProvider.AddMeter(...)</c> list.</summary>
#pragma warning disable S2339 // Kept const (matching EventEmitterMetrics.MeterName's rationale): passed to `new Meter(MeterName)` below.
    public const string MeterName = "AuthCallout.Nats.Callout";
#pragma warning restore S2339

    /// <summary>The counter name for granted requests (<c>callout_requests_granted_total</c>): the callout replied with a grant (success).</summary>
#pragma warning disable S2339
    public const string GrantedCounterName = "callout_requests_granted_total";
#pragma warning restore S2339

    /// <summary>The counter name for denied requests (<c>callout_requests_denied_total</c>): the callout replied with a denial (failure).</summary>
#pragma warning disable S2339
    public const string DeniedCounterName = "callout_requests_denied_total";
#pragma warning restore S2339

    /// <summary>The counter name for dropped requests (<c>callout_requests_dropped_total</c>): a request that never produced a reply (malformed, xkey mismatch, no active config, per-request timeout, or unexpected failure — the client sees the ordinary callout timeout).</summary>
#pragma warning disable S2339
    public const string DroppedCounterName = "callout_requests_dropped_total";
#pragma warning restore S2339

    private static readonly Meter Meter = new(MeterName);

    /// <summary>Gets the counter incremented once per request the callout granted and replied to (success).</summary>
    /// <value>The granted-requests counter.</value>
    public static Counter<long> Granted { get; } =
        Meter.CreateCounter<long>(GrantedCounterName, description: "Auth-callout requests resolved to a grant and replied (success).");

    /// <summary>Gets the counter incremented once per request the callout denied and replied to (failure).</summary>
    /// <value>The denied-requests counter.</value>
    public static Counter<long> Denied { get; } =
        Meter.CreateCounter<long>(DeniedCounterName, description: "Auth-callout requests resolved to a denial and replied (failure).");

    /// <summary>Gets the counter incremented once per request dropped without a reply (malformed, xkey mismatch, no active config, timeout, or unexpected failure).</summary>
    /// <value>The dropped-requests counter.</value>
    public static Counter<long> Dropped { get; } =
        Meter.CreateCounter<long>(DroppedCounterName, description: "Auth-callout requests dropped without a reply (fail closed): malformed, xkey mismatch, no active config, per-request timeout, or unexpected failure.");
}
