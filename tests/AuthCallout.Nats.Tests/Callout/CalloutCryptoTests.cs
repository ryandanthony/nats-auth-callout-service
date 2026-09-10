// -----------------------------------------------------------------------
// <copyright file="CalloutCryptoTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text;
using AuthCallout.Nats.Callout;
using NATS.NKeys;

namespace AuthCallout.Nats.Tests.Callout;

/// <summary>
/// Unit tests for <see cref="CalloutCrypto"/> and <see cref="NoOpCalloutCrypto"/> (P07
/// ADR→test map: "xkey round-trip: sealed request → open → sealed reply verifiable" and
/// the no-op pass-through contract).
/// </summary>
public sealed class CalloutCryptoTests
{
    [Fact]
    public void RealCrypto_IsEnabled_IsTrue()
    {
        using var serverKp = KeyPair.CreatePair(PrefixByte.Curve);
        using var svcKp = KeyPair.CreatePair(PrefixByte.Curve);
        var crypto = new CalloutCrypto(svcKp.GetSeed());

        Assert.True(crypto.IsEnabled);
        crypto.Dispose();
        serverKp.Dispose();
    }

    [Fact]
    public void NoOpCrypto_IsEnabled_IsFalse()
    {
        var crypto = new NoOpCalloutCrypto();

        Assert.False(crypto.IsEnabled);
    }

    [Fact]
    public void RealCrypto_SealThenOpen_RoundTripsToOriginalPlaintext()
    {
        // Simulates the server's role: server has its own curve key pair; the service's
        // CalloutCrypto is the responder side.
        using var serverKp = KeyPair.CreatePair(PrefixByte.Curve);
        using var serviceKp = KeyPair.CreatePair(PrefixByte.Curve);
        using var crypto = new CalloutCrypto(serviceKp.GetSeed());
        var plaintext = Encoding.UTF8.GetBytes("nats-authorization-request payload");

        // "Server" seals a request to the service's public key.
        var sealedRequest = serverKp.Seal(plaintext, serviceKp.GetPublicKey());

        // Service opens it using its own crypto + the server's public key.
        var opened = crypto.Open(sealedRequest, serverKp.GetPublicKey());

        Assert.Equal(plaintext, opened.Payload);
    }

    [Fact]
    public void RealCrypto_ServiceSealedReply_ServerCanOpenIt()
    {
        using var serverKp = KeyPair.CreatePair(PrefixByte.Curve);
        using var serviceKp = KeyPair.CreatePair(PrefixByte.Curve);
        using var crypto = new CalloutCrypto(serviceKp.GetSeed());
        var replyPlaintext = Encoding.UTF8.GetBytes("signed authorization response");

        // Service seals its reply back to the server's public key.
        var sealedReply = crypto.Seal(replyPlaintext, serverKp.GetPublicKey());

        // "Server" opens it using its own key + the service's public key.
        var opened = serverKp.Open(sealedReply.Payload, serviceKp.GetPublicKey());

        Assert.Equal(replyPlaintext, opened);
    }

    [Fact]
    public void RealCrypto_FullRoundTrip_RequestOpenThenReplySeal_BothVerifiable()
    {
        using var serverKp = KeyPair.CreatePair(PrefixByte.Curve);
        using var serviceKp = KeyPair.CreatePair(PrefixByte.Curve);
        using var crypto = new CalloutCrypto(serviceKp.GetSeed());

        var requestPlaintext = Encoding.UTF8.GetBytes("request");
        var sealedRequest = serverKp.Seal(requestPlaintext, serviceKp.GetPublicKey());
        var openedRequest = crypto.Open(sealedRequest, serverKp.GetPublicKey());
        Assert.Equal(requestPlaintext, openedRequest.Payload);

        var replyPlaintext = Encoding.UTF8.GetBytes("reply");
        var sealedReply = crypto.Seal(replyPlaintext, serverKp.GetPublicKey());
        var openedReply = serverKp.Open(sealedReply.Payload, serviceKp.GetPublicKey());
        Assert.Equal(replyPlaintext, openedReply);
    }

    [Fact]
    public void NoOpCrypto_Open_ReturnsPayloadUnchanged()
    {
        var crypto = new NoOpCalloutCrypto();
        var payload = Encoding.UTF8.GetBytes("plaintext");

        var result = crypto.Open(payload, "irrelevant-public-key");

        Assert.Same(payload, result.Payload);
    }

    [Fact]
    public void NoOpCrypto_Seal_ReturnsPayloadUnchanged()
    {
        var crypto = new NoOpCalloutCrypto();
        var payload = Encoding.UTF8.GetBytes("plaintext");

        var result = crypto.Seal(payload, "irrelevant-public-key");

        Assert.Same(payload, result.Payload);
    }

    [Fact]
    public void RealCrypto_Constructor_NullOrWhitespaceSeed_Throws()
    {
        Assert.Throws<ArgumentException>(() => new CalloutCrypto(string.Empty));
    }

    [Fact]
    public void RealCrypto_Open_WrongSenderKey_Throws()
    {
        using var serverKp = KeyPair.CreatePair(PrefixByte.Curve);
        using var wrongKp = KeyPair.CreatePair(PrefixByte.Curve);
        using var serviceKp = KeyPair.CreatePair(PrefixByte.Curve);
        using var crypto = new CalloutCrypto(serviceKp.GetSeed());
        var plaintext = Encoding.UTF8.GetBytes("request");
        var sealedRequest = serverKp.Seal(plaintext, serviceKp.GetPublicKey());

        Assert.ThrowsAny<Exception>(() => crypto.Open(sealedRequest, wrongKp.GetPublicKey()));
    }
}
