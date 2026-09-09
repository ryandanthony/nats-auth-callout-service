// -----------------------------------------------------------------------
// <copyright file="RowAggregator.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Config.Files.Parsing;
using AuthCallout.Config.Postgres.Repository;
using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Validation;

namespace AuthCallout.Config.Postgres.Aggregation;

/// <summary>
/// Maps and validates a full set of enabled <c>auth_applications</c> rows into one
/// <see cref="RoutingConfig"/> aggregate (P12 packet: "Any bad row rejects the batch naming the
/// row", ADR 0006 atomicity). Pure and repository-free, so it is unit-testable without Npgsql or
/// a database. Reuses <see cref="ApplicationFileMapper"/> for the per-row <c>spec</c> mapping
/// (coordinator-approved, ADR 0007: the JSONB document is byte-identical to file schema v1, so
/// files and Postgres share one mapper rather than risking drift between duplicate copies).
/// </summary>
public static class RowAggregator
{
    /// <summary>
    /// Maps and validates every given row. Disabled rows must already be excluded by the caller
    /// (<see cref="IAuthApplicationRepository.ListEnabledAsync"/> only returns enabled rows).
    /// </summary>
    /// <param name="rows">The enabled rows to aggregate.</param>
    /// <returns>The aggregation decision: a validated batch, or a deterministic rejection naming the offending row(s).</returns>
    public static RowAggregationDecision Aggregate(IReadOnlyList<AuthApplicationRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var failures = new List<string>();
        var applications = new List<Application>();
        foreach (var row in rows)
        {
            var result = ApplicationFileMapper.TryMap(row.Spec);
            if (result.IsSuccess)
            {
                applications.Add(result.Application!);
            }
            else
            {
                failures.Add($"{row.Name}: {result.Error}");
            }
        }

        if (failures.Count > 0)
        {
            return RowAggregationDecision.Failure(
                $"Rejected batch: {failures.Count} row(s) invalid (keeping last-good, ADR 0006): {string.Join(" | ", failures)}");
        }

        var routing = new RoutingConfig(applications);
        var validation = ConfigValidator.ValidateRouting(routing);
        if (!validation.IsValid)
        {
            return RowAggregationDecision.Failure(
                $"Rejected batch: aggregate validation failed (keeping last-good, ADR 0006): "
                + string.Join(" | ", validation.Errors.Select(static e => e.Message)));
        }

        return RowAggregationDecision.Success(routing);
    }
}
