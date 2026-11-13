// -----------------------------------------------------------------------
// <copyright file="FakeAuthApplicationRepository.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Threading.Channels;
using AuthCallout.Config.Postgres.Repository;

namespace AuthCallout.Config.Tests.Postgres;

/// <summary>
/// A scriptable fake <see cref="IAuthApplicationRepository"/> for unit-testing
/// <see cref="AuthCallout.Config.Postgres.PostgresRoutingConfigProvider"/> against a repository
/// seam without Npgsql or a database (P12 packet §Testing strategy). Each
/// <see cref="ListEnabledAsync"/> call returns the current <see cref="SetRows"/> snapshot;
/// <see cref="EmitNotification"/> pushes a change signal through
/// <see cref="ListenForChangesAsync"/>.
/// </summary>
public sealed class FakeAuthApplicationRepository : IAuthApplicationRepository
{
    private readonly Channel<NotificationSignal> _notifications = Channel.CreateUnbounded<NotificationSignal>();
    private readonly Lock _gate = new();

    private IReadOnlyList<AuthApplicationRow> _rows = [];
    private Exception? _nextListException;

    /// <summary>Gets the number of times <see cref="ListEnabledAsync"/> has been called.</summary>
    /// <value>The list call count.</value>
    public int ListCallCount { get; private set; }

    /// <summary>Sets the rows the next (and subsequent) <see cref="ListEnabledAsync"/> calls return.</summary>
    /// <param name="rows">The rows to return.</param>
    public void SetRows(IReadOnlyList<AuthApplicationRow> rows)
    {
        lock (_gate)
        {
            _rows = rows;
        }
    }

    /// <summary>Makes the next <see cref="ListEnabledAsync"/> call throw (simulating a transient connection failure), then reverts to normal.</summary>
    public void QueueListFailure()
    {
        lock (_gate)
        {
            _nextListException = new IOException("Simulated connection failure.");
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AuthApplicationRow>> ListEnabledAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            ListCallCount++;
            if (_nextListException is { } ex)
            {
                _nextListException = null;
                throw ex;
            }

            return Task.FromResult(_rows);
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<NotificationSignal> ListenForChangesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in _notifications.Reader.ReadAllAsync(cancellationToken))
        {
            yield return item;
        }
    }

    /// <summary>Pushes one notification through <see cref="ListenForChangesAsync"/>.</summary>
    public void EmitNotification() => _notifications.Writer.TryWrite(NotificationSignal.Instance);
}
