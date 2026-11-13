// -----------------------------------------------------------------------
// <copyright file="NatsConnectionManager.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Nats.Callout;
using NATS.Client.Core;

namespace AuthCallout.Host.Lifecycle;

/// <summary>
/// Owns the callout's own NATS connection lifecycle and the <see cref="CalloutResponder"/>
/// loop riding on it (doc 05 §Signing and connection identity: "authenticates its own NATS
/// connection with the service user seed"; doc 08 §Multiple instances; ADR 0017 §1). Connects
/// once at start, relies on <c>NATS.Net</c>'s own capped-backoff auto-reconnect
/// (<see cref="NatsOpts.ReconnectWaitMin"/>/<c>ReconnectWaitMax</c>) for resilience, and
/// re-subscribes (same queue group, doc 08: "resubscribes with the same queue group") every
/// time <see cref="INatsConnection.ConnectionOpened"/> fires — including the very first
/// connect and every reconnect — by starting a fresh <see cref="CalloutResponder.RunAsync(INatsConnection,CancellationToken)"/>
/// loop scoped to that connection epoch. <see cref="INatsConnection.ConnectionDisconnected"/>
/// reports <c>false</c> to <see cref="ServiceStateMachine.SetNatsConnected"/> (ADR 0017 §1:
/// "transitions Serving -&gt; Idle... holds no subscription and answers nothing"); the prior
/// epoch's responder loop is left to observe the connection's own drop (its subscription
/// simply stops yielding) rather than being force-cancelled, so any request already
/// mid-flight on the old epoch still gets to finish or hit its own per-request timeout.
/// </summary>
public sealed class NatsConnectionManager : IAsyncDisposable
{
    private readonly NatsConnection _connection;
    private readonly CalloutResponder _responder;
    private readonly ServiceStateMachine _stateMachine;
    private readonly Action<string>? _onDiagnostic;
    private readonly CancellationTokenSource _shutdownCts = new();

    private Task? _responderTask;
    private readonly Lock _epochLock = new();

    /// <summary>Initializes a new instance of the <see cref="NatsConnectionManager"/> class.</summary>
    /// <param name="connection">The service-user NATS connection (unconnected; <see cref="StartAsync"/> connects it).</param>
    /// <param name="responder">The callout responder to run on this connection, re-subscribing on every (re)connect.</param>
    /// <param name="stateMachine">The state machine to report connection state into (ADR 0017 §1).</param>
    /// <param name="onDiagnostic">Optional diagnostic sink for connect/disconnect/reconnect events (logger hook).</param>
    public NatsConnectionManager(
        NatsConnection connection, CalloutResponder responder, ServiceStateMachine stateMachine, Action<string>? onDiagnostic = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(responder);
        ArgumentNullException.ThrowIfNull(stateMachine);

        _connection = connection;
        _responder = responder;
        _stateMachine = stateMachine;
        _onDiagnostic = onDiagnostic;

        _connection.ConnectionOpened += OnConnectionOpenedAsync;
        _connection.ConnectionDisconnected += OnConnectionDisconnectedAsync;
        _connection.ReconnectFailed += OnReconnectFailedAsync;
    }

    /// <summary>Gets the underlying connection, for collaborators that need to publish/subscribe directly (e.g. <c>EventEmitter</c>, P13 wiring).</summary>
    /// <value>The managed NATS connection.</value>
    public INatsConnection Connection => _connection;

    /// <summary>Connects to NATS. The first successful connect fires <see cref="INatsConnection.ConnectionOpened"/>, starting the responder loop.</summary>
    /// <param name="cancellationToken">Cancellation for the initial connect attempt.</param>
    /// <returns>A task that completes once the connection attempt finishes (success or the client's own retry policy takes over).</returns>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _connection.ConnectAsync().AsTask().WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Stops the responder loop and closes the connection (doc 05 Stopping: "close NATS").
    /// Awaits the in-flight responder loop so its own drain (bounded
    /// <c>Parallel.ForEachAsync</c> awaits, doc 05) completes before returning.
    /// </summary>
    /// <returns>A task that completes once the responder loop has drained and the connection is closed.</returns>
    public async ValueTask DisposeAsync()
    {
        _connection.ConnectionOpened -= OnConnectionOpenedAsync;
        _connection.ConnectionDisconnected -= OnConnectionDisconnectedAsync;
        _connection.ReconnectFailed -= OnReconnectFailedAsync;

        await _shutdownCts.CancelAsync();

        Task? responderTask;
        lock (_epochLock)
        {
            responderTask = _responderTask;
        }

        if (responderTask is not null)
        {
            try
            {
                await responderTask;
            }
            catch (OperationCanceledException)
            {
                // Expected: the responder loop observes _shutdownCts.
            }
        }

        await _connection.DisposeAsync();
        _shutdownCts.Dispose();
    }

    private ValueTask OnConnectionOpenedAsync(object? sender, NatsEventArgs args)
    {
        _onDiagnostic?.Invoke("NATS connection opened; (re)subscribing the callout responder.");
        lock (_epochLock)
        {
            _responderTask = Task.Run(() => RunResponderEpochAsync(_shutdownCts.Token));
        }

        _stateMachine.SetNatsConnected(connected: true);
        return ValueTask.CompletedTask;
    }

    private ValueTask OnConnectionDisconnectedAsync(object? sender, NatsEventArgs args)
    {
        _onDiagnostic?.Invoke("NATS connection lost; transitioning to Idle (ADR 0017 §1), auto-reconnect in progress.");
        _stateMachine.SetNatsConnected(connected: false);
        return ValueTask.CompletedTask;
    }

    private ValueTask OnReconnectFailedAsync(object? sender, NatsEventArgs args)
    {
        _onDiagnostic?.Invoke("NATS reconnect attempt failed; will retry with capped backoff.");
        return ValueTask.CompletedTask;
    }

    private async Task RunResponderEpochAsync(CancellationToken shutdownToken)
    {
        try
        {
            await _responder.RunAsync(_connection, shutdownToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown or when the connection drops out from under the loop.
        }
        catch (NatsException ex)
        {
            _onDiagnostic?.Invoke($"Responder loop ended after a NATS-level failure ({ex.GetType().Name}); relying on reconnect to restart it.");
        }
    }
}
