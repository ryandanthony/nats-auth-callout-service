// -----------------------------------------------------------------------
// <copyright file="ScanTrigger.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Config.Files.Scanning;

/// <summary>
/// Coalesces scan requests into a single pending signal, with a debounce window for
/// file-system-event-triggered requests (editors commonly emit several write events per save)
/// and an immediate path for requests that should bypass debouncing (the initial scan, and the
/// periodic ADR 0017 reconciliation relist). At most one scan is ever "pending" at a time: a
/// burst of triggers while a scan is already pending collapses to one further scan.
/// </summary>
internal sealed class ScanTrigger : IDisposable
{
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly Timer _debounceTimer;
    private readonly TimeSpan _debounceWindow;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="ScanTrigger"/> class.</summary>
    /// <param name="debounceWindow">The debounce window for <see cref="SignalDebounced"/>.</param>
    internal ScanTrigger(TimeSpan debounceWindow)
    {
        _debounceWindow = debounceWindow;
        _debounceTimer = new Timer(_ => SignalImmediately(), state: null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Requests a scan immediately, bypassing the debounce window.</summary>
    internal void SignalImmediately()
    {
        if (_signal.CurrentCount == 0)
        {
            try
            {
                _signal.Release();
            }
            catch (SemaphoreFullException)
            {
                // Raced with another releaser between the CurrentCount check and Release(); a
                // scan is already signalled either way.
            }
        }
    }

    /// <summary>
    /// (Re)starts the debounce window; the pending scan fires <see cref="SignalImmediately"/>
    /// once no further call arrives within the window. Intended for file-system-event triggers.
    /// </summary>
    internal void SignalDebounced() => _debounceTimer.Change(_debounceWindow, Timeout.InfiniteTimeSpan);

    /// <summary>
    /// Waits for the next pending scan signal.
    /// </summary>
    /// <param name="token">Cancellation token.</param>
    /// <returns>True when a scan was signalled; false when <paramref name="token"/> was cancelled first.</returns>
    internal async Task<bool> WaitAsync(CancellationToken token)
    {
        try
        {
            await _signal.WaitAsync(token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _debounceTimer.Dispose();
        _signal.Dispose();
    }
}
