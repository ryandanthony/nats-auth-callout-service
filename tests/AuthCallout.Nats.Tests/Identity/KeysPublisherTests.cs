// -----------------------------------------------------------------------
// <copyright file="KeysPublisherTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Nats.Identity;
using NATS.Client.KeyValueStore;
using NATS.NKeys;
using NSubstitute;

namespace AuthCallout.Nats.Tests.Identity;

/// <summary>
/// Unit tests for <see cref="KeysPublisher"/> (P08 ADR→test map: "Keys bucket never
/// receives a private key" ADR 0011, "current pointer + per-kid entries on rotation" ADR
/// 0014). Asserts against a substituted <see cref="INatsKVStore"/>: every configured key's
/// public half is written under its own <c>kid</c>, <c>current</c> points at the active
/// entry, and any bucket entry no longer in the desired keyset is deleted (retirement).
/// </summary>
public sealed class KeysPublisherTests
{
    [Fact]
    public async Task PublishAsync_EveryKeyEntry_PublishesPublicKey_NeverTheSeed()
    {
        using var keyPairA = KeyPair.CreatePair(PrefixByte.Account);
        using var keyPairB = KeyPair.CreatePair(PrefixByte.Account);
        var store = SubstituteStoreWithNoExistingKeys();
        var kvContext = ContextReturning(store);
        var publisher = new KeysPublisher(kvContext);
        var keyset = new[]
        {
            new ResolvedPublicAssertionKey("kid-a", keyPairA.GetPublicKey(), Active: false),
            new ResolvedPublicAssertionKey("kid-b", keyPairB.GetPublicKey(), Active: true),
        };

        await publisher.PublishAsync(keyset, CancellationToken.None);

        await store.Received(1).PutAsync("kid-a", keyPairA.GetPublicKey(), cancellationToken: Arg.Any<CancellationToken>());
        await store.Received(1).PutAsync("kid-b", keyPairB.GetPublicKey(), cancellationToken: Arg.Any<CancellationToken>());
        await store.DidNotReceive().PutAsync("kid-a", keyPairA.GetSeed(), cancellationToken: Arg.Any<CancellationToken>());
        await store.DidNotReceive().PutAsync("kid-b", keyPairB.GetSeed(), cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_PointsCurrentAtTheActiveKid()
    {
        var store = SubstituteStoreWithNoExistingKeys();
        var kvContext = ContextReturning(store);
        var publisher = new KeysPublisher(kvContext);
        var keyset = new[]
        {
            new ResolvedPublicAssertionKey("kid-old", "pub-old", Active: false),
            new ResolvedPublicAssertionKey("kid-new", "pub-new", Active: true),
        };

        await publisher.PublishAsync(keyset, CancellationToken.None);

        await store.Received(1).PutAsync(AppSlug.AssertionKeysCurrentKey, "kid-new", cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_ExistingKidNoLongerInList_IsDeleted()
    {
        // ADR 0014 §Rotation procedure step 3 "Retire": removing a key from the bootstrap
        // list must delete its keys-bucket entry.
        var store = SubstituteStoreWithExistingKeys("kid-retired", "kid-keep", AppSlug.AssertionKeysCurrentKey);
        var kvContext = ContextReturning(store);
        var publisher = new KeysPublisher(kvContext);
        var keyset = new[] { new ResolvedPublicAssertionKey("kid-keep", "pub-keep", Active: true) };

        await publisher.PublishAsync(keyset, CancellationToken.None);

        await store.Received(1).DeleteAsync("kid-retired", cancellationToken: Arg.Any<CancellationToken>());
        await store.DidNotReceive().DeleteAsync("kid-keep", cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_NeverDeletesTheCurrentPointerKey()
    {
        var store = SubstituteStoreWithExistingKeys(AppSlug.AssertionKeysCurrentKey);
        var kvContext = ContextReturning(store);
        var publisher = new KeysPublisher(kvContext);
        var keyset = new[] { new ResolvedPublicAssertionKey("kid-a", "pub-a", Active: true) };

        await publisher.PublishAsync(keyset, CancellationToken.None);

        await store.DidNotReceive().DeleteAsync(AppSlug.AssertionKeysCurrentKey, cancellationToken: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_EmptyKeyset_PublishesNothing_TouchesNoStore()
    {
        var kvContext = Substitute.For<INatsKVContext>();
        var publisher = new KeysPublisher(kvContext);

        await publisher.PublishAsync([], CancellationToken.None);

        await kvContext.DidNotReceiveWithAnyArgs().CreateOrUpdateStoreAsync(default!, default);
    }

    private static INatsKVStore SubstituteStoreWithNoExistingKeys() => SubstituteStoreWithExistingKeys();

    private static INatsKVStore SubstituteStoreWithExistingKeys(params string[] existingKeys)
    {
        var store = Substitute.For<INatsKVStore>();
        store.GetKeysAsync(cancellationToken: Arg.Any<CancellationToken>()).Returns(ToAsyncEnumerable(existingKeys));
        return store;
    }

    private static INatsKVContext ContextReturning(INatsKVStore store)
    {
        var kvContext = Substitute.For<INatsKVContext>();
        kvContext.CreateOrUpdateStoreAsync(Arg.Any<NatsKVConfig>(), Arg.Any<CancellationToken>()).Returns(store);
        return kvContext;
    }

    private static async IAsyncEnumerable<string> ToAsyncEnumerable(IEnumerable<string> values)
    {
        foreach (var value in values)
        {
            yield return value;
        }

        await Task.CompletedTask;
    }
}
