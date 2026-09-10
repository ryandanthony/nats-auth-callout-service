// -----------------------------------------------------------------------
// <copyright file="NoOpCalloutCrypto.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Seams;

namespace AuthCallout.Nats.Callout;

/// <summary>
/// The pass-through <see cref="ICalloutCrypto"/> implementation used when <c>xkeySeed</c> is
/// not configured (ADR 0018): every <see cref="Open"/>/<see cref="Seal"/> call returns its
/// input unchanged. It is the responder's (<see cref="CalloutResponder"/>) job to detect and
/// reject an encrypted request arriving while this no-op is in effect (fail closed) — this
/// type never inspects its input to guess whether it "looks encrypted".
/// </summary>
public sealed class NoOpCalloutCrypto : ICalloutCrypto
{
    /// <inheritdoc />
    public bool IsEnabled => false;

    /// <inheritdoc />
    public CalloutCryptoResult Open(byte[] encryptedPayload, string senderXkeyPublic)
    {
        ArgumentNullException.ThrowIfNull(encryptedPayload);
        return new CalloutCryptoResult(encryptedPayload);
    }

    /// <inheritdoc />
    public CalloutCryptoResult Seal(byte[] payload, string recipientXkeyPublic)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return new CalloutCryptoResult(payload);
    }
}
