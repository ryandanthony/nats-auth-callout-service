// -----------------------------------------------------------------------
// <copyright file="AssertionKeyResolver.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Seams;
using NATS.NKeys;

namespace AuthCallout.Nats.Identity;

/// <summary>
/// Resolves each bootstrap <see cref="AssertionKey"/>'s seed to its derived Ed25519 public
/// key (ADR 0011: "only the assertion public key is ever published"), reducing every entry
/// to the shape <see cref="Identity.KeysPublisher"/> is allowed to touch
/// (<see cref="ResolvedPublicAssertionKey"/>). Kept separate from
/// <see cref="Identity.KvIdentityStoreWriter"/> purely to keep that type's own
/// class-coupling count under the repo's analyzer limit; not a new abstraction beyond what
/// P13's own bootstrap-secret resolution already does for <c>ResolvedAssertionKey</c>.
/// </summary>
public static class AssertionKeyResolver
{
    /// <summary>
    /// Resolves every entry's seed and derives its public key. A null or empty
    /// <paramref name="assertionKeys"/> resolves to an empty list (no assertion keyset
    /// configured — nothing to publish).
    /// </summary>
    /// <param name="assertionKeys">The bootstrap assertion keyset.</param>
    /// <param name="secretResolver">Resolves each key's <see cref="AssertionKey.Key"/> seed reference.</param>
    /// <param name="cancellationToken">Cancellation for the resolution attempt.</param>
    /// <returns>The resolved public-key entries, in the same order as <paramref name="assertionKeys"/>.</returns>
    public static async Task<IReadOnlyList<ResolvedPublicAssertionKey>> ResolveAsync(
        IReadOnlyList<AssertionKey>? assertionKeys, ISecretResolver secretResolver, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(secretResolver);

        var resolved = new List<ResolvedPublicAssertionKey>();
        if (assertionKeys is null)
        {
            return resolved;
        }

        foreach (var key in assertionKeys)
        {
            using var secret = await secretResolver.ResolveAsync(key.Key, cancellationToken).ConfigureAwait(false);
            using var keyPair = KeyPair.FromSeed(secret.Reveal());
            resolved.Add(new ResolvedPublicAssertionKey(key.Kid, keyPair.GetPublicKey(), key.Active));
        }

        return resolved;
    }
}
