// -----------------------------------------------------------------------
// <copyright file="PollTrigger.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Config.Postgres;

/// <summary>
/// Coalesces poll requests into a single pending signal: the periodic timer and early wake-ups
/// from <see cref="Repository.IAuthApplicationRepository.ListenForChangesAsync"/> notifications
/// both call <see cref="SignalImmediately"/>, and at most one poll is ever "pending" — a burst
/// of triggers while a poll is already pending collapses to one further poll.
/// </summary>
internal sealed class PollTrigger : IDisposable
{
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly Timer _pollTimer;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="PollTrigger"/> class.</summary>
    /// <param name="pollInterval">The periodic poll cadence.</param>
    internal PollTrigger(TimeSpan pollInterval)
    {
        _pollTimer = new Timer(_ => SignalImmediately(), state: null, pollInterval, pollInterval);
    }

    /// <summary>Requests a poll immediately.</summary>
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
                // Raced with another releaser; a poll is already signalled either way.
            }
        }
    }

    /// <summary>Waits for the next pending poll signal.</summary>
    /// <param name="token">Cancellation token.</param>
    /// <returns>True when a poll was signalled; false when <paramref name="token"/> was cancelled first.</returns>
    internal async Task<bool> WaitAsync(CancellationToken token)
    {
        try
        {
            await _signal.WaitAsync(token);
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
        _pollTimer.Dispose();
        _signal.Dispose();
    }
}
