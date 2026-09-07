// -----------------------------------------------------------------------
// <copyright file="IAssertionSigner.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Contracts.Seams;

/// <summary>
/// Signs and verifies identity assertions (ADR 0011, ADR 0014) using the bootstrap
/// assertion keyset. Pure, no I/O (the keyset arrives already resolved from
/// <see cref="Config.BootstrapConfig.AssertionKeys"/> via <see cref="ISecretResolver"/> at
/// startup). Implemented by <c>AuthCallout.Identity</c> (P08); consumed by the identity
/// store writer (P08/P07) when signing, and by verifiers (test fixtures, sample services,
/// P14) when verifying.
/// </summary>
public interface IAssertionSigner
{
    /// <summary>Signs an assertion with the keyset's currently-active key (ADR 0014).</summary>
    /// <param name="claims">The claims to bind into the assertion.</param>
    /// <returns>The signed, compact assertion JWT plus its signing key's <c>kid</c>.</returns>
    SignedAssertion Sign(AssertionClaims claims);

    /// <summary>
    /// Verifies a compact assertion JWT's signature against a supplied public key
    /// (typically fetched from the assertion-keys bucket by the <c>kid</c> in the JWT's
    /// header). Returns the verified claims, or null if the signature or claims shape is
    /// invalid.
    /// </summary>
    /// <param name="compactJwt">The compact assertion JWT to verify.</param>
    /// <param name="publicKey">The signer's public key to verify against.</param>
    /// <returns>The verified claims, or null if verification fails.</returns>
    AssertionClaims? Verify(string compactJwt, string publicKey);
}
