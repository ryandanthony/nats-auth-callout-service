// -----------------------------------------------------------------------
// <copyright file="GeneratedNKeyPair.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using NATS.NKeys;

namespace AuthCallout.Integration.Support;

/// <summary>
/// A single, throwaway NATS NKey pair generated once per test run, holding both the seed
/// (private) and public key text so callers never re-derive one from the other. Used to mint
/// the issuer account key, the callout's own service-user key, and (when exercised) the
/// xkey curve key (ADR 0018) — every seed a real NKey, never a placeholder string, exactly
/// what <see cref="Host.Wiring.BootstrapSecrets"/> expects to resolve at startup.
/// </summary>
public sealed record GeneratedNKeyPair(string Seed, string PublicKey)
{
    /// <summary>Generates a fresh NKey pair with the given prefix (e.g. <see cref="PrefixByte.Account"/>, <see cref="PrefixByte.User"/>, <see cref="PrefixByte.Curve"/>).</summary>
    /// <param name="prefix">The NKey type prefix.</param>
    /// <returns>The generated seed/public-key pair.</returns>
    public static GeneratedNKeyPair Create(PrefixByte prefix)
    {
        using var keyPair = KeyPair.CreatePair(prefix);
        return new GeneratedNKeyPair(keyPair.GetSeed(), keyPair.GetPublicKey());
    }
}
