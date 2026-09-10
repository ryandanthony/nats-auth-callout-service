// -----------------------------------------------------------------------
// <copyright file="AssertionVerification.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Seams;
using AuthCallout.Identity.Assertions;
using NATS.NKeys;

namespace AuthCallout.Dev.Samples.Common;

/// <summary>
/// A thin adapter over P08's <see cref="AssertionSigner"/> (the frozen
/// <see cref="IAssertionSigner"/> implementation, whose packet file explicitly lists
/// "sample services" among <c>Verify</c>'s intended reusers) so a verify-only party — a
/// subscriber that never signs anything, exactly this sample's situation — can call
/// <see cref="IAssertionSigner.Verify"/> without needing a real signing keyset of its own.
/// <see cref="AssertionSigner"/>'s constructor requires exactly one <c>Active</c> keyset
/// entry (ADR 0014's keyset invariant) purely so it can also support signing; <c>Verify</c>
/// itself never touches the active key, so a single throwaway, never-signed-with,
/// process-local key pair satisfies that invariant without representing any real signing
/// authority. Not a new verification abstraction — this delegates to the real one.
/// </summary>
public static class AssertionVerification
{
    /// <summary>Verifies a compact identity-assertion JWT against a supplied public key (ADR 0011).</summary>
    /// <param name="compactJwt">The compact assertion JWT read off a <c>Callout-Identity</c> header.</param>
    /// <param name="publicKey">The signer's public key, fetched from the assertion-keys bucket by the JWT's <c>kid</c>.</param>
    /// <returns>The verified claims, or null if verification fails.</returns>
    public static AssertionClaims? Verify(string compactJwt, string publicKey)
    {
        using var throwawayActiveKey = KeyPair.CreatePair(PrefixByte.Account);
        using var signer = new AssertionSigner([new ResolvedAssertionKey("throwaway-verify-only", throwawayActiveKey.GetSeed(), Active: true)]);
        return signer.Verify(compactJwt, publicKey);
    }
}
