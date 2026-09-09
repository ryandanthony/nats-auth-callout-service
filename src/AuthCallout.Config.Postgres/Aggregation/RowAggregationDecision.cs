// -----------------------------------------------------------------------
// <copyright file="RowAggregationDecision.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;

namespace AuthCallout.Config.Postgres.Aggregation;

/// <summary>
/// The outcome of <see cref="RowAggregator.Aggregate"/>: either a validated
/// <see cref="RoutingConfig"/> ready to emit, or a rejection (ADR 0006 — no batch is emitted on
/// rejection, the caller keeps last-good).
/// </summary>
/// <param name="Routing">The validated aggregate, when successful.</param>
/// <param name="Error">The deterministic rejection reason, when unsuccessful.</param>
public sealed record RowAggregationDecision(RoutingConfig? Routing, string? Error)
{
    /// <summary>Gets a value indicating whether aggregation succeeded.</summary>
    /// <value>True when <see cref="Routing"/> is set.</value>
    public bool IsSuccess => Routing is not null;

    /// <summary>Creates a successful aggregation decision.</summary>
    /// <param name="routing">The validated aggregate.</param>
    /// <returns>The constructed success decision.</returns>
    public static RowAggregationDecision Success(RoutingConfig routing)
    {
        ArgumentNullException.ThrowIfNull(routing);
        return new RowAggregationDecision(routing, Error: null);
    }

    /// <summary>Creates a failed aggregation decision.</summary>
    /// <param name="error">The deterministic rejection reason.</param>
    /// <returns>The constructed failure decision.</returns>
    public static RowAggregationDecision Failure(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return new RowAggregationDecision(Routing: null, error);
    }
}
