// -----------------------------------------------------------------------
// <copyright file="IAuthApplicationRepository.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Config.Postgres.Repository;

/// <summary>
/// A thin seam over the PostgreSQL operations <see cref="PostgresRoutingConfigProvider"/> needs:
/// list enabled application rows, and be notified when the table changes (P12 packet §Testing
/// strategy: "unit tests: mapping + batch semantics against a repository seam"). The real
/// implementation (<see cref="PostgresAuthApplicationRepository"/>) is the only type in this
/// project that references Npgsql directly for connections/LISTEN.
/// </summary>
public interface IAuthApplicationRepository
{
    /// <summary>
    /// Lists every <b>enabled</b> row in <c>auth_applications</c> (P12 packet: "SELECT all
    /// enabled rows"; a disabled row is excluded here, before mapping ever sees it).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The enabled rows, in a stable order.</returns>
    Task<IReadOnlyList<AuthApplicationRow>> ListEnabledAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Watches for change notifications on the configured NOTIFY channel
    /// (<see cref="AuthCallout.Contracts.Config.PostgresSourceSettings.NotifyChannel"/>). A
    /// latency optimization only — the provider's poll loop is the load-bearing change-detection
    /// mechanism regardless (ADR 0017), so this sequence may legitimately never yield anything
    /// (e.g. LISTEN unsupported/unavailable) without breaking correctness, only responsiveness.
    /// </summary>
    /// <param name="cancellationToken">Cancellation to stop listening.</param>
    /// <returns>An async sequence that yields once per received notification.</returns>
    IAsyncEnumerable<NotificationSignal> ListenForChangesAsync(CancellationToken cancellationToken);
}
