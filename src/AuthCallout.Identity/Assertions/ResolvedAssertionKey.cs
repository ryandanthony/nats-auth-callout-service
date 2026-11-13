// -----------------------------------------------------------------------
// <copyright file="ResolvedAssertionKey.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Identity.Assertions;

/// <summary>
/// One entry of the assertion signing keyset (ADR 0011, ADR 0014) with its
/// <see cref="AuthCallout.Contracts.Primitives.SecretRef"/> already resolved to a plaintext Ed25519
/// NKey seed. <see cref="AssertionSigner"/> consumes a list of these; the caller (P13's
/// startup composition) resolves each <see cref="AuthCallout.Contracts.Config.AssertionKey.Key"/>
/// via <see cref="AuthCallout.Contracts.Seams.ISecretResolver"/> once, since resolution is a
/// startup-time-only concern (doc 04) and <see cref="AssertionSigner"/> itself is pure/no-I/O
/// per its Contracts doc comment.
/// </summary>
/// <param name="Kid">The key id (ADR 0013: must not be <c>current</c>; enforced by config-load validation, not re-checked here).</param>
/// <param name="Seed">The resolved Ed25519 NKey seed (an <c>SUA...</c>-style seed; NKey type validated by <see cref="AssertionSigner"/>'s construction).</param>
/// <param name="Active">True for the single key that signs new assertions (exactly one, enforced by config-load validation).</param>
public sealed record ResolvedAssertionKey(string Kid, string Seed, bool Active);
