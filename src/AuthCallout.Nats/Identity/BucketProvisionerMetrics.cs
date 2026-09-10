// -----------------------------------------------------------------------
// <copyright file="BucketProvisionerMetrics.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Diagnostics.Metrics;

namespace AuthCallout.Nats.Identity;

/// <summary>
/// The <see cref="System.Diagnostics.Metrics.Meter"/> and counters
/// <see cref="BucketProvisioner"/> records (D16: "Removal: leave orphaned, log + warning
/// metric"). P13 (Host) registers this meter with the OTel <c>MeterProvider</c>; this class
/// only creates and increments the instrument, it does not export anywhere itself.
/// </summary>
public static class BucketProvisionerMetrics
{
    /// <summary>The meter name P13 must add to its OTel <c>MeterProvider.AddMeter(...)</c> list.</summary>
#pragma warning disable S2339 // Kept const (matching EventEmitterMetrics.MeterName's own suppression rationale): passed to `new Meter(MeterName)` below.
    public const string MeterName = "AuthCallout.Nats.Identity";
#pragma warning restore S2339

    /// <summary>
    /// The counter name P13 wires into its OTel dashboards (D16: "warning metric").
    /// Incremented once per per-application bucket (<c>callout_apps_{slug}_*</c>) found in
    /// the KV store but no longer belonging to any currently-configured application —
    /// i.e. left over after that application was removed from config. Never incremented
    /// for a bucket that still belongs to a configured application, even one with
    /// identity propagation now disabled (that bucket is simply not re-provisioned, not
    /// orphaned).
    /// </summary>
#pragma warning disable S2339 // See MeterName above.
    public const string OrphanedBucketsCounterName = "identity_orphaned_buckets_total";
#pragma warning restore S2339

    private static readonly Meter Meter = new(MeterName);

    /// <summary>Gets the orphaned-buckets counter (see <see cref="OrphanedBucketsCounterName"/>).</summary>
    /// <value>The orphaned-buckets counter.</value>
    public static Counter<long> OrphanedBuckets { get; } =
        Meter.CreateCounter<long>(OrphanedBucketsCounterName, description: "Per-application KV buckets found for an application no longer in config (D16: left orphaned, not deleted).");
}
