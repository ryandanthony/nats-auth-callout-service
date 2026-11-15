// -----------------------------------------------------------------------
// <copyright file="AssertionVerifierTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Seams;
using AuthCallout.Identity.Assertions;
using NATS.NKeys;

namespace AuthCallout.Client.Tests.Identity;

/// <summary>
/// Tests for the new verify-only entry point, <see cref="AssertionVerifier"/> (ADR 0020's
/// consequence: a verifier should not need an active signing key).
/// </summary>
public sealed class AssertionVerifierTests
{
    [Fact]
    public void Verify_ValidSignature_ReturnsClaims()
    {
        using var keyPair = KeyPair.CreatePair(PrefixByte.Account);
        using var signer = new AssertionSigner([new ResolvedAssertionKey("kid-a", keyPair.GetSeed(), Active: true)]);
        var claims = new AssertionClaims("conn-1", "sub-1", "notes", "https://id.example.com", DateTimeOffset.UtcNow.AddMinutes(5));
        var signed = signer.Sign(claims);

        var verified = AssertionVerifier.Verify(signed.CompactJwt, keyPair.GetPublicKey());

        Assert.NotNull(verified);
        Assert.Equal("conn-1", verified!.Id);
        Assert.Equal("sub-1", verified.Subject);
    }

    [Fact]
    public void Verify_TamperedSignature_ReturnsNull()
    {
        using var keyPair = KeyPair.CreatePair(PrefixByte.Account);
        using var signer = new AssertionSigner([new ResolvedAssertionKey("kid-a", keyPair.GetSeed(), Active: true)]);
        var claims = new AssertionClaims("conn-1", "sub-1", "notes", "https://id.example.com", DateTimeOffset.UtcNow.AddMinutes(5));
        var signed = signer.Sign(claims);
        var tampered = signed.CompactJwt[..^1] + (signed.CompactJwt[^1] == 'A' ? 'B' : 'A');

        var verified = AssertionVerifier.Verify(tampered, keyPair.GetPublicKey());

        Assert.Null(verified);
    }

    [Fact]
    public void Verify_WrongPublicKey_ReturnsNull()
    {
        using var signingKey = KeyPair.CreatePair(PrefixByte.Account);
        using var otherKey = KeyPair.CreatePair(PrefixByte.Account);
        using var signer = new AssertionSigner([new ResolvedAssertionKey("kid-a", signingKey.GetSeed(), Active: true)]);
        var claims = new AssertionClaims("conn-1", "sub-1", "notes", "https://id.example.com", DateTimeOffset.UtcNow.AddMinutes(5));
        var signed = signer.Sign(claims);

        var verified = AssertionVerifier.Verify(signed.CompactJwt, otherKey.GetPublicKey());

        Assert.Null(verified);
    }

    [Fact]
    public void Verify_MalformedToken_ReturnsNull()
    {
        using var keyPair = KeyPair.CreatePair(PrefixByte.Account);

        var verified = AssertionVerifier.Verify("not.a.jwt", keyPair.GetPublicKey());

        Assert.Null(verified);
    }
}
