// -----------------------------------------------------------------------
// <copyright file="CalloutCrypto.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Seams;
using NATS.NKeys;

namespace AuthCallout.Nats.Callout;

/// <summary>
/// The real <see cref="ICalloutCrypto"/> implementation: Curve25519 (NKey <c>Curve</c>
/// prefix) seal/open via <see cref="NATS.NKeys.KeyPair"/>, matching the NATS server's own
/// xkey construction (ADR 0018, D14). Used when <c>xkeySeed</c> is configured; see
/// <see cref="NoOpCalloutCrypto"/> for the unconfigured case.
/// </summary>
public sealed class CalloutCrypto : ICalloutCrypto, IDisposable
{
    private readonly KeyPair _keyPair;

    /// <summary>Initializes a new instance of the <see cref="CalloutCrypto"/> class.</summary>
    /// <param name="xkeySeed">The resolved Curve NKey seed (ADR 0018 <c>xkeySeed</c>).</param>
    public CalloutCrypto(string xkeySeed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xkeySeed);
        _keyPair = KeyPair.FromSeed(xkeySeed);
    }

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public CalloutCryptoResult Open(byte[] encryptedPayload, string senderXkeyPublic)
    {
        ArgumentNullException.ThrowIfNull(encryptedPayload);
        ArgumentException.ThrowIfNullOrWhiteSpace(senderXkeyPublic);
        return new CalloutCryptoResult(_keyPair.Open(encryptedPayload, senderXkeyPublic));
    }

    /// <inheritdoc />
    public CalloutCryptoResult Seal(byte[] payload, string recipientXkeyPublic)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientXkeyPublic);
        return new CalloutCryptoResult(_keyPair.Seal(payload, recipientXkeyPublic));
    }

    /// <summary>Disposes the underlying curve key pair.</summary>
    public void Dispose() => _keyPair.Dispose();
}
