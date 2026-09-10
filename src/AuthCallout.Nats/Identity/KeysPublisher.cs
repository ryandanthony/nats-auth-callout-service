// -----------------------------------------------------------------------
// <copyright file="KeysPublisher.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using NATS.Client.KeyValueStore;

namespace AuthCallout.Nats.Identity;

/// <summary>
/// Publishes the assertion signing keyset's public keys and reconciles the
/// <c>callout_assertion_keys</c> bucket to the configured keyset (ADR 0011, ADR 0014). On
/// every startup and keyset change: every key in the list (active and non-active) gets its
/// public key published at <c>{kid}</c>, <c>current</c> is pointed at the active
/// <c>kid</c>, and any bucket entry whose <c>kid</c> is no longer in the list is deleted
/// (retirement, ADR 0014 §What the callout publishes). Never publishes a seed — only the
/// derived public key.
/// </summary>
public sealed class KeysPublisher
{
    private readonly INatsKVContext _kvContext;

    /// <summary>Initializes a new instance of the <see cref="KeysPublisher"/> class.</summary>
    /// <param name="kvContext">The KV context to publish keys against (the callout's own connection).</param>
    public KeysPublisher(INatsKVContext kvContext)
    {
        ArgumentNullException.ThrowIfNull(kvContext);
        _kvContext = kvContext;
    }

    /// <summary>
    /// Reconciles the assertion-keys bucket to <paramref name="keyset"/>: publishes every
    /// key's public key, points <c>current</c> at the active entry, and deletes any
    /// existing bucket entry whose <c>kid</c> no longer appears in the list (ADR 0014
    /// retirement). A null or empty keyset publishes nothing and deletes nothing — callers
    /// (P13) only invoke this when at least one application actually needs identity
    /// propagation; an empty keyset with propagation-enabled applications is a config-load
    /// error caught upstream, not this type's concern.
    /// </summary>
    /// <param name="keyset">The bootstrap assertion keyset, resolved keys with only <see cref="AssertionKey.Kid"/> and the derived public key ever touching KV.</param>
    /// <param name="cancellationToken">Cancellation for the publish/reconcile attempt.</param>
    /// <returns>A task that completes when the bucket has been reconciled.</returns>
    public async Task PublishAsync(IReadOnlyList<ResolvedPublicAssertionKey> keyset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keyset);

        if (keyset.Count == 0)
        {
            return;
        }

        // create-or-update rather than GetStoreAsync: this can run before (or independent
        // of) BucketProvisioner's own pass, and it must not assume the bucket already
        // exists (idempotent per the D16 create-or-update pattern this type shares with
        // BucketProvisioner).
        var store = await _kvContext.CreateOrUpdateStoreAsync(new NatsKVConfig(AppSlug.AssertionKeysBucket), cancellationToken).ConfigureAwait(false);

        var desiredKids = new HashSet<string>(StringComparer.Ordinal);
        string? activeKid = null;
        foreach (var entry in keyset)
        {
            desiredKids.Add(entry.Kid);
            if (entry.Active)
            {
                activeKid = entry.Kid;
            }

            await store.PutAsync(entry.Kid, entry.PublicKey, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        if (activeKid is not null)
        {
            await store.PutAsync(AppSlug.AssertionKeysCurrentKey, activeKid, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        await RetireStaleKeysAsync(store, desiredKids, cancellationToken).ConfigureAwait(false);
    }

    private static async Task RetireStaleKeysAsync(INatsKVStore store, HashSet<string> desiredKids, CancellationToken cancellationToken)
    {
        await foreach (var key in store.GetKeysAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            if (string.Equals(key, AppSlug.AssertionKeysCurrentKey, StringComparison.Ordinal) || desiredKids.Contains(key))
            {
                continue;
            }

            await store.DeleteAsync(key, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
