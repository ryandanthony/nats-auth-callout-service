// -----------------------------------------------------------------------
// <copyright file="CalloutResponder.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text;
using AuthCallout.Contracts.Decisions;
using AuthCallout.Contracts.Seams;
using NATS.Client.Core;
using NATS.Jwt;

namespace AuthCallout.Nats.Callout;

/// <summary>
/// The transport edge: subscribes to <c>$SYS.REQ.USER.AUTH</c> in the <see cref="QueueGroup"/>
/// queue group (doc 08 §Multiple instances: exactly one instance handles each request),
/// drives requests through the resolution pipeline, and mints/signs/replies (doc 03 §Mint and
/// respond). Bounded concurrency via <c>Parallel.ForEachAsync</c> (doc 08 §In-process
/// parallelism) with a linked per-request timeout. Flow per message: decrypt (ADR 0018, if
/// configured, via <see cref="CalloutXkeyEnvelope"/>) → decode (<see cref="AuthRequestDecoder"/>)
/// → resolution pipeline → on a propagation-enabled grant, the identity-store writer runs
/// **before** the reply (doc 03: a store failure is fail-closed — the grant is downgraded to a
/// <see cref="DenyReason.IdentityStoreWriteFailed"/> denial rather than replied to without
/// propagation) → mint → encrypt (if configured) → reply → fire-and-forget event emission off
/// the critical path, strictly after the reply is sent. A malformed/undecodable request, an
/// xkey configuration mismatch, or any other unexpected per-request failure is logged via the
/// optional diagnostic callback and dropped without a reply (fail closed; the client
/// experiences the ordinary callout timeout).
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Major Code Smell",
    "S1200:Classes should not be coupled to too many other classes",
    Justification = "The responder is the transport-edge orchestrator and sat exactly at the 30-type limit; per-request tracing (CalloutActivitySource) adds the only extra couplings, and splitting the orchestration just to satisfy the count would scatter one linear request flow.")]
public sealed class CalloutResponder
{
    /// <summary>The fixed NATS auth callout subject (doc 08).</summary>
#pragma warning disable S2339 // Kept const (matching the repo's existing named-constant pattern, e.g. PostgresSourceSettings.DefaultNotifyChannel); this is a wire-protocol literal, not mutable state.
    public const string Subject = "$SYS.REQ.USER.AUTH";
#pragma warning restore S2339

    /// <summary>The pinned queue group name every instance subscribes with (doc 08 §Multiple instances).</summary>
#pragma warning disable S2339
    public const string QueueGroup = "auth-callout";
#pragma warning restore S2339

    /// <summary>The header the NATS server sets to signal an xkey-encrypted request and carry its ephemeral public key (ADR 0018).</summary>
#pragma warning disable S2339
    public const string XkeyHeaderName = "Nats-Server-Xkey";
#pragma warning restore S2339

    private readonly CalloutResponderDependencies _dependencies;
    private readonly CalloutResponderOptions _options;
    private readonly Action<string>? _onDiagnostic;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="CalloutResponder"/> class.</summary>
    /// <param name="dependencies">The collaborators this responder orchestrates per request.</param>
    /// <param name="options">Concurrency settings (bootstrap <c>concurrency</c> block).</param>
    /// <param name="onDiagnostic">
    /// Invoked with a human-readable, value-free diagnostic message when a request is
    /// dropped without a reply (malformed, xkey mismatch, timeout, unexpected failure) or
    /// downgraded to a store-failure denial. Optional; when null, diagnostics are silently
    /// dropped. Never called with token, claim, or secret material. Callers (P13) may wire
    /// this to their logger.
    /// </param>
    /// <param name="timeProvider">
    /// The monotonic time source for the per-request duration metric
    /// (<see cref="CalloutResponderMetrics.RequestDuration"/>), read via
    /// <see cref="TimeProvider.GetTimestamp"/>/<see cref="TimeProvider.GetElapsedTime(long)"/>.
    /// Defaults to <see cref="TimeProvider.System"/>; tests inject a fake to assert on the
    /// recorded latency deterministically (mirrors <c>EventEmitter</c>'s own time source).
    /// </param>
    public CalloutResponder(
        CalloutResponderDependencies dependencies,
        CalloutResponderOptions options,
        Action<string>? onDiagnostic = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        dependencies.Validate();
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _dependencies = dependencies;
        _options = options;
        _onDiagnostic = onDiagnostic;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Subscribes to <see cref="Subject"/> in <see cref="QueueGroup"/> on <paramref name="connection"/>
    /// and processes requests until <paramref name="cancellationToken"/> is cancelled (e.g. on
    /// shutdown, doc 05 Stopping). Delegates to <see cref="RunAsync(IAsyncEnumerable{NatsMsg{byte[]}}, CancellationToken)"/>,
    /// which is the overload unit tests drive directly against a synthetic message sequence.
    /// </summary>
    /// <param name="connection">The NATS connection to subscribe and reply on (the callout's own service-user connection, doc 05).</param>
    /// <param name="cancellationToken">Cancellation for the whole subscription loop.</param>
    /// <returns>A task that completes when the loop stops (cancellation) and every in-flight request has been awaited (doc 05 Stopping: graceful drain).</returns>
    public Task RunAsync(INatsConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var messages = connection.SubscribeAsync<byte[]>(Subject, queueGroup: QueueGroup, cancellationToken: cancellationToken);
        return RunAsync(messages, cancellationToken);
    }

    /// <summary>
    /// Drives <paramref name="messages"/> through <c>Parallel.ForEachAsync</c>, bounded by
    /// <see cref="CalloutResponderOptions.MaxDegreeOfParallelism"/> (doc 08 §In-process
    /// parallelism). Exposed directly (not only via the <see cref="INatsConnection"/> overload)
    /// so unit tests can supply a synthetic sequence without a real NATS subscription.
    /// </summary>
    /// <param name="messages">The message sequence to process (a real subscription, or a test double).</param>
    /// <param name="cancellationToken">Cancellation for the whole loop.</param>
    /// <returns>A task that completes when the loop stops and every in-flight request has been awaited.</returns>
#pragma warning disable S4017 // The nested generic (NatsMsg<byte[]> inside IAsyncEnumerable<T>) is NATS.Net's own message shape for a byte[]-payload subscription; introducing a wrapper type here buys nothing and would just re-wrap the same shape.
    public async Task RunAsync(IAsyncEnumerable<NatsMsg<byte[]>> messages, CancellationToken cancellationToken)
#pragma warning restore S4017
    {
        ArgumentNullException.ThrowIfNull(messages);
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = _options.MaxDegreeOfParallelism,
            CancellationToken = cancellationToken,
        };
        await Parallel.ForEachAsync(messages, parallelOptions, HandleAsync);
    }

    private async ValueTask HandleAsync(NatsMsg<byte[]> msg, CancellationToken cancellationToken)
    {
        // Monotonic start marker for the per-request duration metric: captured the moment the
        // message is picked up, stopped right after the reply is sent (only replied requests
        // are timed; a dropped request never replies, so its latency is meaningless).
        var startTimestamp = _timeProvider.GetTimestamp();
        using var requestSpan = CalloutActivitySource.StartRequest();
        using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestCts.CancelAfter(_options.PerRequestTimeout);
        var requestToken = requestCts.Token;

        try
        {
            await ProcessAsync(msg, startTimestamp, requestToken);
        }
        catch (OperationCanceledException) when (requestToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            Drop("Auth request exceeded the per-request timeout; dropped without reply.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Drop($"Auth request processing failed unexpectedly ({ex.GetType().Name}); dropped without reply.");
        }
    }

    private async Task ProcessAsync(NatsMsg<byte[]> msg, long startTimestamp, CancellationToken cancellationToken)
    {
        var envelope = CalloutXkeyEnvelope.Open(msg, _dependencies.Crypto);
        if (!envelope.Accepted)
        {
            Drop(envelope.RejectionReason ?? "Auth request rejected by the xkey envelope check.");
            return;
        }

        if (!TryDecodeRequest(envelope.Payload ?? [], out var request))
        {
            return;
        }

        var activeConfig = _dependencies.ActiveConfigSource.Current;
        if (activeConfig is null)
        {
            Drop("No active config; dropped without reply.");
            return;
        }

        var decision = await CalloutActivitySource.TraceStageAsync(
            "auth.resolve", () => _dependencies.Pipeline.ResolveAsync(request, activeConfig, cancellationToken));
        decision = await CalloutActivitySource.TraceStageAsync(
            "auth.identity_store", () => StoreIdentityIfNeededAsync(decision, request, cancellationToken));
        await CalloutActivitySource.TraceStageAsync(
            "auth.reply", () => ReplyAsync(msg, request, decision, envelope.ServerXkeyPublic, cancellationToken));
        CalloutActivitySource.TagOutcome(decision is GrantedDecision);

        // The reply is out: record the success/failure metric for the callout method being
        // invoked (a grant is a success, a denial a failure) and the receive-to-reply duration,
        // strictly after the reply.
        RecordDecision(decision, startTimestamp);

        // Fire-and-forget, strictly after the reply: IEventEmitter.EmitAsync is documented
        // never to throw (ADR 0008), so this is not awaited and never delays or risks the
        // reply that already went out.
        _ = _dependencies.EventEmitter.EmitAsync(decision, request.UserNKey, CancellationToken.None);
    }

    /// <summary>Counts a request that produced no reply (<see cref="CalloutResponderMetrics.Dropped"/>) and surfaces the value-free reason via the diagnostic callback.</summary>
    private void Drop(string reason)
    {
        CalloutResponderMetrics.Dropped.Add(1);
        _onDiagnostic?.Invoke(reason);
    }

    /// <summary>Records the grant/deny outcome metric and the receive-to-reply duration, strictly after the reply has been sent.</summary>
    private void RecordDecision(Decision decision, long startTimestamp)
    {
        var elapsedMilliseconds = _timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds;
        switch (decision)
        {
            case GrantedDecision:
                {
                    CalloutResponderMetrics.RecordGranted(elapsedMilliseconds);
                    break;
                }

            case DeniedDecision:
                {
                    CalloutResponderMetrics.RecordDenied(elapsedMilliseconds);
                    break;
                }

            default:
                throw new InvalidOperationException("Unrecognized Decision subtype; expected GrantedDecision or DeniedDecision.");
        }
    }

    private bool TryDecodeRequest(byte[] payload, out AuthRequest request)
    {
        try
        {
            request = AuthRequestDecoder.Decode(payload);
            return true;
        }
        catch (Exception ex) when (ex is NatsJwtException or FormatException or ArgumentException)
        {
            Drop($"Malformed auth request ({ex.GetType().Name}); dropped without reply.");
            request = null!;
            return false;
        }
    }

    private async Task ReplyAsync(NatsMsg<byte[]> msg, AuthRequest request, Decision decision, string? serverXkeyPublic, CancellationToken cancellationToken)
    {
        var response = MintResponse(decision, request);
        var responseBytes = Encoding.UTF8.GetBytes(response.EncodedResponseJwt);
        responseBytes = CalloutXkeyEnvelope.Seal(responseBytes, serverXkeyPublic, _dependencies.Crypto);
        await msg.ReplyAsync(responseBytes, cancellationToken: cancellationToken);
    }

    private SignedAuthResponse MintResponse(Decision decision, AuthRequest request) => decision switch
    {
        GrantedDecision granted => _dependencies.Minter.MintGrant(granted, request.UserNKey, request.ServerId),
        DeniedDecision denied => _dependencies.Minter.MintDenial(denied, request.UserNKey, request.ServerId),
        _ => throw new InvalidOperationException("Unrecognized Decision subtype; expected GrantedDecision or DeniedDecision."),
    };

    private async Task<Decision> StoreIdentityIfNeededAsync(Decision decision, AuthRequest request, CancellationToken cancellationToken)
    {
        if (decision is not GrantedDecision { IdentityPropagationEnabled: true } granted)
        {
            return decision;
        }

        if (request.RawToken is null)
        {
            // Invariant violation: a non-anonymous, propagation-enabled grant is only ever
            // produced from a real presented token (ADR 0003/0011); treat this defensively
            // as a store failure (fail closed) rather than writing an empty token.
            _onDiagnostic?.Invoke("Propagation-enabled grant had no raw token to store; denying (fail closed).");
            return DenyForStoreFailure(granted);
        }

        var writeResult = await _dependencies.IdentityStoreWriter.WriteAsync(granted, request.RawToken, cancellationToken);
        if (writeResult.Succeeded)
        {
            return decision;
        }

        _onDiagnostic?.Invoke("Identity-store write failed for a propagation-enabled grant; denying (fail closed, doc 03).");
        return DenyForStoreFailure(granted);
    }

    private static DeniedDecision DenyForStoreFailure(GrantedDecision granted) =>
        new(granted.Application, granted.Principal, granted.Issuer, DenyReason.IdentityStoreWriteFailed);
}
