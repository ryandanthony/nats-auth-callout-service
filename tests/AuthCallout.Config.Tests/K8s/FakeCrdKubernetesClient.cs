// -----------------------------------------------------------------------
// <copyright file="FakeCrdKubernetesClient.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Threading.Channels;
using AuthCallout.Config.K8s.Client;

namespace AuthCallout.Config.Tests.K8s;

/// <summary>
/// A scriptable fake <see cref="ICrdKubernetesClient"/> for unit-testing
/// <see cref="AuthCallout.Config.K8s.CrdRoutingConfigProvider"/> and
/// <see cref="AuthCallout.Config.K8s.CrdWatchSupervisor"/> against a faked watch-event stream,
/// without a cluster or KubernetesClient (P11 packet §Testing strategy). Each <see cref="ListAsync"/>
/// call consumes the next queued <see cref="QueueList"/> result (or blocks/throws per
/// configuration); each subsequent <see cref="WatchAsync"/> call gets an independent channel that
/// the test feeds events into via <see cref="Emit"/>, <see cref="EmitGone"/>, or
/// <see cref="CompleteWatch"/>.
/// </summary>
public sealed class FakeCrdKubernetesClient : ICrdKubernetesClient
{
    private readonly Queue<Func<CrdListResult>> _listResults = new();
    private readonly List<StatusPatchRecord> _statusPatches = [];
    private readonly Lock _gate = new();

    private Channel<CrdChangeEvent>? _currentWatchChannel;
    private CrdWatchGoneException? _currentWatchGoneException;

    /// <summary>Gets every status patch this fake received, in call order. Thread-safe snapshot.</summary>
    /// <value>The recorded status patches.</value>
    public IReadOnlyList<StatusPatchRecord> StatusPatches
    {
        get
        {
            lock (_gate)
            {
                return _statusPatches.ToList();
            }
        }
    }

    /// <summary>Gets the number of times <see cref="ListAsync"/> has been called.</summary>
    /// <value>The list call count.</value>
    public int ListCallCount { get; private set; }

    /// <summary>Queues the result of the next <see cref="ListAsync"/> call.</summary>
    /// <param name="resources">The resources the list should return.</param>
    /// <param name="resourceVersion">The resource version the list should return.</param>
    public void QueueList(IReadOnlyList<CustomResourceRecord> resources, string resourceVersion) =>
        _listResults.Enqueue(() => new CrdListResult(resources, resourceVersion));

    /// <summary>Queues the next <see cref="ListAsync"/> call to throw (simulating a transport failure).</summary>
    public void QueueListFailure() =>
        _listResults.Enqueue(static () => throw new IOException("Simulated list failure."));

    /// <inheritdoc />
    public Task<CrdListResult> ListAsync(CancellationToken cancellationToken)
    {
        ListCallCount++;
        if (_listResults.Count == 0)
        {
            throw new InvalidOperationException("No queued list result; call QueueList first.");
        }

        var factory = _listResults.Dequeue();

        // A new watch begins fresh after every list, mirroring the real client (each ListAsync
        // is followed by a new WatchAsync starting from that list's resource version).
        lock (_gate)
        {
            _currentWatchChannel = Channel.CreateUnbounded<CrdChangeEvent>();
            _currentWatchGoneException = null;
        }

        return Task.FromResult(factory());
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<CrdChangeEvent> WatchAsync(
        string resourceVersion, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Channel<CrdChangeEvent> channel;
        lock (_gate)
        {
            channel = _currentWatchChannel ?? throw new InvalidOperationException("WatchAsync called before ListAsync.");
        }

        await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }

        CrdWatchGoneException? gone;
        lock (_gate)
        {
            gone = _currentWatchGoneException;
        }

        if (gone is not null)
        {
            throw gone;
        }
    }

    /// <inheritdoc />
    public Task PatchStatusAsync(
        CustomResourceRecord resource, long observedGeneration, bool accepted, string reason, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _statusPatches.Add(new StatusPatchRecord(resource, observedGeneration, accepted, reason));
        }

        return Task.CompletedTask;
    }

    /// <summary>Emits one change event on the current watch (must be called after the corresponding <see cref="ListAsync"/> started a watch).</summary>
    /// <param name="changeEvent">The event to emit.</param>
    public void Emit(CrdChangeEvent changeEvent)
    {
        Channel<CrdChangeEvent> channel;
        lock (_gate)
        {
            channel = _currentWatchChannel ?? throw new InvalidOperationException("No active watch to emit on; call ListAsync first.");
        }

        var written = channel.Writer.TryWrite(changeEvent);
        if (!written)
        {
            throw new InvalidOperationException("Failed to write to the fake watch channel.");
        }
    }

    /// <summary>Ends the current watch with a simulated <see cref="CrdWatchGoneException"/> (410 Gone).</summary>
    public void EmitGone()
    {
        lock (_gate)
        {
            _currentWatchGoneException = new CrdWatchGoneException();
            _currentWatchChannel?.Writer.TryComplete();
        }
    }

    /// <summary>Ends the current watch cleanly (as if the server closed the connection without error).</summary>
    public void CompleteWatch()
    {
        lock (_gate)
        {
            _currentWatchChannel?.Writer.TryComplete();
        }
    }
}
