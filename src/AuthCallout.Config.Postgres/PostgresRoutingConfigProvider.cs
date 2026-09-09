// -----------------------------------------------------------------------
// <copyright file="PostgresRoutingConfigProvider.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Runtime.CompilerServices;
using AuthCallout.Config.Postgres.Aggregation;
using AuthCallout.Config.Postgres.Repository;
using AuthCallout.Contracts.Seams;

namespace AuthCallout.Config.Postgres;

/// <summary>
/// <see cref="IRoutingConfigProvider"/> over PostgreSQL (ADR 0005 §Sources "Database", QUESTIONS
/// A2, P12). Polls <see cref="IAuthApplicationRepository.ListEnabledAsync"/> on
/// <c>PostgresSourceSettings.PollInterval</c> and re-aggregates + validates the <b>full</b>
/// enabled row set on every poll (ADR 0006 atomicity: one bad row rejects the whole batch, this
/// type never yields a partially-valid aggregate). The poll loop is the load-bearing
/// change-detection mechanism and the sole <see cref="LastSuccessfulSync"/> heartbeat (ADR 0017:
/// it advances on every successful poll, changed or not); <see cref="IAuthApplicationRepository.ListenForChangesAsync"/>
/// is layered on top purely as a latency optimization that wakes the poll early — its complete
/// absence (LISTEN unsupported/unavailable) never affects correctness, only responsiveness.
/// </summary>
public sealed class PostgresRoutingConfigProvider : IRoutingConfigProvider
{
    private readonly IAuthApplicationRepository _repository;
    private readonly TimeSpan _pollInterval;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _disposalCts = new();

    private long _lastSuccessfulSyncTicks = -1;
    private bool _disposed;
    private bool _watchStarted;

    /// <summary>Initializes a new instance of the <see cref="PostgresRoutingConfigProvider"/> class.</summary>
    /// <param name="repository">The repository seam to poll/listen through.</param>
    /// <param name="pollInterval">How often to poll for changes; also the staleness heartbeat cadence (ADR 0017).</param>
    /// <param name="timeProvider">The time source for <see cref="RoutingConfigBatch.ObservedAt"/> and <see cref="LastSuccessfulSync"/>. Defaults to <see cref="TimeProvider.System"/>.</param>
    public PostgresRoutingConfigProvider(IAuthApplicationRepository repository, TimeSpan pollInterval, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        if (pollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval), pollInterval, "Poll interval must be positive.");
        }

        _repository = repository;
        _pollInterval = pollInterval;
        _timeProvider = timeProvider ?? TimeProvider.System;
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
    /// Gets the deterministic error from the most recent rejected poll (ADR 0006), or null when
    /// the most recent poll succeeded, failed transiently (a connection error, distinct from a
    /// bad row), or none has run yet. Exposed for diagnostics/tests.
    /// </summary>
    /// <value>The most recent poll's rejection error, or null.</value>
    public string? LastError { get; private set; }

    /// <inheritdoc />
    public IAsyncEnumerable<RoutingConfigBatch> WatchAsync(CancellationToken cancellationToken)
    {
        // Guard eagerly, on the calling thread: an `async IAsyncEnumerable` method's body does
        // not execute until the first `MoveNextAsync` (the same lesson learned building P10/P11's
        // providers), so these checks would otherwise silently wait for enumeration instead of
        // failing the call that violates the contract.
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
    }

    private async IAsyncEnumerable<RoutingConfigBatch> WatchCoreAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposalCts.Token);
        var token = linkedCts.Token;

        using var pollTrigger = new PollTrigger(_pollInterval);
        var listenTask = RunListenBridgeAsync(pollTrigger, token);

        // Initial poll fires immediately so the first valid config is available as fast as
        // possible after startup (doc 05/ADR 0006 cold start).
        pollTrigger.SignalImmediately();

        try
        {
            while (true)
            {
                var signalled = await pollTrigger.WaitAsync(token).ConfigureAwait(false);
                if (!signalled)
                {
                    yield break;
                }

                var batch = await TryPollAsync(token).ConfigureAwait(false);
                if (batch is not null)
                {
                    yield return batch;
                }
            }
        }
        finally
        {
            await StopListenBridgeAsync(listenTask).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Bridges <see cref="IAuthApplicationRepository.ListenForChangesAsync"/> notifications into
    /// early poll triggers. Any failure here (LISTEN unsupported, connection dropped) is silently
    /// absorbed — the poll loop keeps running on its own regardless (this is a latency
    /// optimization, never load-bearing).
    /// </summary>
    private async Task RunListenBridgeAsync(PollTrigger pollTrigger, CancellationToken token)
    {
        try
        {
            await foreach (var signal in _repository.ListenForChangesAsync(token).ConfigureAwait(false))
            {
                _ = signal;
                pollTrigger.SignalImmediately();
            }
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException)
        {
            // LISTEN unavailable/failed: the poll loop is unaffected, so there is nothing more
            // to do here than stop bridging notifications.
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    private async Task<RoutingConfigBatch?> TryPollAsync(CancellationToken token)
    {
        IReadOnlyList<Repository.AuthApplicationRow> rows;
        try
        {
            rows = await _repository.ListEnabledAsync(token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException)
        {
            // A connection failure is distinct from a bad row (ADR 0006 vs ADR 0017): it does
            // not advance LastSuccessfulSync and does not overwrite LastError with a validation
            // message, since the last-good config is still exactly as valid as it was.
            return null;
        }

        var decision = RowAggregator.Aggregate(rows);
        var now = _timeProvider.GetUtcNow();
        Interlocked.Exchange(ref _lastSuccessfulSyncTicks, now.UtcTicks);

        LastError = decision.IsSuccess ? null : decision.Error;

        return decision.IsSuccess ? new RoutingConfigBatch(decision.Routing!, now) : null;
    }

    private static async Task StopListenBridgeAsync(Task listenTask)
    {
        try
        {
#pragma warning disable VSTHRD003 // listenTask was already started by this instance before being passed here; awaiting it is the intended join point.
            await listenTask.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }
}
