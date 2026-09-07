// -----------------------------------------------------------------------
// <copyright file="IRoutingConfigProvider.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Contracts.Seams;

/// <summary>
/// A pluggable routing/apps config source (ADR 0005, doc 04 §Provider abstraction).
/// Implemented by <c>AuthCallout.Config.Files</c> (P10), <c>AuthCallout.Config.K8s</c>
/// (P11), and <c>AuthCallout.Config.Postgres</c> (P12); consumed by <c>AuthCallout.Host</c>'s
/// config-apply loop (P13). Exactly one implementation is active per running instance
/// (ADR 0005: "sources are not combined").
/// </summary>
public interface IRoutingConfigProvider : IAsyncDisposable
{
    /// <summary>
    /// Starts watching the source and yields a new <see cref="RoutingConfigBatch"/> every
    /// time a full, successfully-validated aggregate is available — on initial load, on
    /// every subsequent successful list/relist (even when nothing changed, so
    /// <see cref="RoutingConfigBatch.ObservedAt"/> can drive the ADR 0017 staleness clock),
    /// and whenever the underlying source changes. An invalid unit rejects the whole
    /// prospective batch (ADR 0006): this method never yields a partially-valid batch, and
    /// the failed attempt is surfaced only through <see cref="LastSuccessfulSync"/> not
    /// advancing and the provider's own logging/metrics.
    /// </summary>
    /// <param name="cancellationToken">Cancellation to stop watching.</param>
    /// <returns>An async sequence of successfully-validated routing batches.</returns>
    IAsyncEnumerable<RoutingConfigBatch> WatchAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets the timestamp of the most recent successful list/relist, updated on every successful
    /// scan regardless of whether it produced a change (ADR 0017: "so 'no changes' and
    /// 'not syncing' are distinguishable"). Null before the first successful sync.
    /// </summary>
    /// <value>The timestamp of the most recent successful sync, or null before the first.</value>
    DateTimeOffset? LastSuccessfulSync { get; }
}
