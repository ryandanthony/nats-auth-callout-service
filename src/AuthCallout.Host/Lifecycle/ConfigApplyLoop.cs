// -----------------------------------------------------------------------
// <copyright file="ConfigApplyLoop.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Seams;
using AuthCallout.Contracts.Validation;

namespace AuthCallout.Host.Lifecycle;

/// <summary>
/// Consumes one <see cref="IRoutingConfigProvider"/>'s batches and drives the whole config
/// side of the doc 05 state machine (ADR 0006 §Reload, §Steady-state failure, §Cold start;
/// ADR 0017 §2 dead-watch staleness). Every accepted batch swaps <see cref="ActiveConfigSource.Apply"/>
/// in with a monotonically increasing <see cref="ActiveConfig.Version"/> and reports the
/// bucket-provisioning/keys-and-discovery side effects (P08) via the supplied delegates,
/// exactly once per accepted apply — never on a rejected batch (ADR 0006: "keep last-good,
/// log + metric" is a strict no-op on the active snapshot). Also runs the ADR 0017 staleness
/// clock independent of whether batches are arriving, since "no changes" (provider still
/// alive) and "not syncing" (dead watch) are only distinguishable by polling
/// <see cref="IRoutingConfigProvider.LastSuccessfulSync"/> against a timer, not from the
/// batch stream alone (ADR 0017 §Detect).
/// </summary>
public sealed class ConfigApplyLoop
{
    private readonly IRoutingConfigProvider _provider;
    private readonly BootstrapConfig _bootstrap;
    private readonly ActiveConfigSource _activeConfigSource;
    private readonly ServiceStateMachine _stateMachine;
    private readonly TimeSpan _warnStaleness;
    private readonly TimeSpan _maxStaleness;
    private readonly TimeProvider _timeProvider;
    private readonly Action<string>? _onRejected;
    private readonly Func<ActiveConfig, CancellationToken, Task>? _onApplied;

    private long _nextVersion = 1;

    /// <summary>Initializes a new instance of the <see cref="ConfigApplyLoop"/> class.</summary>
    /// <param name="provider">The single active routing provider (files/k8s/postgres, ADR 0005: exactly one).</param>
    /// <param name="bootstrap">The bootstrap layer, paired unchanged into every <see cref="ActiveConfig"/> snapshot (doc 04: never hot-reloaded).</param>
    /// <param name="activeConfigSource">The swap target for every accepted batch.</param>
    /// <param name="stateMachine">The state machine to report config-availability and freshness into.</param>
    /// <param name="warnStaleness">ADR 0017 warn-staleness threshold.</param>
    /// <param name="maxStaleness">ADR 0017 max-staleness threshold.</param>
    /// <param name="onApplied">Invoked after every accepted apply (P08 bucket provisioning + discovery/keys publication, doc 08 task 3). Optional.</param>
    /// <param name="onRejected">Invoked with a deterministic reason string on every rejected batch (log + metric, ADR 0006). Optional.</param>
    /// <param name="timeProvider">Time source for the staleness clock. Defaults to <see cref="TimeProvider.System"/>.</param>
    public ConfigApplyLoop(
        IRoutingConfigProvider provider,
        BootstrapConfig bootstrap,
        ActiveConfigSource activeConfigSource,
        ServiceStateMachine stateMachine,
        TimeSpan warnStaleness,
        TimeSpan maxStaleness,
        Func<ActiveConfig, CancellationToken, Task>? onApplied = null,
        Action<string>? onRejected = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(bootstrap);
        ArgumentNullException.ThrowIfNull(activeConfigSource);
        ArgumentNullException.ThrowIfNull(stateMachine);

        _provider = provider;
        _bootstrap = bootstrap;
        _activeConfigSource = activeConfigSource;
        _stateMachine = stateMachine;
        _warnStaleness = warnStaleness;
        _maxStaleness = maxStaleness;
        _onApplied = onApplied;
        _onRejected = onRejected;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Gets the deterministic rejection reason from the most recently rejected batch, or null if none (diagnostics/tests).</summary>
    /// <value>The most recent rejection reason, or null.</value>
    public string? LastRejectionReason { get; private set; }

    /// <summary>
    /// Runs the watch loop and the staleness clock concurrently until
    /// <paramref name="cancellationToken"/> is cancelled. Never throws for a rejected batch
    /// (fail-closed per-batch, per ADR 0006) — only cancellation and provider-level faults
    /// propagate.
    /// </summary>
    /// <param name="cancellationToken">Cancellation for the whole loop (shutdown, doc 05 Stopping).</param>
    /// <returns>A task that completes when both the watch and staleness loops stop.</returns>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var stalenessTask = RunStalenessClockAsync(cancellationToken);
        var watchTask = RunWatchAsync(cancellationToken);
        await Task.WhenAll(stalenessTask, watchTask).ConfigureAwait(false);
    }

    private async Task RunWatchAsync(CancellationToken cancellationToken)
    {
        await foreach (var batch in _provider.WatchAsync(cancellationToken).ConfigureAwait(false))
        {
            ApplyOrReject(batch);
        }
    }

    private void ApplyOrReject(RoutingConfigBatch batch)
    {
        var result = ConfigValidator.ValidateActiveConfig(_bootstrap, batch.Routing);
        if (!result.IsValid)
        {
            var reason = string.Join("; ", result.Errors.Select(static e => $"{e.Code}: {e.Message}"));
            LastRejectionReason = reason;
            _onRejected?.Invoke(reason);

            // ADR 0006 §Cold start: an invalid first batch (no last-good yet) stays Idle with
            // no config; an invalid reload with a last-good already active is a strict no-op
            // on the active snapshot (SetConfigAvailable is deliberately not touched here).
            if (_activeConfigSource.Current is null)
            {
                _stateMachine.SetConfigAvailable(available: false);
            }

            return;
        }

        var version = Interlocked.Increment(ref _nextVersion) - 1;
        var snapshot = new ActiveConfig(_bootstrap, batch.Routing, version);
        _activeConfigSource.Apply(snapshot);
        _stateMachine.SetConfigAvailable(available: true);

        if (_onApplied is { } onApplied)
        {
            // Fire-and-forget from the watch loop's perspective: bucket provisioning/keys
            // publication (P08) must not block picking up the next batch, and a failure here
            // is P08's own concern (logged there), never a reason to reject an already-valid
            // config (doc 08 task 3 is a side effect of an accepted apply, not a gate on it).
            _ = InvokeOnAppliedAsync(onApplied, snapshot);
        }
    }

    private static async Task InvokeOnAppliedAsync(Func<ActiveConfig, CancellationToken, Task> onApplied, ActiveConfig snapshot)
    {
        try
        {
            await onApplied(snapshot, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Deliberately swallowed here: P08's own bucket-provisioning/keys-publication
            // path is responsible for its own logging/metrics (doc 08 task 3); this loop's
            // job is only to never let that failure take down config watching.
        }
    }

    private async Task RunStalenessClockAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), _timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                UpdateFreshness();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    private void UpdateFreshness()
    {
        var lastSync = _provider.LastSuccessfulSync;
        if (lastSync is null)
        {
            // No successful sync yet (doc 05 cold start): there is nothing stale to report,
            // ConfigAvailable=false already keeps the state machine out of Serving.
            _stateMachine.SetFreshness(ConfigFreshness.Fresh);
            return;
        }

        var age = _timeProvider.GetUtcNow() - lastSync.Value;
        var freshness = age >= _maxStaleness
            ? ConfigFreshness.StaleUnready
            : age >= _warnStaleness
                ? ConfigFreshness.StaleDegraded
                : ConfigFreshness.Fresh;
        _stateMachine.SetFreshness(freshness);
    }
}
