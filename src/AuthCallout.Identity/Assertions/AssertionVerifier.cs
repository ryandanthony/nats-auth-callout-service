// -----------------------------------------------------------------------
// <copyright file="AssertionVerifier.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using NATS.NKeys;

namespace AuthCallout.Identity.Assertions;

/// <summary>
/// A verify-only entry point over <see cref="AssertionSigner"/> (ADR 0020's consequence:
/// "<see cref="AssertionSigner"/> needs a verify-only entry point... today it demands a
/// throwaway active signing key just to verify"). <see cref="AssertionSigner"/>'s constructor
/// requires exactly one <see cref="ResolvedAssertionKey.Active"/> entry (ADR 0014's keyset
/// invariant) purely so it can also support signing; its <c>Verify</c> method itself never
/// touches the active key, so a verify-only party (a subscriber that never signs anything)
/// can call <see cref="Verify"/> here without constructing a real signing keyset of its own.
/// Not a new verification abstraction — this delegates to the real one via a single
/// throwaway, never-signed-with, process-local key pair.
/// </summary>
public static class AssertionVerifier
{
    /// <summary>Verifies a compact identity-assertion JWT against a supplied public key (ADR 0011).</summary>
    /// <param name="compactJwt">The compact assertion JWT read off a <c>Callout-Identity</c> header.</param>
    /// <param name="publicKey">The signer's public key, fetched from the assertion-keys bucket by the JWT's <c>kid</c>.</param>
    /// <returns>The verified claims, or null if verification fails.</returns>
    public static AuthCallout.Contracts.Seams.AssertionClaims? Verify(string compactJwt, string publicKey)
    {
        using var throwawayActiveKey = KeyPair.CreatePair(PrefixByte.Account);
        using var signer = new AssertionSigner([new ResolvedAssertionKey("throwaway-verify-only", throwawayActiveKey.GetSeed(), Active: true)]);
        return signer.Verify(compactJwt, publicKey);
    }
}
