// -----------------------------------------------------------------------
// <copyright file="CrdWatchSupervisor.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Config.K8s.Client;

namespace AuthCallout.Config.K8s;

/// <summary>
/// Owns the list-then-watch loop against <see cref="ICrdKubernetesClient"/>: performs an
/// initial list, watches from that resource version, and relists+rewatches on any interruption
/// — a <see cref="CrdWatchGoneException"/> (410 Gone), any other watch failure/end, or the
/// periodic reconciliation interval elapsing (doc 08 §Kubernetes CRD source: "periodic full
/// relist updating LastSuccessfulSync") — with capped exponential backoff after a genuine
/// failure (ADR 0017). Emits every observed change plus a "resynced" callback after each
/// successful list/relist (even one with no resources and even when nothing changed), so the
/// caller can distinguish "no changes" from "not syncing" (ADR 0017) and refresh
/// <c>LastSuccessfulSync</c> regardless of whether anything actually changed.
/// </summary>
internal sealed class CrdWatchSupervisor
{
    private readonly ICrdKubernetesClient _client;
    private readonly TimeSpan _minBackoff;
    private readonly TimeSpan _maxBackoff;
    private readonly TimeSpan _reconcileInterval;

    /// <summary>Initializes a new instance of the <see cref="CrdWatchSupervisor"/> class.</summary>
    /// <param name="client">The Kubernetes client seam to list/watch through.</param>
    /// <param name="minBackoff">The initial relist/rewatch backoff after a genuine failure.</param>
    /// <param name="maxBackoff">The cap on relist/rewatch backoff.</param>
    /// <param name="reconcileInterval">
    /// The periodic full-relist cadence that backstops a silently dead watch even while it
    /// reports no failure (doc 08: "a silently dead watch surfaces as staleness, not as config
    /// loss" — this is what makes that detection possible at all).
    /// </param>
    internal CrdWatchSupervisor(ICrdKubernetesClient client, TimeSpan minBackoff, TimeSpan maxBackoff, TimeSpan reconcileInterval)
    {
        _client = client;
        _minBackoff = minBackoff;
        _maxBackoff = maxBackoff;
        _reconcileInterval = reconcileInterval;
    }

    /// <summary>
    /// Runs the list-watch-relist loop until <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <param name="onResync">
    /// Called after every successful list/relist with the full current resource set, before any
    /// subsequent watch events are applied — the caller resets its owned-set tracking here.
    /// </param>
    /// <param name="onChange">Called for every watch event once the initial list has resynced.</param>
    /// <param name="cancellationToken">Cancellation to stop the loop.</param>
    /// <returns>A task that completes once <paramref name="cancellationToken"/> is cancelled.</returns>
    internal async Task RunAsync(
        Action<IReadOnlyList<CustomResourceRecord>> onResync,
        Action<CrdChangeEvent> onChange,
        CancellationToken cancellationToken)
    {
        var backoff = _minBackoff;

        while (!cancellationToken.IsCancellationRequested)
        {
            using var cycleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cycleCts.CancelAfter(_reconcileInterval);

            var succeeded = await TryListAndWatchAsync(onResync, onChange, cycleCts.Token, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            backoff = succeeded
                ? _minBackoff
                : await DelayAndDoubleBackoffAsync(backoff, cancellationToken);
        }
    }

    /// <summary>
    /// Runs one list-then-watch cycle, bounded by <paramref name="cycleToken"/> (the periodic
    /// reconcile deadline linked to <paramref name="outerToken"/>). Returns true when the cycle
    /// ended for a benign reason (410 Gone, reconcile-interval elapsed, clean server close) worth
    /// resetting backoff for; false when list/watch itself failed.
    /// </summary>
    private async Task<bool> TryListAndWatchAsync(
        Action<IReadOnlyList<CustomResourceRecord>> onResync,
        Action<CrdChangeEvent> onChange,
        CancellationToken cycleToken,
        CancellationToken outerToken)
    {
        CrdListResult list;
        try
        {
            list = await _client.ListAsync(cycleToken);
        }
        catch (OperationCanceledException) when (!outerToken.IsCancellationRequested)
        {
            // The reconcile-interval deadline hit mid-list; treat as a benign cycle end (a fresh
            // list on the next loop iteration is exactly the periodic relist doc 08 calls for).
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException)
        {
            return false;
        }

        onResync(list.Resources);

        try
        {
            await foreach (var changeEvent in _client.WatchAsync(list.ResourceVersion, cycleToken))
            {
                onChange(changeEvent);
            }

            // The watch ended cleanly (server closed it) rather than failing; still relist to
            // get a fresh resource version and resync, but this cycle counts as "successful"
            // (not a backoff-worthy failure).
            return true;
        }
        catch (CrdWatchGoneException)
        {
            // 410 Gone is the documented signal to relist, not a failure worth backing off for.
            return true;
        }
        catch (OperationCanceledException) when (!outerToken.IsCancellationRequested)
        {
            // The reconcile-interval deadline hit while watching: the periodic full-relist
            // backstop (doc 08), not a failure.
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException)
        {
            return false;
        }
    }

    private async Task<TimeSpan> DelayAndDoubleBackoffAsync(TimeSpan backoff, CancellationToken token)
    {
        try
        {
            await Task.Delay(backoff, token);
        }
        catch (OperationCanceledException)
        {
            return backoff;
        }

        var doubled = backoff + backoff;
        return doubled > _maxBackoff ? _maxBackoff : doubled;
    }
}
