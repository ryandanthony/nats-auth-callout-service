// -----------------------------------------------------------------------
// <copyright file="CrdRoutingConfigProvider.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Runtime.CompilerServices;
using AuthCallout.Config.K8s.Aggregation;
using AuthCallout.Config.K8s.Client;
using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Seams;

namespace AuthCallout.Config.K8s;

/// <summary>
/// <see cref="IRoutingConfigProvider"/> over a cluster-wide watched <c>AuthApplication</c>
/// Custom Resource (ADR 0005 §Sources, doc 04 §Kubernetes, doc 09 §4, P11). Lists and watches
/// every CR matching the instance's owner label selector across all namespaces (doc 04: "CRs may
/// live in any namespace") via <see cref="ICrdKubernetesClient"/>, tracks the owned set in an
/// <see cref="Aggregation.OwnedResourceSet"/>, and re-aggregates + validates the <b>full</b>
/// owned set on every add/modify/delete and every successful relist (ADR 0006 atomicity: one
/// offending CR rejects the whole batch, this type never yields a partially-valid aggregate).
/// After each aggregation decision, patches every tracked CR's <c>status</c> subresource
/// (accepted/rejected/observedGeneration, doc 09 §4); status write failures are logged by the
/// caller and never fatal to the watch loop. <see cref="LastSuccessfulSync"/> advances on every
/// successful list/relist regardless of whether the aggregation was accepted, so staleness
/// (ADR 0017) is driven purely by "can I still talk to the API server", not by "is my config
/// valid".
/// </summary>
public sealed class CrdRoutingConfigProvider : IRoutingConfigProvider
{
    private static readonly TimeSpan DefaultMinBackoff = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DefaultMaxBackoff = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultReconcileInterval = TimeSpan.FromMinutes(5);

    private readonly ICrdKubernetesClient _client;
    private readonly CrdWatchSupervisor _supervisor;
    private readonly OwnedResourceSet _ownedResources = new();
    private readonly CancellationTokenSource _disposalCts = new();
    private readonly TimeProvider _timeProvider;
    private readonly bool _disposeClient;

    private long _lastSuccessfulSyncTicks = -1;
    private bool _disposed;
    private bool _watchStarted;

    /// <summary>Initializes a new instance of the <see cref="CrdRoutingConfigProvider"/> class over the real Kubernetes cluster.</summary>
    /// <param name="settings">The Kubernetes-source settings (owner label selector).</param>
    /// <param name="minBackoff">The initial relist/rewatch backoff after a failure. Defaults to <c>1s</c>.</param>
    /// <param name="maxBackoff">The cap on relist/rewatch backoff. Defaults to <c>30s</c>.</param>
    /// <param name="reconcileInterval">The periodic full-relist cadence. Defaults to <c>5m</c>.</param>
    public CrdRoutingConfigProvider(
        K8sSourceSettings settings, TimeSpan? minBackoff = null, TimeSpan? maxBackoff = null, TimeSpan? reconcileInterval = null)
        : this(CreateDefaultClient(settings), disposeClient: true, minBackoff: minBackoff, maxBackoff: maxBackoff, reconcileInterval: reconcileInterval)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CrdRoutingConfigProvider"/> class over a caller-supplied client (tests, k3s Testcontainers).</summary>
    /// <param name="client">The Kubernetes client seam to list/watch/patch through.</param>
    /// <param name="disposeClient">Whether this instance disposes <paramref name="client"/> when it is itself disposed.</param>
    /// <param name="minBackoff">The initial relist/rewatch backoff after a failure. Defaults to <c>1s</c>.</param>
    /// <param name="maxBackoff">The cap on relist/rewatch backoff. Defaults to <c>30s</c>.</param>
    /// <param name="reconcileInterval">The periodic full-relist cadence. Defaults to <c>5m</c>.</param>
    /// <param name="timeProvider">The time source for <see cref="RoutingConfigBatch.ObservedAt"/> and <see cref="LastSuccessfulSync"/>. Defaults to <see cref="TimeProvider.System"/>.</param>
    public CrdRoutingConfigProvider(
        ICrdKubernetesClient client,
        bool disposeClient = false,
        TimeSpan? minBackoff = null,
        TimeSpan? maxBackoff = null,
        TimeSpan? reconcileInterval = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _disposeClient = disposeClient;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _supervisor = new CrdWatchSupervisor(
            client,
            minBackoff ?? DefaultMinBackoff,
            maxBackoff ?? DefaultMaxBackoff,
            reconcileInterval ?? DefaultReconcileInterval);
    }

    /// <inheritdoc />
    public DateTimeOffset? LastSuccessfulSync
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastSuccessfulSyncTicks);
            return ticks < 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    /// <summary>
    /// Gets the deterministic error from the most recent rejected aggregation (ADR 0006), or
    /// null when the most recent aggregation succeeded or none has run yet. Exposed for
    /// diagnostics/tests; production error surfacing (logging, metrics) is a caller concern.
    /// </summary>
    /// <value>The most recent aggregation's rejection error, or null.</value>
    public string? LastError { get; private set; }

    /// <inheritdoc />
    public IAsyncEnumerable<RoutingConfigBatch> WatchAsync(CancellationToken cancellationToken)
    {
        // Guard eagerly, on the calling thread: an `async IAsyncEnumerable` method's body does
        // not execute until the first `MoveNextAsync` (P10 lesson learned the same way), so
        // these checks would otherwise silently wait for enumeration instead of failing the
        // call that violates the contract.
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_watchStarted)
        {
            throw new InvalidOperationException($"{nameof(WatchAsync)} was already called on this provider instance; each provider instance watches once.");
        }

        _watchStarted = true;
        return WatchCoreAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _disposalCts.CancelAsync().ConfigureAwait(false);
        _disposalCts.Dispose();

        if (_disposeClient && _client is IDisposable disposableClient)
        {
            disposableClient.Dispose();
        }
    }

    private static ICrdKubernetesClient CreateDefaultClient(K8sSourceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new CrdKubernetesClient(settings.OwnerLabelSelector);
    }

    private async IAsyncEnumerable<RoutingConfigBatch> WatchCoreAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposalCts.Token);
        var token = linkedCts.Token;

        using var pendingBatches = new PendingBatchChannel();
        var supervisorTask = _supervisor.RunAsync(
            resources => OnResync(resources, pendingBatches, token),
            changeEvent => OnChange(changeEvent, pendingBatches, token),
            token);

        try
        {
            while (true)
            {
                var batch = await pendingBatches.TakeAsync(token).ConfigureAwait(false);
                if (batch is null)
                {
                    yield break;
                }

                yield return batch;
            }
        }
        finally
        {
            await StopSupervisorAsync(supervisorTask).ConfigureAwait(false);
        }
    }

    private void OnResync(IReadOnlyList<CustomResourceRecord> resources, PendingBatchChannel pendingBatches, CancellationToken token)
    {
        _ownedResources.Reset(resources);
        ProcessAggregation(pendingBatches, token);
    }

    private void OnChange(CrdChangeEvent changeEvent, PendingBatchChannel pendingBatches, CancellationToken token)
    {
        _ownedResources.Apply(changeEvent);
        ProcessAggregation(pendingBatches, token);
    }

    private void ProcessAggregation(PendingBatchChannel pendingBatches, CancellationToken token)
    {
        var decision = _ownedResources.Aggregate();
        var now = _timeProvider.GetUtcNow();
        Interlocked.Exchange(ref _lastSuccessfulSyncTicks, now.UtcTicks);

        LastError = decision.IsSuccess ? null : decision.Error;

        if (decision.IsSuccess)
        {
            pendingBatches.Publish(new RoutingConfigBatch(decision.Routing!, now));
        }

        WriteStatusesFireAndForget(decision.Statuses, token);
    }

    /// <summary>
    /// Patches every tracked CR's status without blocking the aggregation/emit path (P11 packet:
    /// "Status write failures are logged, never fatal" — and never on the hot path either, since
    /// a slow API server for status writes must not stall new batches from being detected).
    /// </summary>
    private void WriteStatusesFireAndForget(IReadOnlyList<CustomResourceStatusDecision> statuses, CancellationToken token)
    {
        foreach (var status in statuses)
        {
            _ = WriteOneStatusAsync(status, token);
        }
    }

    private async Task WriteOneStatusAsync(CustomResourceStatusDecision status, CancellationToken token)
    {
        try
        {
            await _client.PatchStatusAsync(
                status.Resource, status.Resource.Generation, status.Accepted, status.Reason, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException or OperationCanceledException)
        {
            // Logged by the caller in production wiring; the packet's own contract
            // (ICrdKubernetesClient.PatchStatusAsync) already swallows ordinary API rejections,
            // this guards the fire-and-forget task itself against an unobserved-exception crash.
        }
    }

    private static async Task StopSupervisorAsync(Task supervisorTask)
    {
        try
        {
#pragma warning disable VSTHRD003 // supervisorTask was already started (and is running) by the caller before being passed in here; awaiting it is the intended join point, not a foreign/unstarted task.
            await supervisorTask.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown: the supervisor observes the cancelled token and returns.
        }
    }
}
