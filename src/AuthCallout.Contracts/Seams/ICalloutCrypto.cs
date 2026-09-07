// -----------------------------------------------------------------------
// <copyright file="ICalloutCrypto.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Contracts.Seams;

/// <summary>
/// Xkey-based encryption/decryption for <c>$SYS.REQ.USER.AUTH</c> traffic (ADR 0018, D14).
/// Implemented by <c>AuthCallout.Nats</c> (P07): a real curve-seal implementation when
/// <see cref="Config.BootstrapConfig.XkeySeed"/> is configured, or a pass-through no-op
/// implementation when it is not — the responder (P07) always calls through this
/// interface rather than branching on configuration itself.
/// </summary>
public interface ICalloutCrypto
{
    /// <summary>Gets a value indicating whether this instance holds a real xkey seed (not the no-op implementation).</summary>
    /// <value>True when this instance holds a real xkey seed.</value>
    bool IsEnabled { get; }

    /// <summary>
    /// Decrypts (opens) an incoming request payload using this instance's curve seed and the
    /// server's ephemeral public xkey carried on the request. The no-op implementation
    /// returns the payload unchanged; it is the responder's (P07) job to reject an
    /// encrypted request arriving while unconfigured (fail closed) rather than this method
    /// silently "opening" ciphertext as if it were plaintext.
    /// </summary>
    /// <param name="encryptedPayload">The incoming request payload to decrypt.</param>
    /// <param name="senderXkeyPublic">The sender's ephemeral public xkey.</param>
    /// <returns>The decrypted payload (or the input unchanged, when unconfigured).</returns>
    CalloutCryptoResult Open(byte[] encryptedPayload, string senderXkeyPublic);

    /// <summary>
    /// Encrypts (seals) an outgoing response payload for the given recipient's ephemeral
    /// public xkey. The no-op implementation returns the payload unchanged.
    /// </summary>
    /// <param name="payload">The outgoing response payload to encrypt.</param>
    /// <param name="recipientXkeyPublic">The recipient's ephemeral public xkey.</param>
    /// <returns>The encrypted payload (or the input unchanged, when unconfigured).</returns>
    CalloutCryptoResult Seal(byte[] payload, string recipientXkeyPublic);
}
