// -----------------------------------------------------------------------
// <copyright file="AggregationDecision.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;

namespace AuthCallout.Config.K8s.Aggregation;

/// <summary>
/// The outcome of <see cref="OwnedResourceSet.Aggregate"/>: either a validated
/// <see cref="RoutingConfig"/> ready to emit, or a rejection (ADR 0006 — no batch is emitted on
/// rejection, the caller keeps last-good). Either way, per-CR <see cref="Statuses"/> are always
/// populated so the caller can write status back for every tracked CR (doc 09 §4).
/// </summary>
/// <param name="Routing">The validated aggregate, when successful.</param>
/// <param name="Error">The deterministic rejection reason, when unsuccessful.</param>
/// <param name="Statuses">Per-CR status write-back decisions, covering every currently-tracked CR.</param>
public sealed record AggregationDecision(RoutingConfig? Routing, string? Error, IReadOnlyList<CustomResourceStatusDecision> Statuses)
{
    /// <summary>Gets a value indicating whether aggregation succeeded.</summary>
    /// <value>True when <see cref="Routing"/> is set.</value>
    public bool IsSuccess => Routing is not null;

    /// <summary>Creates a successful aggregation decision.</summary>
    /// <param name="routing">The validated aggregate.</param>
    /// <param name="statuses">Per-CR status decisions (all accepted).</param>
    /// <returns>The constructed success decision.</returns>
    public static AggregationDecision Success(RoutingConfig routing, IReadOnlyList<CustomResourceStatusDecision> statuses)
    {
        ArgumentNullException.ThrowIfNull(routing);
        ArgumentNullException.ThrowIfNull(statuses);
        return new AggregationDecision(routing, Error: null, statuses);
    }

    /// <summary>Creates a failed aggregation decision.</summary>
    /// <param name="error">The deterministic rejection reason.</param>
    /// <param name="statuses">Per-CR status decisions (mixed accepted/rejected).</param>
    /// <returns>The constructed failure decision.</returns>
    public static AggregationDecision Failure(string error, IReadOnlyList<CustomResourceStatusDecision> statuses)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        ArgumentNullException.ThrowIfNull(statuses);
        return new AggregationDecision(Routing: null, error, statuses);
    }
}
