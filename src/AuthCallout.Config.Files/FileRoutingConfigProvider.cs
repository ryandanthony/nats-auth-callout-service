// -----------------------------------------------------------------------
// <copyright file="FileRoutingConfigProvider.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Runtime.CompilerServices;
using AuthCallout.Config.Files.Scanning;
using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Seams;

namespace AuthCallout.Config.Files;

/// <summary>
/// <see cref="IRoutingConfigProvider"/> over a watched directory of per-application JSON/YAML
/// files (ADR 0005 §Sources, doc 04 §Files, doc 09 §3, P10). Reads all
/// <c>*.yaml</c>/<c>*.yml</c>/<c>*.json</c> files directly under the configured directory
/// (non-recursive, pinned by doc 09), maps each into the canonical <c>Application</c> model,
/// aggregates, and validates before ever emitting a batch (ADR 0006: a single offending file
/// rejects the whole batch; this type never yields a partially-valid aggregate) — all via
/// <see cref="Scanning.DirectoryScanner"/>. Watches for changes via
/// <see cref="Scanning.DirectoryWatcherSupervisor"/> (debounced, since editors commonly emit
/// several write events per save) plus a periodic reconciliation relist that is the ADR 0017
/// staleness backstop: it advances <see cref="LastSuccessfulSync"/> on every successful scan,
/// changed or not, and survives a dead/missed watcher event, since the watcher supervisor
/// recreates a dead watcher independently and the reconciliation timer here runs regardless of
/// watcher health.
/// </summary>
public sealed class FileRoutingConfigProvider : IRoutingConfigProvider
{
    private static readonly TimeSpan DefaultDebounceWindow = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan DefaultReconcileInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DefaultMinWatcherRecreateBackoff = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan DefaultMaxWatcherRecreateBackoff = TimeSpan.FromSeconds(30);

    private readonly DirectoryScanner _scanner;
    private readonly DirectoryWatcherSupervisor _watcherSupervisor;
    private readonly TimeSpan _debounceWindow;
    private readonly TimeSpan _reconcileInterval;
    private readonly CancellationTokenSource _disposalCts = new();

    private long _lastSuccessfulSyncTicks = -1;
    private bool _disposed;
    private bool _watchStarted;

    /// <summary>Initializes a new instance of the <see cref="FileRoutingConfigProvider"/> class.</summary>
    /// <param name="settings">The files-source settings (directory to watch).</param>
    /// <param name="timeProvider">The time source for <see cref="RoutingConfigBatch.ObservedAt"/> and <see cref="LastSuccessfulSync"/>. Defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="debounceWindow">
    /// How long to wait after the last file-system event before rescanning, coalescing bursts
    /// of editor write events into one batch. Defaults to <c>300ms</c>.
    /// </param>
    /// <param name="reconcileInterval">
    /// The periodic relist cadence that backstops missed watcher events and advances
    /// <see cref="LastSuccessfulSync"/> even when nothing changed (ADR 0017). Defaults to <c>30s</c>.
    /// </param>
    /// <param name="minWatcherRecreateBackoff">The initial delay before recreating a dead watcher. Defaults to <c>500ms</c>.</param>
    /// <param name="maxWatcherRecreateBackoff">The cap on watcher-recreate backoff. Defaults to <c>30s</c>.</param>
    public FileRoutingConfigProvider(
        FilesSourceSettings settings,
        TimeProvider? timeProvider = null,
        TimeSpan? debounceWindow = null,
        TimeSpan? reconcileInterval = null,
        TimeSpan? minWatcherRecreateBackoff = null,
        TimeSpan? maxWatcherRecreateBackoff = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.Directory);

        var effectiveTimeProvider = timeProvider ?? TimeProvider.System;
        _scanner = new DirectoryScanner(settings.Directory, effectiveTimeProvider);
        _watcherSupervisor = new DirectoryWatcherSupervisor(
            settings.Directory,
            minWatcherRecreateBackoff ?? DefaultMinWatcherRecreateBackoff,
            maxWatcherRecreateBackoff ?? DefaultMaxWatcherRecreateBackoff);
        _debounceWindow = debounceWindow ?? DefaultDebounceWindow;
        _reconcileInterval = reconcileInterval ?? DefaultReconcileInterval;
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
    /// Gets the deterministic error from the most recent rejected scan (naming the offending
    /// file(s), ADR 0006), or null when the most recent scan succeeded or none has run yet.
    /// Exposed for diagnostics/tests; production error surfacing (logging, metrics) is a
    /// caller concern layered on top of this provider.
    /// </summary>
    /// <value>The most recent scan's rejection error, or null.</value>
    public string? LastError => _scanner.LastError;

    /// <summary>
    /// Gets the schema-version-migration warnings from the most recent scan (doc 09 §3: a file
    /// missing <c>schemaVersion</c> is treated as <c>1</c> during the migration window, with a
    /// warning), regardless of whether that scan's batch was ultimately accepted or rejected.
    /// </summary>
    /// <value>The most recent scan's migration warnings.</value>
    public IReadOnlyList<string> LastWarnings => _scanner.LastWarnings;

    /// <inheritdoc />
    public IAsyncEnumerable<RoutingConfigBatch> WatchAsync(CancellationToken cancellationToken)
    {
        // Guard eagerly, on the calling thread: an `async IAsyncEnumerable` method's body does
        // not execute until the first `MoveNextAsync`, so these checks would otherwise silently
        // wait for enumeration instead of failing the call that violates the contract.
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_watchStarted)
        {
            throw new InvalidOperationException($"{nameof(WatchAsync)} was already called on this provider instance; each provider instance watches once.");
        }

        _watchStarted = true;
        return WatchCoreAsync(cancellationToken);
    }

    private async IAsyncEnumerable<RoutingConfigBatch> WatchCoreAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposalCts.Token);
        var token = linkedCts.Token;

        using var scanTrigger = new ScanTrigger(_debounceWindow);
        await using var reconcileTimer = new Timer(_ => scanTrigger.SignalImmediately(), state: null, _reconcileInterval, _reconcileInterval);

        var watcherSupervisorTask = _watcherSupervisor.RunAsync(scanTrigger.SignalDebounced, token);

        // Initial scan fires immediately, bypassing the debounce window, so the first valid
        // config is available as fast as possible after startup (doc 05/ADR 0006 cold start).
        scanTrigger.SignalImmediately();

        try
        {
            await foreach (var batch in ConsumeScansAsync(scanTrigger, token))
            {
                yield return batch;
            }
        }
        finally
        {
            await StopWatcherSupervisorAsync(watcherSupervisorTask);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _disposalCts.CancelAsync();
        _disposalCts.Dispose();
    }

    private async IAsyncEnumerable<RoutingConfigBatch> ConsumeScansAsync(ScanTrigger scanTrigger, [EnumeratorCancellation] CancellationToken token)
    {
        while (true)
        {
            var signalled = await scanTrigger.WaitAsync(token);
            if (!signalled)
            {
                yield break;
            }

            var batch = TryScanCatchingIoErrors();
            if (batch is not null)
            {
                Interlocked.Exchange(ref _lastSuccessfulSyncTicks, batch.ObservedAt.UtcTicks);
                yield return batch;
            }
        }
    }

    private RoutingConfigBatch? TryScanCatchingIoErrors()
    {
        try
        {
            return _scanner.Scan();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The scanner already set its own LastError for a rejected batch; an I/O failure
            // reading the directory itself is caught here so a transient/missing directory
            // never crashes the watch loop (ADR 0017: watch loss keeps serving last-good).
            return null;
        }
    }

    private static async Task StopWatcherSupervisorAsync(Task supervisorTask)
    {
        try
        {
#pragma warning disable VSTHRD003 // supervisorTask was already started (and is running) by the caller before being passed in here; awaiting it is the intended join point, not a foreign/unstarted task.
            await supervisorTask;
#pragma warning restore VSTHRD003
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown: the supervisor observes the cancelled token and returns.
        }
    }
}
