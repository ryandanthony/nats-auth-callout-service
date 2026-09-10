// -----------------------------------------------------------------------
// <copyright file="KvIdentityStoreWriterTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Seams;
using AuthCallout.Nats.Identity;
using NATS.Client.Core;
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
    public async Task WriteAsync_PropagationEnabled_ReadsAndWritesFromCorrectPhysicalBuckets()
    {
        var tokenStore = Substitute.For<INatsKVStore>();
        var assertionStore = Substitute.For<INatsKVStore>();
        var kvContext = ContextReturning(tokenStore, assertionStore);
        var signer = SignerReturning(new SignedAssertion("header.payload.sig", "kid-1"));
        var writer = new KvIdentityStoreWriter(kvContext, signer, Substitute.For<ISecretResolver>(), FixedTimeProvider());
        var grant = IdentityTestBuilders.GrantFor("notes", propagationEnabled: true, selfLookupKey: "jti-1", FixedNow.AddMinutes(5));

        await writer.WriteAsync(grant, "raw-oidc-token", CancellationToken.None);

        await kvContext.Received(1).GetStoreAsync("callout_apps_notes_oidc_tokens", Arg.Any<CancellationToken>());
        await kvContext.Received(1).GetStoreAsync("callout_apps_notes_assertions", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WriteAsync_PropagationEnabled_WritesAssertionAtSanitizedSelfLookupKey()
    {
        var tokenStore = Substitute.For<INatsKVStore>();
        var assertionStore = Substitute.For<INatsKVStore>();
        var kvContext = ContextReturning(tokenStore, assertionStore);
        var signer = SignerReturning(new SignedAssertion("header.payload.sig", "kid-1"));
        var writer = new KvIdentityStoreWriter(kvContext, signer, Substitute.For<ISecretResolver>(), FixedTimeProvider());
        // ADR 0013 key-safety transform: uppercase and unsafe chars get sanitized to lowercase/underscore.
        var grant = IdentityTestBuilders.GrantFor("notes", propagationEnabled: true, selfLookupKey: "JTI.With Spaces", FixedNow.AddMinutes(5));

        await writer.WriteAsync(grant, "raw-oidc-token", CancellationToken.None);

        await assertionStore.Received(1).TryCreateAsync(
            "jti_with_spaces", "header.payload.sig", TimeSpan.FromMinutes(5), cancellationToken: Arg.Any<CancellationToken>());
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

    private static TimeProvider FixedTimeProvider() => new FixedTimeProviderImpl(FixedNow);

    private static INatsKVContext ContextReturning(INatsKVStore tokenStore, INatsKVStore assertionStore)
    {
        var kvContext = Substitute.For<INatsKVContext>();
        kvContext.GetStoreAsync("callout_apps_notes_oidc_tokens", Arg.Any<CancellationToken>()).Returns(tokenStore);
        kvContext.GetStoreAsync("callout_apps_notes_assertions", Arg.Any<CancellationToken>()).Returns(assertionStore);
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
