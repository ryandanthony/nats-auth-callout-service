// -----------------------------------------------------------------------
// <copyright file="AssertionKey.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Primitives;

namespace AuthCallout.Contracts.Config;

/// <summary>
/// One entry in the assertion signing keyset (ADR 0011, ADR 0014). Exactly one entry across
/// a <see cref="BootstrapConfig.AssertionKeys"/> list is <see cref="Active"/> (the signer);
/// the rest are verify-only, kept so assertions signed before the last rotation still
/// verify. <see cref="Kid"/> must never be <c>current</c> — that value is reserved as the
/// pointer key in the assertion-keys bucket (ADR 0013).
/// </summary>
/// <param name="Kid">The key id stamped in each assertion header signed by this key (when active) and used as its public-key bucket key.</param>
/// <param name="Key">The Ed25519 signing seed, as a secret reference. Only the derived public key is ever published.</param>
/// <param name="Active">True for the single key currently used to sign new assertions.</param>
public sealed record AssertionKey(string Kid, SecretRef Key, bool Active)
{
    /// <summary>Gets the reserved <see cref="Kid"/> value forbidden by ADR 0013 (it collides with the current-pointer key).</summary>
    /// <value>The reserved <c>kid</c> value, <c>current</c>.</value>
    public static string ReservedCurrentKid => "current";
}
