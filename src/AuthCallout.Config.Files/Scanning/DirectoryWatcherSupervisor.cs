// -----------------------------------------------------------------------
// <copyright file="DirectoryWatcherSupervisor.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Config.Files.Scanning;

/// <summary>
/// Owns a <see cref="FileSystemWatcher"/>'s lifecycle over a directory: creates it, invokes a
/// callback on every change/create/delete/rename, and recreates it with capped exponential
/// backoff whenever it dies (its <c>Error</c> event fires, or creation itself fails because the
/// directory is temporarily missing/unreadable). Runs independently of any periodic
/// reconciliation the caller layers on top (doc 09/ADR 0017): a watcher outage here does not
/// prevent the caller's own backstop relist from continuing to run and advance
/// <c>LastSuccessfulSync</c>.
/// </summary>
internal sealed class DirectoryWatcherSupervisor
{
    private static readonly string[] ConfigFilePatterns = ["*.yaml", "*.yml", "*.json"];

    private readonly string _directory;
    private readonly TimeSpan _minBackoff;
    private readonly TimeSpan _maxBackoff;

    /// <summary>Initializes a new instance of the <see cref="DirectoryWatcherSupervisor"/> class.</summary>
    /// <param name="directory">The directory to watch.</param>
    /// <param name="minBackoff">The initial delay before recreating a dead watcher.</param>
    /// <param name="maxBackoff">The cap on watcher-recreate backoff.</param>
    internal DirectoryWatcherSupervisor(string directory, TimeSpan minBackoff, TimeSpan maxBackoff)
    {
        _directory = directory;
        _minBackoff = minBackoff;
        _maxBackoff = maxBackoff;
    }

    /// <summary>
    /// Runs the supervise-and-recreate loop until <paramref name="token"/> is cancelled,
    /// invoking <paramref name="onFileEvent"/> on every raised watcher event.
    /// </summary>
    /// <param name="onFileEvent">Called on every raised change/create/delete/rename event.</param>
    /// <param name="token">Cancellation to stop supervising.</param>
    /// <returns>A task that completes once <paramref name="token"/> is cancelled.</returns>
    internal async Task RunAsync(Action onFileEvent, CancellationToken token)
    {
        var backoff = _minBackoff;

        while (!token.IsCancellationRequested)
        {
            var (watcher, created) = TryCreateWatcher(onFileEvent);
            if (!created)
            {
                backoff = await DelayAndDoubleBackoffAsync(backoff, token);
                continue;
            }

            backoff = _minBackoff;
            await RunUntilDeadOrCancelledAsync(watcher!, token);

            if (token.IsCancellationRequested)
            {
                return;
            }

            backoff = await DelayAndDoubleBackoffAsync(backoff, token);
        }
    }

    private (FileSystemWatcher? watcher, bool created) TryCreateWatcher(Action onFileEvent)
    {
        try
        {
            return (CreateWatcher(onFileEvent), true);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // Directory missing/unreadable at watcher-creation time; the caller's own
            // reconciliation relist will surface the same problem on its own cadence. Keep
            // retrying watcher creation independently.
            return (null, false);
        }
    }

    private static async Task RunUntilDeadOrCancelledAsync(FileSystemWatcher watcher, CancellationToken token)
    {
        using (watcher)
        {
            var died = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            watcher.Error += (_, _) => died.TrySetResult();

#pragma warning disable VSTHRD003 // Awaiting the watcher's own lifetime signal (die-or-cancel), not a foreign in-flight task; this is the intended synchronization point.
            await using var registration = token.Register(static state => ((TaskCompletionSource)state!).TrySetResult(), died);
            await died.Task;
#pragma warning restore VSTHRD003
        }
    }

    private FileSystemWatcher CreateWatcher(Action onFileEvent)
    {
        var watcher = new FileSystemWatcher(_directory)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            IncludeSubdirectories = false,
        };

        foreach (var pattern in ConfigFilePatterns)
        {
            watcher.Filters.Add(pattern);
        }

        watcher.Changed += (_, _) => onFileEvent();
        watcher.Created += (_, _) => onFileEvent();
        watcher.Deleted += (_, _) => onFileEvent();
        watcher.Renamed += (_, _) => onFileEvent();

        watcher.EnableRaisingEvents = true;
        return watcher;
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
