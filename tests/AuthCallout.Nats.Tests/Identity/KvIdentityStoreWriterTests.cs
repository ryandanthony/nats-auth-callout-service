// -----------------------------------------------------------------------
// <copyright file="KvIdentityStoreWriterTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Seams;
using AuthCallout.Nats.Identity;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;
using NATS.Client.KeyValueStore;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace AuthCallout.Nats.Tests.Identity;

/// <summary>
/// Unit tests for <see cref="KvIdentityStoreWriter.WriteAsync"/> (P08 ADR→test map: "Token
/// TTL = exp - now exactly, no clamp" D17; "jti -> sub fallback -> skip" doc 07; "Toggle
/// off: no token/assertion writes" ADR 0012). Substitutes <see cref="INatsKVContext"/>,
/// <see cref="INatsKVStore"/>, and <see cref="IAssertionSigner"/>; real KV/JetStream
/// verification is P14's.
/// </summary>
public sealed class KvIdentityStoreWriterTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task WriteAsync_PropagationDisabled_SkipsBothWrites_SucceedsWithNullConnectionId()
    {
        var tokenStore = Substitute.For<INatsKVStore>();
        var assertionStore = Substitute.For<INatsKVStore>();
        var kvContext = ContextReturning(tokenStore, assertionStore);
        var signer = Substitute.For<IAssertionSigner>();
        var writer = new KvIdentityStoreWriter(kvContext, signer, Substitute.For<ISecretResolver>(), FixedTimeProvider());
        var grant = IdentityTestBuilders.GrantFor("notes", propagationEnabled: false, selfLookupKey: "jti-1", FixedNow.AddMinutes(5));

        var result = await writer.WriteAsync(grant, "raw-oidc-token", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Null(result.ConnectionId);
        await kvContext.DidNotReceiveWithAnyArgs().GetStoreAsync(default!, default);
        signer.DidNotReceiveWithAnyArgs().Sign(default!);
    }

    [Fact]
    public async Task WriteAsync_NoSelfLookupKey_SkipsBothWrites_SucceedsWithNullConnectionId()
    {
        // doc 07 §Key parts: neither jti nor sub present -> propagation unavailable, grant
        // still stands. GrantBuilder encodes this as a null SelfLookupKey even when
        // IdentityPropagationEnabled is true.
        var tokenStore = Substitute.For<INatsKVStore>();
        var assertionStore = Substitute.For<INatsKVStore>();
        var kvContext = ContextReturning(tokenStore, assertionStore);
        var signer = Substitute.For<IAssertionSigner>();
        var writer = new KvIdentityStoreWriter(kvContext, signer, Substitute.For<ISecretResolver>(), FixedTimeProvider());
        var grant = IdentityTestBuilders.GrantFor("notes", propagationEnabled: true, selfLookupKey: null, FixedNow.AddMinutes(5));

        var result = await writer.WriteAsync(grant, "raw-oidc-token", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Null(result.ConnectionId);
        await kvContext.DidNotReceiveWithAnyArgs().GetStoreAsync(default!, default);
    }

    [Fact]
    public async Task WriteAsync_PropagationEnabled_WritesTokenWithTtlExactlyExpMinusNow_NoClamp()
    {
        var tokenStore = Substitute.For<INatsKVStore>();
        var assertionStore = Substitute.For<INatsKVStore>();
        var kvContext = ContextReturning(tokenStore, assertionStore);
        var signer = SignerReturning(new SignedAssertion("header.payload.sig", "kid-1"));
        var writer = new KvIdentityStoreWriter(kvContext, signer, Substitute.For<ISecretResolver>(), FixedTimeProvider());
        var expiry = FixedNow.AddMinutes(7);
        var grant = IdentityTestBuilders.GrantFor("notes", propagationEnabled: true, selfLookupKey: "jti-1", expiry);

        await writer.WriteAsync(grant, "raw-oidc-token", CancellationToken.None);

        await tokenStore.Received(1).CreateAsync(
            Arg.Any<string>(), "raw-oidc-token", TimeSpan.FromMinutes(7), cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WriteAsync_PropagationEnabled_WritesTokenToCorrectPhysicalBucket()
    {
        var tokenStore = Substitute.For<INatsKVStore>();
        var assertionStore = Substitute.For<INatsKVStore>();
        var kvContext = ContextReturning(tokenStore, assertionStore);
        var signer = SignerReturning(new SignedAssertion("header.payload.sig", "kid-1"));
        var writer = new KvIdentityStoreWriter(kvContext, signer, Substitute.For<ISecretResolver>(), FixedTimeProvider());
        var grant = IdentityTestBuilders.GrantFor("notes", propagationEnabled: true, selfLookupKey: "jti-1", FixedNow.AddMinutes(5));

        await writer.WriteAsync(grant, "raw-oidc-token", CancellationToken.None);

        await kvContext.Received(1).GetStoreAsync("callout_apps_notes_oidc_tokens", Arg.Any<CancellationToken>());
        await tokenStore.Received(1).CreateAsync(
            Arg.Any<string>(), "raw-oidc-token", Arg.Any<TimeSpan>(), cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WriteAsync_PropagationEnabled_PutsAssertionAtSanitizedSelfLookupKeyInAssertionsBucket()
    {
        var kvContext = ContextReturning(Substitute.For<INatsKVStore>(), Substitute.For<INatsKVStore>());
        var signer = SignerReturning(new SignedAssertion("header.payload.sig", "kid-1"));
        var writer = new KvIdentityStoreWriter(kvContext, signer, Substitute.For<ISecretResolver>(), FixedTimeProvider());
        // ADR 0013 key derivation: sanitized (lowercase/underscore) plus a 16-hex SHA-256
        // suffix of the raw value, so sanitization aliases cannot collide across principals.
        var grant = IdentityTestBuilders.GrantFor("notes", propagationEnabled: true, selfLookupKey: "JTI.With Spaces", FixedNow.AddMinutes(5));

        await writer.WriteAsync(grant, "raw-oidc-token", CancellationToken.None);

        await kvContext.JetStreamContext.Received(1).PublishAsync(
            $"$KV.callout_apps_notes_assertions.{AppSlug.DeriveKeySegment("JTI.With Spaces")}",
            "header.payload.sig",
            Arg.Any<INatsSerialize<string>?>(),
            Arg.Any<NatsJSPubOpts?>(),
            Arg.Any<NatsHeaders?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WriteAsync_AssertionPut_CarriesPerMessageTtlExactlyExpMinusNow_InWholeSeconds()
    {
        // D17: TTL = exp - now, no clamp, formatted the same way the KV client formats the
        // token record's own Nats-TTL header (whole seconds + "s") so both records expire together.
        var kvContext = ContextReturning(Substitute.For<INatsKVStore>(), Substitute.For<INatsKVStore>());
        var signer = SignerReturning(new SignedAssertion("header.payload.sig", "kid-1"));
        var writer = new KvIdentityStoreWriter(kvContext, signer, Substitute.For<ISecretResolver>(), FixedTimeProvider());
        var grant = IdentityTestBuilders.GrantFor("notes", propagationEnabled: true, selfLookupKey: "jti-1", FixedNow.AddMinutes(7));

        await writer.WriteAsync(grant, "raw-oidc-token", CancellationToken.None);

        await kvContext.JetStreamContext.Received(1).PublishAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<INatsSerialize<string>?>(),
            Arg.Any<NatsJSPubOpts?>(),
            Arg.Is<NatsHeaders?>(h => h != null && h["Nats-TTL"] == "420s"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WriteAsync_ExistingAssertionAtSameKey_IsReplacedBySinglePut_NoPurgeOrRetry()
    {
        // The self-lookup key legitimately collides (same jti across connections of one token,
        // or a stable sub on the fallback path, doc 07). A KV put is last-writer-wins by
        // definition, so the replace is exactly one publish: no create-collision, get, or purge
        // round trips (those were 15-26ms of every request under load).
        var assertionStore = Substitute.For<INatsKVStore>();
        var kvContext = ContextReturning(Substitute.For<INatsKVStore>(), assertionStore);
        var signer = SignerReturning(new SignedAssertion("header.payload.sig", "kid-1"));
        var writer = new KvIdentityStoreWriter(kvContext, signer, Substitute.For<ISecretResolver>(), FixedTimeProvider());
        var grant = IdentityTestBuilders.GrantFor("notes", propagationEnabled: true, selfLookupKey: "jti-1", FixedNow.AddMinutes(5));

        var first = await writer.WriteAsync(grant, "raw-oidc-token", CancellationToken.None);
        var second = await writer.WriteAsync(grant, "raw-oidc-token", CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        await kvContext.JetStreamContext.Received(2).PublishAsync(
            $"$KV.callout_apps_notes_assertions.{AppSlug.DeriveKeySegment("jti-1")}",
            Arg.Any<string>(),
            Arg.Any<INatsSerialize<string>?>(),
            Arg.Any<NatsJSPubOpts?>(),
            Arg.Any<NatsHeaders?>(),
            Arg.Any<CancellationToken>());
        Assert.Empty(assertionStore.ReceivedCalls());
    }

    [Fact]
    public async Task WriteAsync_RepeatedWrites_LookUpTokenStoreOnce_NotPerRequest()
    {
        // GetStoreAsync is a $JS.API.STREAM.INFO round trip; it must not run on every request.
        var kvContext = ContextReturning(Substitute.For<INatsKVStore>(), Substitute.For<INatsKVStore>());
        var signer = SignerReturning(new SignedAssertion("header.payload.sig", "kid-1"));
        var writer = new KvIdentityStoreWriter(kvContext, signer, Substitute.For<ISecretResolver>(), FixedTimeProvider());
        var grant = IdentityTestBuilders.GrantFor("notes", propagationEnabled: true, selfLookupKey: "jti-1", FixedNow.AddMinutes(5));

        for (var i = 0; i < 5; i++)
        {
            Assert.True((await writer.WriteAsync(grant, "raw-oidc-token", CancellationToken.None)).Succeeded);
        }

        await kvContext.Received(1).GetStoreAsync("callout_apps_notes_oidc_tokens", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WriteAsync_StoreLookupFails_FailsClosed_AndIsRetriedOnNextRequest()
    {
        // A failed lookup must fail that grant closed and must never be cached: the next
        // request looks the store up again rather than reusing the failure.
        var tokenStore = Substitute.For<INatsKVStore>();
        var kvContext = ContextReturning(tokenStore, Substitute.For<INatsKVStore>());
        var calls = 0;
        kvContext.GetStoreAsync("callout_apps_notes_oidc_tokens", Arg.Any<CancellationToken>())
            .Returns(_ => Interlocked.Increment(ref calls) == 1
                ? ValueTask.FromException<INatsKVStore>(new NatsException("stream info failed"))
                : ValueTask.FromResult(tokenStore));
        var signer = SignerReturning(new SignedAssertion("header.payload.sig", "kid-1"));
        var writer = new KvIdentityStoreWriter(kvContext, signer, Substitute.For<ISecretResolver>(), FixedTimeProvider());
        var grant = IdentityTestBuilders.GrantFor("notes", propagationEnabled: true, selfLookupKey: "jti-1", FixedNow.AddMinutes(5));

        var failed = await writer.WriteAsync(grant, "raw-oidc-token", CancellationToken.None);
        var retried = await writer.WriteAsync(grant, "raw-oidc-token", CancellationToken.None);

        Assert.False(failed.Succeeded);
        Assert.True(retried.Succeeded);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task WriteAsync_AssertionPutRejectedByServer_FailsClosed()
    {
        // A JetStream error ack (e.g. stream unavailable, TTL not enabled) is a real failure and
        // must fail the grant closed.
        var kvContext = ContextReturning(Substitute.For<INatsKVStore>(), Substitute.For<INatsKVStore>());
        kvContext.JetStreamContext
            .PublishAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<INatsSerialize<string>?>(), Arg.Any<NatsJSPubOpts?>(), Arg.Any<NatsHeaders?>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<PubAckResponse>(new PubAckResponse { Error = new ApiError { Code = 503, Description = "unavailable" } }));
        var signer = SignerReturning(new SignedAssertion("header.payload.sig", "kid-1"));
        var writer = new KvIdentityStoreWriter(kvContext, signer, Substitute.For<ISecretResolver>(), FixedTimeProvider());
        var grant = IdentityTestBuilders.GrantFor("notes", propagationEnabled: true, selfLookupKey: "jti-1", FixedNow.AddMinutes(5));

        var result = await writer.WriteAsync(grant, "raw-oidc-token", CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task WriteAsync_AssertionPublishThrows_FailsClosed()
    {
        var kvContext = ContextReturning(Substitute.For<INatsKVStore>(), Substitute.For<INatsKVStore>());
        kvContext.JetStreamContext
            .PublishAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<INatsSerialize<string>?>(), Arg.Any<NatsJSPubOpts?>(), Arg.Any<NatsHeaders?>(), Arg.Any<CancellationToken>())
            .ThrowsAsyncForAnyArgs(new NatsJSPublishNoResponseException());
        var signer = SignerReturning(new SignedAssertion("header.payload.sig", "kid-1"));
        var writer = new KvIdentityStoreWriter(kvContext, signer, Substitute.For<ISecretResolver>(), FixedTimeProvider());
        var grant = IdentityTestBuilders.GrantFor("notes", propagationEnabled: true, selfLookupKey: "jti-1", FixedNow.AddMinutes(5));

        var result = await writer.WriteAsync(grant, "raw-oidc-token", CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task WriteAsync_PropagationEnabled_SignsAssertionWithConnectionIdSubjectAppIssuerExpiry()
    {
        var tokenStore = Substitute.For<INatsKVStore>();
        var assertionStore = Substitute.For<INatsKVStore>();
        var kvContext = ContextReturning(tokenStore, assertionStore);
        var signer = SignerReturning(new SignedAssertion("header.payload.sig", "kid-1"));
        var writer = new KvIdentityStoreWriter(kvContext, signer, Substitute.For<ISecretResolver>(), FixedTimeProvider());
        var expiry = FixedNow.AddMinutes(5);
        var grant = IdentityTestBuilders.GrantFor("notes", propagationEnabled: true, selfLookupKey: "jti-1", expiry, principal: "sub-42", issuer: "https://id.example.com/realms/main");

        var result = await writer.WriteAsync(grant, "raw-oidc-token", CancellationToken.None);

        var expectedClaims = new AssertionClaims(result.ConnectionId!, "sub-42", "notes", "https://id.example.com/realms/main", expiry);
        signer.Received(1).Sign(expectedClaims);
    }

    [Fact]
    public async Task WriteAsync_EachCall_GeneratesADifferentConnectionId()
    {
        var kvContext = ContextReturning(Substitute.For<INatsKVStore>(), Substitute.For<INatsKVStore>());
        var signer = SignerReturning(new SignedAssertion("header.payload.sig", "kid-1"));
        var writer = new KvIdentityStoreWriter(kvContext, signer, Substitute.For<ISecretResolver>(), FixedTimeProvider());
        var grant = IdentityTestBuilders.GrantFor("notes", propagationEnabled: true, selfLookupKey: "jti-1", FixedNow.AddMinutes(5));

        var first = await writer.WriteAsync(grant, "raw-oidc-token", CancellationToken.None);
        var second = await writer.WriteAsync(grant, "raw-oidc-token", CancellationToken.None);

        Assert.NotNull(first.ConnectionId);
        Assert.NotNull(second.ConnectionId);
        Assert.NotEqual(first.ConnectionId, second.ConnectionId);
    }

    [Fact]
    public async Task WriteAsync_TokenStoreWriteThrows_ReturnsFailed_DoesNotPropagateException()
    {
        var tokenStore = Substitute.For<INatsKVStore>();
        tokenStore.CreateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), cancellationToken: Arg.Any<CancellationToken>())
            .ThrowsAsyncForAnyArgs(new NatsException("kv unavailable"));
        var kvContext = ContextReturning(tokenStore, Substitute.For<INatsKVStore>());
        var signer = SignerReturning(new SignedAssertion("header.payload.sig", "kid-1"));
        var writer = new KvIdentityStoreWriter(kvContext, signer, Substitute.For<ISecretResolver>(), FixedTimeProvider());
        var grant = IdentityTestBuilders.GrantFor("notes", propagationEnabled: true, selfLookupKey: "jti-1", FixedNow.AddMinutes(5));

        var result = await writer.WriteAsync(grant, "raw-oidc-token", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.ConnectionId);
    }

    [Fact]
    public async Task WriteAsync_AlreadyExpiredGrant_FailsClosed_WithoutWritingAnything()
    {
        var tokenStore = Substitute.For<INatsKVStore>();
        var kvContext = ContextReturning(tokenStore, Substitute.For<INatsKVStore>());
        var signer = Substitute.For<IAssertionSigner>();
        var writer = new KvIdentityStoreWriter(kvContext, signer, Substitute.For<ISecretResolver>(), FixedTimeProvider());
        var grant = IdentityTestBuilders.GrantFor("notes", propagationEnabled: true, selfLookupKey: "jti-1", FixedNow.AddMinutes(-1));

        var result = await writer.WriteAsync(grant, "raw-oidc-token", CancellationToken.None);

        Assert.False(result.Succeeded);
        await kvContext.DidNotReceiveWithAnyArgs().GetStoreAsync(default!, default);
    }

    [Fact]
    public async Task ProvisionBucketsAsync_PropagationEnabledApp_PrewarmsTokenStoreCache()
    {
        // Without this prewarm, WriteAsync's own first-ever GetStoreAsync call for the
        // bucket runs inline on the auth hot path (the callout_request_duration_ms outlier
        // this fix targets). ProvisionBucketsAsync must pay that round trip itself, up front.
        var tokenStore = Substitute.For<INatsKVStore>();
        var kvContext = ContextReturning(tokenStore, Substitute.For<INatsKVStore>());
        var writer = new KvIdentityStoreWriter(kvContext, Substitute.For<IAssertionSigner>(), Substitute.For<ISecretResolver>(), FixedTimeProvider());
        var routing = IdentityTestBuilders.RoutingFor(IdentityTestBuilders.ApplicationWithPropagation("notes", propagationEnabled: true));

        await writer.ProvisionBucketsAsync(routing, CancellationToken.None);

        await kvContext.Received(1).GetStoreAsync("callout_apps_notes_oidc_tokens", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProvisionBucketsAsync_ThenWriteAsync_DoesNotLookUpTokenStoreAgain()
    {
        // The prewarm must actually land in KvStoreCache, not just probe the server and
        // discard the result - WriteAsync's later lookup must be a cache hit.
        var tokenStore = Substitute.For<INatsKVStore>();
        var kvContext = ContextReturning(tokenStore, Substitute.For<INatsKVStore>());
        var signer = SignerReturning(new SignedAssertion("header.payload.sig", "kid-1"));
        var writer = new KvIdentityStoreWriter(kvContext, signer, Substitute.For<ISecretResolver>(), FixedTimeProvider());
        var routing = IdentityTestBuilders.RoutingFor(IdentityTestBuilders.ApplicationWithPropagation("notes", propagationEnabled: true));
        var grant = IdentityTestBuilders.GrantFor("notes", propagationEnabled: true, selfLookupKey: "jti-1", FixedNow.AddMinutes(5));

        await writer.ProvisionBucketsAsync(routing, CancellationToken.None);
        var result = await writer.WriteAsync(grant, "raw-oidc-token", CancellationToken.None);

        Assert.True(result.Succeeded);
        await kvContext.Received(1).GetStoreAsync("callout_apps_notes_oidc_tokens", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProvisionBucketsAsync_PropagationDisabledApp_DoesNotPrewarmTokenStoreCache()
    {
        var kvContext = ContextReturning(Substitute.For<INatsKVStore>(), Substitute.For<INatsKVStore>());
        var writer = new KvIdentityStoreWriter(kvContext, Substitute.For<IAssertionSigner>(), Substitute.For<ISecretResolver>(), FixedTimeProvider());
        var routing = IdentityTestBuilders.RoutingFor(IdentityTestBuilders.ApplicationWithPropagation("tasks", propagationEnabled: false));

        await writer.ProvisionBucketsAsync(routing, CancellationToken.None);

        await kvContext.DidNotReceiveWithAnyArgs().GetStoreAsync(default!, default);
    }

    [Fact]
    public async Task ProvisionBucketsAsync_MultiplePropagationEnabledApps_PrewarmsEachTokenStoreCache()
    {
        var kvContext = Substitute.For<INatsKVContext>();
        kvContext.GetStoreAsync("callout_apps_notes_oidc_tokens", Arg.Any<CancellationToken>()).Returns(Substitute.For<INatsKVStore>());
        kvContext.GetStoreAsync("callout_apps_tasks_oidc_tokens", Arg.Any<CancellationToken>()).Returns(Substitute.For<INatsKVStore>());
        var writer = new KvIdentityStoreWriter(kvContext, Substitute.For<IAssertionSigner>(), Substitute.For<ISecretResolver>(), FixedTimeProvider());
        var routing = IdentityTestBuilders.RoutingFor(
            IdentityTestBuilders.ApplicationWithPropagation("notes", propagationEnabled: true),
            IdentityTestBuilders.ApplicationWithPropagation("tasks", propagationEnabled: true));

        await writer.ProvisionBucketsAsync(routing, CancellationToken.None);

        await kvContext.Received(1).GetStoreAsync("callout_apps_notes_oidc_tokens", Arg.Any<CancellationToken>());
        await kvContext.Received(1).GetStoreAsync("callout_apps_tasks_oidc_tokens", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProvisionBucketsAsync_PrewarmLookupThrows_DoesNotPropagate_StillReturns()
    {
        // Best-effort: a transient KV failure during prewarm must not stop provisioning from
        // completing (the caller still needs to run PublishKeysAndDiscoveryAsync right after).
        var kvContext = Substitute.For<INatsKVContext>();
        kvContext.GetStoreAsync("callout_apps_notes_oidc_tokens", Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromException<INatsKVStore>(new NatsException("stream info failed")));
        var writer = new KvIdentityStoreWriter(kvContext, Substitute.For<IAssertionSigner>(), Substitute.For<ISecretResolver>(), FixedTimeProvider());
        var routing = IdentityTestBuilders.RoutingFor(IdentityTestBuilders.ApplicationWithPropagation("notes", propagationEnabled: true));

        var task = writer.ProvisionBucketsAsync(routing, CancellationToken.None);
        await task;

        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ProvisionBucketsAsync_PrewarmLookupThrows_NextWriteAsyncStillRetriesLookup()
    {
        // A failed prewarm must not be cached as a permanent failure (mirrors
        // WriteAsync_StoreLookupFails_FailsClosed_AndIsRetriedOnNextRequest): the KvStoreCache
        // contract evicts failed lookups, so the real request after a failed prewarm gets its
        // own fresh attempt rather than reusing the prewarm's failure.
        var tokenStore = Substitute.For<INatsKVStore>();
        var kvContext = ContextReturning(tokenStore, Substitute.For<INatsKVStore>());
        var calls = 0;
        kvContext.GetStoreAsync("callout_apps_notes_oidc_tokens", Arg.Any<CancellationToken>())
            .Returns(_ => Interlocked.Increment(ref calls) == 1
                ? ValueTask.FromException<INatsKVStore>(new NatsException("stream info failed"))
                : ValueTask.FromResult(tokenStore));
        var signer = SignerReturning(new SignedAssertion("header.payload.sig", "kid-1"));
        var writer = new KvIdentityStoreWriter(kvContext, signer, Substitute.For<ISecretResolver>(), FixedTimeProvider());
        var routing = IdentityTestBuilders.RoutingFor(IdentityTestBuilders.ApplicationWithPropagation("notes", propagationEnabled: true));
        var grant = IdentityTestBuilders.GrantFor("notes", propagationEnabled: true, selfLookupKey: "jti-1", FixedNow.AddMinutes(5));

        await writer.ProvisionBucketsAsync(routing, CancellationToken.None);
        var result = await writer.WriteAsync(grant, "raw-oidc-token", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2, calls);
    }

    private static TimeProvider FixedTimeProvider() => new FixedTimeProviderImpl(FixedNow);

    private static INatsKVContext ContextReturning(INatsKVStore tokenStore, INatsKVStore assertionStore)
    {
        var kvContext = Substitute.For<INatsKVContext>();
        kvContext.GetStoreAsync("callout_apps_notes_oidc_tokens", Arg.Any<CancellationToken>()).Returns(tokenStore);
        kvContext.GetStoreAsync("callout_apps_notes_assertions", Arg.Any<CancellationToken>()).Returns(assertionStore);
        var js = Substitute.For<INatsJSContext>();
        js.PublishAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<INatsSerialize<string>?>(), Arg.Any<NatsJSPubOpts?>(), Arg.Any<NatsHeaders?>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<PubAckResponse>(new PubAckResponse { Stream = "KV_callout_apps_notes_assertions", Seq = 1 }));
        kvContext.JetStreamContext.Returns(js);
        return kvContext;
    }

    private static IAssertionSigner SignerReturning(SignedAssertion signed)
    {
        var signer = Substitute.For<IAssertionSigner>();
        signer.Sign(Arg.Any<AssertionClaims>()).Returns(signed);
        return signer;
    }

    private sealed class FixedTimeProviderImpl(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
