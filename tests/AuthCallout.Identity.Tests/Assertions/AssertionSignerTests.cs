// -----------------------------------------------------------------------
// <copyright file="AssertionSignerTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Seams;
using AuthCallout.Identity.Assertions;
using NATS.NKeys;

namespace AuthCallout.Identity.Tests.Assertions;

/// <summary>
/// Unit tests for <see cref="AssertionSigner"/> (P08 ADR->test map): claims/kid shape per ADR
/// 0011, round-trip verification (valid + tampered), keyset construction invariants (ADR
/// 0014), and that the signer never leaks a private key through its public surface.
/// </summary>
public sealed class AssertionSignerTests
{
    [Fact]
    public void Sign_ProducesThreeSegmentCompactJwt_WithActiveKid()
    {
        using var keyPair = KeyPair.CreatePair(PrefixByte.Account);
        var keyset = new[] { new ResolvedAssertionKey("kid-a", keyPair.GetSeed(), Active: true) };
        using var signer = new AssertionSigner(keyset);

        var claims = new AssertionClaims("conn-1", "sub-1", "notes", "https://id.example.com", DateTimeOffset.UtcNow.AddMinutes(5));
        var signed = signer.Sign(claims);

        Assert.Equal("kid-a", signed.Kid);
        Assert.Equal(3, signed.CompactJwt.Split('.').Length);
    }

    [Fact]
    public void Sign_ThenVerify_WithPublicKey_RoundTripsClaims()
    {
        using var keyPair = KeyPair.CreatePair(PrefixByte.Account);
        var keyset = new[] { new ResolvedAssertionKey("kid-a", keyPair.GetSeed(), Active: true) };
        using var signer = new AssertionSigner(keyset);

        var expiry = DateTimeOffset.UtcNow.AddMinutes(5);
        var claims = new AssertionClaims("conn-1", "sub-1", "notes", "https://id.example.com", expiry);
        var signed = signer.Sign(claims);

        var verified = signer.Verify(signed.CompactJwt, keyPair.GetPublicKey());

        Assert.NotNull(verified);
        Assert.Equal(claims.Id, verified!.Id);
        Assert.Equal(claims.Subject, verified.Subject);
        Assert.Equal(claims.Application, verified.Application);
        Assert.Equal(claims.Issuer, verified.Issuer);
        Assert.Equal(expiry.ToUnixTimeSeconds(), verified.Expiry.ToUnixTimeSeconds());
    }

    [Fact]
    public void Verify_TamperedPayload_ReturnsNull()
    {
        using var keyPair = KeyPair.CreatePair(PrefixByte.Account);
        var keyset = new[] { new ResolvedAssertionKey("kid-a", keyPair.GetSeed(), Active: true) };
        using var signer = new AssertionSigner(keyset);

        var claims = new AssertionClaims("conn-1", "sub-1", "notes", "https://id.example.com", DateTimeOffset.UtcNow.AddMinutes(5));
        var signed = signer.Sign(claims);
        var parts = signed.CompactJwt.Split('.');
        var tampered = $"{parts[0]}.{parts[1]}X.{parts[2]}";

        var verified = signer.Verify(tampered, keyPair.GetPublicKey());

        Assert.Null(verified);
    }

    [Fact]
    public void Verify_WrongPublicKey_ReturnsNull()
    {
        using var keyPair = KeyPair.CreatePair(PrefixByte.Account);
        using var wrongKeyPair = KeyPair.CreatePair(PrefixByte.Account);
        var keyset = new[] { new ResolvedAssertionKey("kid-a", keyPair.GetSeed(), Active: true) };
        using var signer = new AssertionSigner(keyset);

        var claims = new AssertionClaims("conn-1", "sub-1", "notes", "https://id.example.com", DateTimeOffset.UtcNow.AddMinutes(5));
        var signed = signer.Sign(claims);

        var verified = signer.Verify(signed.CompactJwt, wrongKeyPair.GetPublicKey());

        Assert.Null(verified);
    }

    [Fact]
    public void Verify_MalformedCompactJwt_ReturnsNullRatherThanThrowing()
    {
        using var keyPair = KeyPair.CreatePair(PrefixByte.Account);
        var keyset = new[] { new ResolvedAssertionKey("kid-a", keyPair.GetSeed(), Active: true) };
        using var signer = new AssertionSigner(keyset);

        Assert.Null(signer.Verify("not-a-jwt", keyPair.GetPublicKey()));
        Assert.Null(signer.Verify("a.b", keyPair.GetPublicKey()));
        Assert.Null(signer.Verify("a.b.c.d", keyPair.GetPublicKey()));
    }

    [Fact]
    public void RotatedKeyset_NonActiveKeyStillVerifies()
    {
        // ADR 0014: non-active keys are kept for verification (overlap window).
        using var oldKeyPair = KeyPair.CreatePair(PrefixByte.Account);
        using var newKeyPair = KeyPair.CreatePair(PrefixByte.Account);
        var keyset = new[]
        {
            new ResolvedAssertionKey("kid-old", oldKeyPair.GetSeed(), Active: false),
            new ResolvedAssertionKey("kid-new", newKeyPair.GetSeed(), Active: true),
        };

        using var oldSigner = new AssertionSigner([new ResolvedAssertionKey("kid-old", oldKeyPair.GetSeed(), Active: true)]);
        var claims = new AssertionClaims("conn-1", "sub-1", "notes", "https://id.example.com", DateTimeOffset.UtcNow.AddMinutes(5));
        var signedByOld = oldSigner.Sign(claims);

        using var currentSigner = new AssertionSigner(keyset);
        var verified = currentSigner.Verify(signedByOld.CompactJwt, oldKeyPair.GetPublicKey());

        Assert.NotNull(verified);
        Assert.Equal("kid-old", signedByOld.Kid);
    }

    [Fact]
    public void NewSignsWithActiveKid_EvenWhenNonActiveKeysPresent()
    {
        using var oldKeyPair = KeyPair.CreatePair(PrefixByte.Account);
        using var newKeyPair = KeyPair.CreatePair(PrefixByte.Account);
        var keyset = new[]
        {
            new ResolvedAssertionKey("kid-old", oldKeyPair.GetSeed(), Active: false),
            new ResolvedAssertionKey("kid-new", newKeyPair.GetSeed(), Active: true),
        };
        using var signer = new AssertionSigner(keyset);

        var claims = new AssertionClaims("conn-1", "sub-1", "notes", "https://id.example.com", DateTimeOffset.UtcNow.AddMinutes(5));
        var signed = signer.Sign(claims);

        Assert.Equal("kid-new", signed.Kid);
    }

    [Fact]
    public void Constructor_EmptyKeyset_Throws()
    {
        Assert.Throws<ArgumentException>(() => new AssertionSigner([]));
    }

    [Fact]
    public void Constructor_NoActiveKey_Throws()
    {
        using var keyPair = KeyPair.CreatePair(PrefixByte.Account);
        var keyset = new[] { new ResolvedAssertionKey("kid-a", keyPair.GetSeed(), Active: false) };

        Assert.Throws<ArgumentException>(() => new AssertionSigner(keyset));
    }

    [Fact]
    public void Constructor_MultipleActiveKeys_Throws()
    {
        using var keyPair1 = KeyPair.CreatePair(PrefixByte.Account);
        using var keyPair2 = KeyPair.CreatePair(PrefixByte.Account);
        var keyset = new[]
        {
            new ResolvedAssertionKey("kid-a", keyPair1.GetSeed(), Active: true),
            new ResolvedAssertionKey("kid-b", keyPair2.GetSeed(), Active: true),
        };

        Assert.Throws<ArgumentException>(() => new AssertionSigner(keyset));
    }

    [Fact]
    public void Constructor_DuplicateKids_Throws()
    {
        using var keyPair1 = KeyPair.CreatePair(PrefixByte.Account);
        using var keyPair2 = KeyPair.CreatePair(PrefixByte.Account);
        var keyset = new[]
        {
            new ResolvedAssertionKey("kid-a", keyPair1.GetSeed(), Active: true),
            new ResolvedAssertionKey("kid-a", keyPair2.GetSeed(), Active: false),
        };

        Assert.Throws<ArgumentException>(() => new AssertionSigner(keyset));
    }

    [Fact]
    public void SignedAssertion_NeverContainsThePrivateSeed()
    {
        // ADR 0011: "It carries no raw OIDC token and no secrets." Defense-in-depth check that
        // the compact JWT text never contains the signing seed material.
        using var keyPair = KeyPair.CreatePair(PrefixByte.Account);
        var seed = keyPair.GetSeed();
        var keyset = new[] { new ResolvedAssertionKey("kid-a", seed, Active: true) };
        using var signer = new AssertionSigner(keyset);

        var claims = new AssertionClaims("conn-1", "sub-1", "notes", "https://id.example.com", DateTimeOffset.UtcNow.AddMinutes(5));
        var signed = signer.Sign(claims);

        Assert.DoesNotContain(seed, signed.CompactJwt, StringComparison.Ordinal);
    }
}
