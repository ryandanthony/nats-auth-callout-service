// -----------------------------------------------------------------------
// <copyright file="CalloutNatsConnection.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Tokens;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NATS.Client.Core;

namespace AuthCallout.Client.Connection;

/// <summary>
/// A managed NATS connection through the callout implementing ADR 0021's retry and
/// auth-violation semantics exactly:
/// <list type="number">
/// <item>Transport failures (server down, network): retry indefinitely with exponential
/// backoff and jitter (<see cref="ConnectBackoff"/>).</item>
/// <item>An Authorization Violation server error (detected by walking inner exceptions,
/// <see cref="AuthViolationDetector"/>): invalidate the cached token, force a refresh, retry
/// exactly once, then fail terminally.</item>
/// <item>No token available on the authenticated path: fail immediately with
/// <see cref="CalloutAuthenticationRequiredException"/>, never falling through to anonymous.</item>
/// <item>Reconnects are owned by this type, not NATS.Net: the underlying <c>NatsOpts</c> always
/// has <c>MaxReconnectRetry = 0</c> (set by <see cref="CalloutNatsOpts.CreateAsync"/>), and this
/// type's own handler on <c>ConnectionDisconnected</c> re-enters the connect pipeline, fetching
/// a current token and recomputing the scoped inbox prefix each time.</item>
/// <item>Token refresh is proactive, delegated entirely to the configured
/// <see cref="ICalloutTokenProvider"/> (e.g. <see cref="ClientCredentialsTokenProvider"/>'s
/// 30-second margin / 5-second failed-remint backoff); this type only ever calls
/// <see cref="ICalloutTokenProvider.GetTokenAsync"/> and, on an auth violation,
/// <see cref="ICalloutTokenProvider.InvalidateAsync"/>.</item>
/// </list>
/// No Polly: a small internal backoff loop (<see cref="ConnectBackoff"/>), <see cref="TimeProvider"/>-driven
/// for deterministic testing.
/// </summary>
public sealed class CalloutNatsConnection : IAsyncDisposable
{
    private readonly CalloutClientOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ConnectBackoff _backoff;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _reconnectGate = new(1, 1);

    private NatsConnection? _connection;
    private CalloutConnectionState _state = CalloutConnectionState.Disconnected;
    private CancellationTokenSource? _lifetimeCts;
    private bool _anonymous;

    /// <summary>Initializes a new instance of the <see cref="CalloutNatsConnection"/> class.</summary>
    /// <param name="options">The connection options. See <see cref="CalloutClientOptions"/>.</param>
    /// <param name="timeProvider">The time provider; defaults to <see cref="TimeProvider.System"/>.</param>
    /// <param name="backoff">The transport-failure backoff policy; defaults to a new <see cref="ConnectBackoff"/>.</param>
    /// <param name="logger">The logger; defaults to <see cref="NullLogger"/>.</param>
    public CalloutNatsConnection(
        CalloutClientOptions options,
        TimeProvider? timeProvider = null,
        ConnectBackoff? backoff = null,
        ILogger<CalloutNatsConnection>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _backoff = backoff ?? new ConnectBackoff();
        _logger = logger ?? NullLogger<CalloutNatsConnection>.Instance;
    }

    /// <summary>Raised whenever <see cref="State"/> changes.</summary>
    public event EventHandler<CalloutConnectionStateChangedEventArgs>? StateChanged;

    /// <summary>Gets the connection's current lifecycle state.</summary>
    /// <value>The current state.</value>
    public CalloutConnectionState State => _state;

    /// <summary>
    /// Gets the live NATS connection, valid once <see cref="State"/> is
    /// <see cref="CalloutConnectionState.Connected"/>.
    /// </summary>
    /// <value>The underlying connection, or null before the first successful connect.</value>
    public NatsConnection? Connection => _connection;

    /// <summary>Connects through the callout on the authenticated path (<see cref="CalloutClientOptions.TokenProvider"/>).</summary>
    /// <param name="cancellationToken">Cancellation for the connect attempt (and every retry, until it succeeds or this token is cancelled).</param>
    /// <returns>A task that completes once the connection is established.</returns>
    /// <exception cref="CalloutAuthenticationRequiredException">The token provider returned null (ADR 0021 rule 3).</exception>
    public Task ConnectAsync(CancellationToken cancellationToken = default) => ConnectCoreAsync(anonymous: false, cancellationToken);

    /// <summary>Connects through the callout on the anonymous path (<see cref="CalloutClientOptions.Anonymous"/> opt-in).</summary>
    /// <param name="cancellationToken">Cancellation for the connect attempt (and every retry, until it succeeds or this token is cancelled).</param>
    /// <returns>A task that completes once the connection is established.</returns>
    public Task ConnectAnonymousAsync(CancellationToken cancellationToken = default) => ConnectCoreAsync(anonymous: true, cancellationToken);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_lifetimeCts is not null)
        {
            await _lifetimeCts.CancelAsync().ConfigureAwait(false);
        }

        if (_connection is not null)
        {
            _connection.ConnectionDisconnected -= OnConnectionDisconnectedAsync;
            await _connection.DisposeAsync().ConfigureAwait(false);
        }

        _lifetimeCts?.Dispose();
        _reconnectGate.Dispose();
        SetState(CalloutConnectionState.Disconnected);
    }

    private async Task ConnectCoreAsync(bool anonymous, CancellationToken cancellationToken)
    {
        _anonymous = anonymous;
        _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await ConnectWithRetryAsync(isReconnect: false, _lifetimeCts.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// The connect pipeline shared by the initial connect and every self-owned reconnect: build
    /// fresh <c>NatsOpts</c> (fresh token, recomputed inbox prefix per ADR 0021 rule 4), connect,
    /// and on failure apply the ADR 0021 rules 1-3 retry/fail decision.
    /// </summary>
    private async Task ConnectWithRetryAsync(bool isReconnect, CancellationToken cancellationToken)
    {
        SetState(CalloutConnectionState.Connecting);

        var attempt = 0;
        var usedAuthViolationRetry = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outcome = await TryConnectOnceAsync(isReconnect, cancellationToken).ConfigureAwait(false);
            if (outcome.Succeeded)
            {
                return;
            }

            if (outcome.IsAuthenticationRequired)
            {
                // ADR 0021 rule 3: never retry a missing token as if it were a transport
                // failure. Fail immediately and loudly.
                SetState(CalloutConnectionState.Failed);
                throw outcome.Exception!;
            }

            if (!_anonymous && AuthViolationDetector.IsAuthorizationViolation(outcome.Exception))
            {
                if (usedAuthViolationRetry)
                {
                    // ADR 0021 rule 2: "retry exactly once, then fail terminally. Same token
                    // twice is pointless."
                    _logger.LogError(outcome.Exception, "Connect denied with Authorization Violation after the one forced-refresh retry; failing terminally.");
                    SetState(CalloutConnectionState.Failed);
                    throw outcome.Exception!;
                }

                usedAuthViolationRetry = true;
                SetState(CalloutConnectionState.Retrying);
                _logger.LogWarning(outcome.Exception, "Connect denied with Authorization Violation; invalidating the cached token and retrying once with a fresh one.");
                await (_options.TokenProvider?.InvalidateAsync(cancellationToken) ?? ValueTask.CompletedTask).ConfigureAwait(false);
                continue;
            }

            // ADR 0021 rule 1: transport failures retry indefinitely with backoff+jitter.
            attempt++;
            SetState(CalloutConnectionState.Retrying);
            var delay = _backoff.ComputeDelay(attempt);
            _logger.LogWarning(outcome.Exception, "Connect attempt {Attempt} failed; retrying in {Delay}.", attempt, delay);
            await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<ConnectAttemptOutcome> TryConnectOnceAsync(bool isReconnect, CancellationToken cancellationToken)
    {
        try
        {
            var opts = await BuildOptsAsync(cancellationToken).ConfigureAwait(false);
            var connection = new NatsConnection(opts);
            await connection.ConnectAsync().ConfigureAwait(false);
            await SwapConnectionAsync(connection, isReconnect).ConfigureAwait(false);
            SetState(CalloutConnectionState.Connected);
            return ConnectAttemptOutcome.Success;
        }
        catch (CalloutAuthenticationRequiredException ex)
        {
            return ConnectAttemptOutcome.AuthenticationRequired(ex);
        }
        catch (Exception ex)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ConnectAttemptOutcome.Failed(ex);
        }
    }

    private async Task SwapConnectionAsync(NatsConnection connection, bool isReconnect)
    {
        if (isReconnect && _connection is not null)
        {
            _connection.ConnectionDisconnected -= OnConnectionDisconnectedAsync;
            await _connection.DisposeAsync().ConfigureAwait(false);
        }

        connection.ConnectionDisconnected += OnConnectionDisconnectedAsync;
        _connection = connection;
    }

    private async ValueTask<NatsOpts> BuildOptsAsync(CancellationToken cancellationToken)
    {
        var connectOptions = new CalloutClientOptions
        {
            Url = _options.Url,
            ApplicationName = _options.ApplicationName,
            TokenProvider = _options.TokenProvider,
            Anonymous = _anonymous,
            AnonymousInboxPrefix = _options.AnonymousInboxPrefix,
            Name = _options.Name,
        };
        return await CalloutNatsOpts.CreateAsync(connectOptions, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask OnConnectionDisconnectedAsync(object? sender, NatsEventArgs args)
    {
        // Self-owned reconnect (ADR 0021 rule 4): guarded by a semaphore so a storm of
        // disconnect events (unlikely with a single connection, but defensive) doesn't start
        // concurrent reconnect pipelines.
        if (_lifetimeCts is null || _lifetimeCts.IsCancellationRequested)
        {
            return;
        }

        if (!await _reconnectGate.WaitAsync(0, CancellationToken.None).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            await ConnectWithRetryAsync(isReconnect: true, _lifetimeCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Disposed/cancelled while reconnecting; nothing further to do.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Self-owned reconnect failed terminally.");
        }
        finally
        {
            _reconnectGate.Release();
        }
    }

    private void SetState(CalloutConnectionState newState)
    {
        _state = newState;
        StateChanged?.Invoke(this, new CalloutConnectionStateChangedEventArgs(newState));
    }

    /// <summary>The outcome of one connect attempt, used to drive the retry decision without a long method.</summary>
    private sealed class ConnectAttemptOutcome
    {
        private ConnectAttemptOutcome(bool succeeded, bool isAuthenticationRequired, Exception? exception)
        {
            Succeeded = succeeded;
            IsAuthenticationRequired = isAuthenticationRequired;
            Exception = exception;
        }

        public static ConnectAttemptOutcome Success { get; } = new(succeeded: true, isAuthenticationRequired: false, exception: null);

        public bool Succeeded { get; }

        public bool IsAuthenticationRequired { get; }

        public Exception? Exception { get; }

        public static ConnectAttemptOutcome Failed(Exception exception) => new(succeeded: false, isAuthenticationRequired: false, exception);

        public static ConnectAttemptOutcome AuthenticationRequired(Exception exception) => new(succeeded: false, isAuthenticationRequired: true, exception);
    }
}
