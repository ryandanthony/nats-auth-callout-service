// -----------------------------------------------------------------------
// <copyright file="PendingBatchChannel.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Seams;

namespace AuthCallout.Config.K8s;

/// <summary>
/// A single-slot "latest batch wins" handoff between the aggregation callbacks (which may fire
/// synchronously and rapidly during a relist replaying many CRs) and the
/// <see cref="CrdRoutingConfigProvider.WatchAsync"/> consumer loop. Only the most recent batch
/// is ever held: if a new batch arrives before the consumer drains the previous one, it
/// overwrites it, since only the latest aggregate matters (an older accepted batch superseded by
/// a newer one is never independently useful).
/// </summary>
internal sealed class PendingBatchChannel : IDisposable
{
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly Lock _gate = new();
    private RoutingConfigBatch? _pending;

    /// <summary>Publishes a batch, overwriting any not-yet-consumed previous one, and wakes a waiting consumer.</summary>
    /// <param name="batch">The batch to publish.</param>
    internal void Publish(RoutingConfigBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        lock (_gate)
        {
            _pending = batch;
        }

        if (_signal.CurrentCount == 0)
        {
            try
            {
                _signal.Release();
            }
            catch (SemaphoreFullException)
            {
                // Raced with another releaser; a batch is already signalled either way.
            }
        }
    }

    /// <summary>Waits for and takes the next published batch.</summary>
    /// <param name="token">Cancellation token.</param>
    /// <returns>The next batch, or null when <paramref name="token"/> was cancelled first.</returns>
    internal async Task<RoutingConfigBatch?> TakeAsync(CancellationToken token)
    {
        try
        {
            await _signal.WaitAsync(token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        lock (_gate)
        {
            var batch = _pending;
            _pending = null;
            return batch;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _signal.Dispose();
}
