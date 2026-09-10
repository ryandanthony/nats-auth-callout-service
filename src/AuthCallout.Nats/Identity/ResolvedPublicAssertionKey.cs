// -----------------------------------------------------------------------
// <copyright file="ResolvedPublicAssertionKey.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Nats.Identity;

/// <summary>
/// One assertion-keyset entry reduced to what <see cref="KeysPublisher"/> is allowed to
/// touch: the <c>kid</c>, the derived Ed25519 public key, and whether it is the active
/// signer (ADR 0011: "only the assertion public key is ever published"). Deliberately
/// carries no seed — constructing one from a <see cref="AuthCallout.Contracts.Config.AssertionKey"/> requires deriving
/// the public key via <c>KeyPair.FromSeed</c> first, which only the caller building the
/// resolved keyset (P13, alongside <c>ResolvedAssertionKey</c>) does.
/// </summary>
/// <param name="Kid">The key id.</param>
/// <param name="PublicKey">The derived Ed25519 public key (never the seed).</param>
/// <param name="Active">True for the single key that signs new assertions.</param>
public sealed record ResolvedPublicAssertionKey(string Kid, string PublicKey, bool Active);
