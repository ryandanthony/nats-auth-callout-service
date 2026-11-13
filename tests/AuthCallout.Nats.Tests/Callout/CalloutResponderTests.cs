// -----------------------------------------------------------------------
// <copyright file="CalloutResponderTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Text;
using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Decisions;
using AuthCallout.Contracts.Seams;
using AuthCallout.Nats.Callout;
using NATS.Client.Core;
using NATS.NKeys;
using NSubstitute;

namespace AuthCallout.Nats.Tests.Callout;

/// <summary>
/// Unit tests for <see cref="CalloutResponder"/> (P07 ADR→test map): store-before-reply
/// ordering, store-failure fail-closed, per-request timeout isolation, bounded parallelism,
/// queue-group/subject constants, event-emitted-after-reply, xkey round-trip and mismatch
/// handling, and malformed/no-config drop behavior. Every collaborator is substituted
/// (NSubstitute) per the packet's "unit tests substitute INatsConnection-level surfaces"
/// DoD note; real-NATS coverage is P14's.
/// </summary>
public sealed class CalloutResponderTests
{
    private static readonly CalloutResponderOptions DefaultOptions = new(MaxDegreeOfParallelism: 4, PerRequestTimeout: TimeSpan.FromSeconds(5));

    /// <summary>Wires <paramref name="onReply"/> to fire whenever <paramref name="connection"/>.PublishAsync (used by NatsMsg.ReplyAsync) is called.</summary>
    private static void WireOnReply(INatsConnection connection, Action onReply)
    {
#pragma warning disable VSTHRD110, MA0134 // NSubstitute's .When(x => x.AsyncMethod(...)) pattern intentionally invokes the async method without awaiting it: NSubstitute intercepts the call itself and never actually executes the real (ValueTask-returning) method body, so there is nothing to await.
        connection
            .When(c => c.PublishAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<NatsHeaders>(), Arg.Any<string>(), Arg.Any<INatsSerialize<byte[]>>(), Arg.Any<NatsPubOpts>(), Arg.Any<CancellationToken>()))
            .Do(_ => onReply());
#pragma warning restore VSTHRD110, MA0134
    }

    [Fact]
    public void Subject_And_QueueGroup_ArePinned()
    {
        Assert.Equal("$SYS.REQ.USER.AUTH", CalloutResponder.Subject);
        Assert.Equal("auth-callout", CalloutResponder.QueueGroup);
    }

    [Fact]
    public async Task RunAsync_GrantedDecision_RepliesWithMintedGrantJwt()
    {
        var pipeline = Substitute.For<IResolutionPipeline>();
        var grant = CalloutTestBuilders.Granted();
        pipeline.ResolveAsync(Arg.Any<AuthRequest>(), Arg.Any<ActiveConfig>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Decision>(grant));
        var minter = Substitute.For<IUserJwtMinter>();
        var mintedResponse = new SignedAuthResponse("minted-jwt-text");
        minter.MintGrant(Arg.Any<GrantedDecision>(), Arg.Any<string>(), Arg.Any<string>()).Returns(mintedResponse);

        var harness = new ResponderHarness(new ResponderHarnessOptions { Pipeline = pipeline, Minter = minter });
        var payload = CalloutTestBuilders.BuildRequestPayload();

        await harness.RunOneAsync(payload);

        harness.AssertRepliedOnce(out var replyBytes);
        Assert.Equal("minted-jwt-text", Encoding.UTF8.GetString(replyBytes));
    }

    [Fact]
    public async Task RunAsync_DeniedDecision_RepliesWithMintedDenialJwt()
    {
        var pipeline = Substitute.For<IResolutionPipeline>();
        var denial = CalloutTestBuilders.Denied();
        pipeline.ResolveAsync(Arg.Any<AuthRequest>(), Arg.Any<ActiveConfig>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Decision>(denial));
        var minter = Substitute.For<IUserJwtMinter>();
        minter.MintDenial(Arg.Any<DeniedDecision>(), Arg.Any<string>(), Arg.Any<string>()).Returns(new SignedAuthResponse("denial-jwt-text"));

        var harness = new ResponderHarness(new ResponderHarnessOptions { Pipeline = pipeline, Minter = minter });
        var payload = CalloutTestBuilders.BuildRequestPayload();

        await harness.RunOneAsync(payload);

        harness.AssertRepliedOnce(out var replyBytes);
        Assert.Equal("denial-jwt-text", Encoding.UTF8.GetString(replyBytes));
    }

    [Fact]
    public async Task RunAsync_MalformedRequest_DropsWithoutReply()
    {
        var harness = new ResponderHarness();
        var malformedPayload = Encoding.UTF8.GetBytes("not a valid nats jwt");

        await harness.RunOneAsync(malformedPayload);

        harness.AssertNeverReplied();
    }

    [Fact]
    public async Task RunAsync_NoActiveConfig_DropsWithoutReply()
    {
        var harness = new ResponderHarness(new ResponderHarnessOptions { UseDefaultActiveConfig = false });
        var payload = CalloutTestBuilders.BuildRequestPayload();

        await harness.RunOneAsync(payload);

        harness.AssertNeverReplied();
    }

    // ---- store-before-reply ordering (doc 03) ----

    [Fact]
    public async Task RunAsync_PropagationEnabledGrant_StoresIdentityBeforeReplying()
    {
        var order = new ConcurrentQueue<string>();
        var pipeline = Substitute.For<IResolutionPipeline>();
        var grant = CalloutTestBuilders.Granted() with { IdentityPropagationEnabled = true };
        pipeline.ResolveAsync(Arg.Any<AuthRequest>(), Arg.Any<ActiveConfig>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Decision>(grant));

        var identityStoreWriter = Substitute.For<IIdentityStoreWriter>();
        identityStoreWriter
            .WriteAsync(Arg.Any<GrantedDecision>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                order.Enqueue("store");
                return Task.FromResult(new IdentityStoreWriteResult(Succeeded: true, ConnectionId: "conn-1"));
            });

        var connection = Substitute.For<INatsConnection>();
        WireOnReply(connection, () => order.Enqueue("reply"));

        var harness = new ResponderHarness(new ResponderHarnessOptions { Pipeline = pipeline, IdentityStoreWriter = identityStoreWriter, Connection = connection });
        var payload = CalloutTestBuilders.BuildRequestPayload(new RequestPayloadOptions { RawToken = "the-oidc-token" });

        await harness.RunOneAsync(payload);

        Assert.Equal(["store", "reply"], order);
    }

    [Fact]
    public async Task RunAsync_AnonymousGrant_NeverCallsIdentityStoreWriter()
    {
        var pipeline = Substitute.For<IResolutionPipeline>();
        pipeline.ResolveAsync(Arg.Any<AuthRequest>(), Arg.Any<ActiveConfig>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Decision>(CalloutTestBuilders.AnonymousGranted()));
        var identityStoreWriter = Substitute.For<IIdentityStoreWriter>();

        var harness = new ResponderHarness(new ResponderHarnessOptions { Pipeline = pipeline, IdentityStoreWriter = identityStoreWriter });
        var payload = CalloutTestBuilders.BuildRequestPayload(new RequestPayloadOptions { RawToken = null });

        await harness.RunOneAsync(payload);

        await identityStoreWriter.DidNotReceiveWithAnyArgs().WriteAsync(default!, default!, default);
        harness.AssertRepliedOnce(out _);
    }

    [Fact]
    public async Task RunAsync_PropagationDisabledGrant_NeverCallsIdentityStoreWriter()
    {
        var pipeline = Substitute.For<IResolutionPipeline>();
        var grant = CalloutTestBuilders.Granted() with { IdentityPropagationEnabled = false };
        pipeline.ResolveAsync(Arg.Any<AuthRequest>(), Arg.Any<ActiveConfig>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Decision>(grant));
        var identityStoreWriter = Substitute.For<IIdentityStoreWriter>();

        var harness = new ResponderHarness(new ResponderHarnessOptions { Pipeline = pipeline, IdentityStoreWriter = identityStoreWriter });
        var payload = CalloutTestBuilders.BuildRequestPayload();

        await harness.RunOneAsync(payload);

        await identityStoreWriter.DidNotReceiveWithAnyArgs().WriteAsync(default!, default!, default);
        harness.AssertRepliedOnce(out _);
    }

    // ---- store failure -> deny (fail closed) ----

    [Fact]
    public async Task RunAsync_IdentityStoreWriteFails_DeniesInsteadOfGrantReply()
    {
        var pipeline = Substitute.For<IResolutionPipeline>();
        var grant = CalloutTestBuilders.Granted() with { IdentityPropagationEnabled = true };
        pipeline.ResolveAsync(Arg.Any<AuthRequest>(), Arg.Any<ActiveConfig>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Decision>(grant));
        var identityStoreWriter = Substitute.For<IIdentityStoreWriter>();
        identityStoreWriter
            .WriteAsync(Arg.Any<GrantedDecision>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new IdentityStoreWriteResult(Succeeded: false, ConnectionId: null)));
        var minter = Substitute.For<IUserJwtMinter>();
        minter.MintDenial(Arg.Any<DeniedDecision>(), Arg.Any<string>(), Arg.Any<string>()).Returns(new SignedAuthResponse("store-failure-denial"));

        var harness = new ResponderHarness(new ResponderHarnessOptions { Pipeline = pipeline, IdentityStoreWriter = identityStoreWriter, Minter = minter });
        var payload = CalloutTestBuilders.BuildRequestPayload(new RequestPayloadOptions { RawToken = "the-oidc-token" });

        await harness.RunOneAsync(payload);

        minter.DidNotReceiveWithAnyArgs().MintGrant(default!, default!, default!);
        minter.Received(1).MintDenial(
            Arg.Is<DeniedDecision>(d => d.Reason == DenyReason.IdentityStoreWriteFailed),
            Arg.Any<string>(),
            Arg.Any<string>());
        harness.AssertRepliedOnce(out var replyBytes);
        Assert.Equal("store-failure-denial", Encoding.UTF8.GetString(replyBytes));
    }

    [Fact]
    public async Task RunAsync_IdentityStoreWriteFails_StillEmitsOutcomeEvent()
    {
        // Even the fail-closed downgrade is a real decision (a denial) and must still be
        // reported (ADR 0008 events cover every decision, not just pipeline-native ones).
        var pipeline = Substitute.For<IResolutionPipeline>();
        var grant = CalloutTestBuilders.Granted() with { IdentityPropagationEnabled = true };
        pipeline.ResolveAsync(Arg.Any<AuthRequest>(), Arg.Any<ActiveConfig>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<Decision>(grant));
        var identityStoreWriter = Substitute.For<IIdentityStoreWriter>();
        identityStoreWriter
            .WriteAsync(Arg.Any<GrantedDecision>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new IdentityStoreWriteResult(Succeeded: false, ConnectionId: null)));
        var eventEmitter = Substitute.For<IEventEmitter>();
        eventEmitter.EmitAsync(Arg.Any<Decision>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var harness = new ResponderHarness(new ResponderHarnessOptions { Pipeline = pipeline, IdentityStoreWriter = identityStoreWriter, EventEmitter = eventEmitter });
        var payload = CalloutTestBuilders.BuildRequestPayload(new RequestPayloadOptions { RawToken = "the-oidc-token" });

        await harness.RunOneAsync(payload);
        await harness.WaitForEventEmitAsync();

        await eventEmitter.Received(1).EmitAsync(
            Arg.Is<Decision>(d => (d as DeniedDecision)!.Reason == DenyReason.IdentityStoreWriteFailed),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    // ---- event emitted after reply (never blocks the reply) ----

    [Fact]
    public async Task RunAsync_EmitsOutcomeEvent_AfterTheReplyIsSent()
    {
        var order = new ConcurrentQueue<string>();
        var pipeline = Substitute.For<IResolutionPipeline>();
        pipeline.ResolveAsync(Arg.Any<AuthRequest>(), Arg.Any<ActiveConfig>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Decision>(CalloutTestBuilders.Granted()));

        var connection = Substitute.For<INatsConnection>();
        WireOnReply(connection, () => order.Enqueue("reply"));

        var eventEmitter = Substitute.For<IEventEmitter>();
        eventEmitter
            .EmitAsync(Arg.Any<Decision>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                order.Enqueue("event");
                return Task.CompletedTask;
            });

        var harness = new ResponderHarness(new ResponderHarnessOptions { Pipeline = pipeline, Connection = connection, EventEmitter = eventEmitter });
        var payload = CalloutTestBuilders.BuildRequestPayload();

        await harness.RunOneAsync(payload);
        await harness.WaitForEventEmitAsync();

        Assert.Equal(["reply", "event"], order);
    }

    [Fact]
    public async Task RunAsync_EventEmitterThrows_NeverPropagatesOutOfRunAsync()
    {
        // Defense-in-depth: IEventEmitter is documented never to throw (ADR 0008), but the
        // responder's fire-and-forget call must not let a violation of that contract take
        // down the whole Parallel.ForEachAsync loop either.
        var pipeline = Substitute.For<IResolutionPipeline>();
        pipeline.ResolveAsync(Arg.Any<AuthRequest>(), Arg.Any<ActiveConfig>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Decision>(CalloutTestBuilders.Granted()));
        var eventEmitter = Substitute.For<IEventEmitter>();
        eventEmitter.EmitAsync(Arg.Any<Decision>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("emitter contract violated")));

        var harness = new ResponderHarness(new ResponderHarnessOptions { Pipeline = pipeline, EventEmitter = eventEmitter });
        var payload = CalloutTestBuilders.BuildRequestPayload();

        var exception = await Record.ExceptionAsync(() => harness.RunOneAsync(payload));

        Assert.Null(exception);
        harness.AssertRepliedOnce(out _);
    }

    // ---- per-request timeout isolation ----

    [Fact]
    public async Task RunAsync_OneHungRequest_TimesOutWithoutReply_OtherRequestsUnaffected()
    {
        var pipeline = Substitute.For<IResolutionPipeline>();
        pipeline.ResolveAsync(Arg.Any<AuthRequest>(), Arg.Any<ActiveConfig>(), Arg.Any<CancellationToken>())
            .Returns(async ci =>
            {
                var request = ci.Arg<AuthRequest>();
                var token = ci.Arg<CancellationToken>();
                if (string.Equals(request.UserNKey, "HANG", StringComparison.Ordinal))
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), token);
                    return (Decision)CalloutTestBuilders.Granted();
                }

                return (Decision)CalloutTestBuilders.Granted();
            });

        var options = new CalloutResponderOptions(MaxDegreeOfParallelism: 4, PerRequestTimeout: TimeSpan.FromMilliseconds(200));
        var harness = new ResponderHarness(new ResponderHarnessOptions { Pipeline = pipeline, ResponderOptions = options });
        var hangPayload = CalloutTestBuilders.BuildRequestPayload(new RequestPayloadOptions { UserNKey = "HANG" });
        var normalPayload = CalloutTestBuilders.BuildRequestPayload(new RequestPayloadOptions { UserNKey = CalloutTestBuilders.NewUserPublicKey() });

        await harness.RunManyAsync([hangPayload, normalPayload]);

        Assert.Equal(1, harness.ReplyCount);
        Assert.Contains(harness.Diagnostics, d => d.Contains("timeout", StringComparison.OrdinalIgnoreCase));
    }

    // ---- bounded parallelism ----

    [Fact]
    public async Task RunAsync_NeverExceedsConfiguredMaxDegreeOfParallelism()
    {
        const int maxDegree = 3;
        const int requestCount = 12;
        var currentConcurrency = 0;
        var maxObservedConcurrency = 0;
        using var release = new SemaphoreSlim(0, requestCount);

        var pipeline = Substitute.For<IResolutionPipeline>();
        pipeline.ResolveAsync(Arg.Any<AuthRequest>(), Arg.Any<ActiveConfig>(), Arg.Any<CancellationToken>())
            .Returns(async ci =>
            {
                var observed = Interlocked.Increment(ref currentConcurrency);
                InterlockedMax(ref maxObservedConcurrency, observed);

                await release.WaitAsync(ci.Arg<CancellationToken>());

                Interlocked.Decrement(ref currentConcurrency);

                return (Decision)CalloutTestBuilders.Granted();
            });

        var options = new CalloutResponderOptions(MaxDegreeOfParallelism: maxDegree, PerRequestTimeout: TimeSpan.FromSeconds(10));
        var harness = new ResponderHarness(new ResponderHarnessOptions { Pipeline = pipeline, ResponderOptions = options });
        var payloads = Enumerable.Range(0, requestCount)
            .Select(_ => CalloutTestBuilders.BuildRequestPayload(new RequestPayloadOptions { UserNKey = CalloutTestBuilders.NewUserPublicKey() }))
            .ToArray();

        var runTask = harness.RunManyAsync(payloads);

        // Let the in-flight handlers reach the gate, then release them all at once and await completion.
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        release.Release(requestCount);
        await runTask;

        Assert.True(maxObservedConcurrency <= maxDegree, $"Observed concurrency {maxObservedConcurrency} exceeded configured max {maxDegree}.");
        Assert.Equal(requestCount, harness.ReplyCount);
    }

    // ---- xkey round-trip and mismatch handling (ADR 0018) ----

    [Fact]
    public async Task RunAsync_XkeyConfigured_EncryptedRequest_OpensAndSealsReplyToServerKey()
    {
        using var serverKp = KeyPair.CreatePair(PrefixByte.Curve);
        using var serviceKp = KeyPair.CreatePair(PrefixByte.Curve);
        using var crypto = new CalloutCrypto(serviceKp.GetSeed());

        var pipeline = Substitute.For<IResolutionPipeline>();
        pipeline.ResolveAsync(Arg.Any<AuthRequest>(), Arg.Any<ActiveConfig>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Decision>(CalloutTestBuilders.Granted()));
        var minter = Substitute.For<IUserJwtMinter>();
        minter.MintGrant(Arg.Any<GrantedDecision>(), Arg.Any<string>(), Arg.Any<string>()).Returns(new SignedAuthResponse("plaintext-grant-jwt"));

        var harness = new ResponderHarness(new ResponderHarnessOptions { Pipeline = pipeline, Minter = minter, Crypto = crypto });
        var plainPayload = CalloutTestBuilders.BuildRequestPayload();
        var sealedPayload = serverKp.Seal(plainPayload, serviceKp.GetPublicKey());

        await harness.RunOneAsync(sealedPayload, xkeyHeaderValue: serverKp.GetPublicKey());

        harness.AssertRepliedOnce(out var replyBytes);
        var openedReply = serverKp.Open(replyBytes, serviceKp.GetPublicKey());
        Assert.Equal("plaintext-grant-jwt", Encoding.UTF8.GetString(openedReply));
    }

    [Fact]
    public async Task RunAsync_XkeyUnconfigured_EncryptedRequestArrives_DropsWithoutReply()
    {
        using var serverKp = KeyPair.CreatePair(PrefixByte.Curve);
        var payload = CalloutTestBuilders.BuildRequestPayload();
        // "Encrypted" from the responder's point of view is signaled purely by header
        // presence (ADR 0018); the actual bytes don't need to be real ciphertext for this
        // fail-closed check, since the no-op crypto is never asked to open them.
        var harness = new ResponderHarness(new ResponderHarnessOptions { Crypto = new NoOpCalloutCrypto() });

        await harness.RunOneAsync(payload, xkeyHeaderValue: serverKp.GetPublicKey());

        harness.AssertNeverReplied();
        Assert.Contains(harness.Diagnostics, d => d.Contains("not configured", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_XkeyConfigured_PlaintextRequestArrives_DropsWithoutReply()
    {
        using var serviceKp = KeyPair.CreatePair(PrefixByte.Curve);
        using var crypto = new CalloutCrypto(serviceKp.GetSeed());
        var harness = new ResponderHarness(new ResponderHarnessOptions { Crypto = crypto });
        var payload = CalloutTestBuilders.BuildRequestPayload();

        await harness.RunOneAsync(payload, xkeyHeaderValue: null);

        harness.AssertNeverReplied();
        Assert.Contains(harness.Diagnostics, d => d.Contains("xkey is configured", StringComparison.OrdinalIgnoreCase));
    }

    // ---- per-decision success/failure metrics (callout method invoked) ----

    [Fact]
    public async Task RunAsync_GrantedDecision_IncrementsGrantedCounter()
    {
        var pipeline = Substitute.For<IResolutionPipeline>();
        pipeline.ResolveAsync(Arg.Any<AuthRequest>(), Arg.Any<ActiveConfig>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Decision>(CalloutTestBuilders.Granted()));
        var harness = new ResponderHarness(new ResponderHarnessOptions { Pipeline = pipeline });

        using var counters = new ResponderCounterListener();
        await harness.RunOneAsync(CalloutTestBuilders.BuildRequestPayload());

        Assert.Equal(1, counters.Granted);
        Assert.Equal(0, counters.Denied);
        Assert.Equal(0, counters.Dropped);
    }

    [Fact]
    public async Task RunAsync_DeniedDecision_IncrementsDeniedCounter()
    {
        var pipeline = Substitute.For<IResolutionPipeline>();
        pipeline.ResolveAsync(Arg.Any<AuthRequest>(), Arg.Any<ActiveConfig>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Decision>(CalloutTestBuilders.Denied()));
        var harness = new ResponderHarness(new ResponderHarnessOptions { Pipeline = pipeline });

        using var counters = new ResponderCounterListener();
        await harness.RunOneAsync(CalloutTestBuilders.BuildRequestPayload());

        Assert.Equal(0, counters.Granted);
        Assert.Equal(1, counters.Denied);
        Assert.Equal(0, counters.Dropped);
    }

    [Fact]
    public async Task RunAsync_MalformedRequest_IncrementsDroppedCounter()
    {
        var harness = new ResponderHarness();

        using var counters = new ResponderCounterListener();
        await harness.RunOneAsync(Encoding.UTF8.GetBytes("not a valid nats jwt"));

        Assert.Equal(0, counters.Granted);
        Assert.Equal(0, counters.Denied);
        Assert.Equal(1, counters.Dropped);
    }

    // ---- per-request duration (receive to reply) ----

    [Fact]
    public async Task RunAsync_RepliedRequest_RecordsRequestDuration()
    {
        var pipeline = Substitute.For<IResolutionPipeline>();
        pipeline.ResolveAsync(Arg.Any<AuthRequest>(), Arg.Any<ActiveConfig>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Decision>(CalloutTestBuilders.Granted()));
        var harness = new ResponderHarness(new ResponderHarnessOptions { Pipeline = pipeline });

        using var counters = new ResponderCounterListener();
        await harness.RunOneAsync(CalloutTestBuilders.BuildRequestPayload());

        Assert.Single(counters.Durations);
        Assert.True(counters.Durations.Single() >= 0, "Recorded duration should be non-negative.");
    }

    [Fact]
    public async Task RunAsync_RequestDuration_MeasuresElapsedTimeFromReceiveToReply()
    {
        // A controllable monotonic clock: the responder reads GetTimestamp() at receive and
        // GetElapsedTime() after the reply, so advancing the clock by a known amount between
        // those two reads makes the recorded latency deterministic.
        var clock = new AdvanceableTimeProvider();
        var pipeline = Substitute.For<IResolutionPipeline>();
        pipeline.ResolveAsync(Arg.Any<AuthRequest>(), Arg.Any<ActiveConfig>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                clock.Advance(TimeSpan.FromMilliseconds(250));
                return Task.FromResult<Decision>(CalloutTestBuilders.Granted());
            });
        var harness = new ResponderHarness(new ResponderHarnessOptions { Pipeline = pipeline, TimeProvider = clock });

        using var counters = new ResponderCounterListener();
        await harness.RunOneAsync(CalloutTestBuilders.BuildRequestPayload());

        Assert.Equal(250, counters.Durations.Single(), precision: 3);
    }

    [Fact]
    public async Task RunAsync_DroppedRequest_RecordsNoRequestDuration()
    {
        var harness = new ResponderHarness();

        using var counters = new ResponderCounterListener();
        await harness.RunOneAsync(Encoding.UTF8.GetBytes("not a valid nats jwt"));

        Assert.Empty(counters.Durations);
    }

    // ---- constructor validation ----

    [Fact]
    public void Constructor_NullDependencies_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CalloutResponder(null!, DefaultOptions));
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        var dependencies = ResponderHarness.BuildDependencies();
        Assert.Throws<ArgumentNullException>(() => new CalloutResponder(dependencies, null!));
    }

    [Fact]
    public void Constructor_NonPositiveMaxDegreeOfParallelism_Throws()
    {
        var dependencies = ResponderHarness.BuildDependencies();
        Assert.Throws<InvalidOperationException>(() => new CalloutResponder(dependencies, new CalloutResponderOptions(0, TimeSpan.FromSeconds(1))));
    }

    [Fact]
    public void Constructor_NonPositivePerRequestTimeout_Throws()
    {
        var dependencies = ResponderHarness.BuildDependencies();
        Assert.Throws<InvalidOperationException>(() => new CalloutResponder(dependencies, new CalloutResponderOptions(1, TimeSpan.Zero)));
    }

    private static void InterlockedMax(ref int target, int candidate)
    {
        int initial;
        do
        {
            initial = target;
            if (candidate <= initial)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(ref target, candidate, initial) != initial);
    }
}
