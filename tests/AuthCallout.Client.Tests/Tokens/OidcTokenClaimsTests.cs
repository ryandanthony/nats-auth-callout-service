// -----------------------------------------------------------------------
// <copyright file="OidcTokenClaimsTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Tokens;

namespace AuthCallout.Client.Tests.Tokens;

/// <summary>Tests for <see cref="OidcTokenClaims"/>'s unvalidated claim reads.</summary>
public sealed class OidcTokenClaimsTests
{
    [Fact]
    public void ReadSub_PresentClaim_ReturnsValue()
    {
        var jwt = TestJwt.Create(new Dictionary<string, object?>(StringComparer.Ordinal) { ["sub"] = "user-1" });

        Assert.Equal("user-1", OidcTokenClaims.ReadSub(jwt));
    }

    [Fact]
    public void ReadSub_AbsentClaim_ReturnsNull()
    {
        var jwt = TestJwt.Create(new Dictionary<string, object?>(StringComparer.Ordinal) { ["azp"] = "svc" });

        Assert.Null(OidcTokenClaims.ReadSub(jwt));
    }

    [Fact]
    public void ReadAzp_PresentClaim_ReturnsValue()
    {
        var jwt = TestJwt.Create(new Dictionary<string, object?>(StringComparer.Ordinal) { ["azp"] = "notes-worker" });

        Assert.Equal("notes-worker", OidcTokenClaims.ReadAzp(jwt));
    }

    [Fact]
    public void ReadJti_PresentClaim_ReturnsValue()
    {
        var jwt = TestJwt.Create(new Dictionary<string, object?>(StringComparer.Ordinal) { ["jti"] = "abc-123" });

        Assert.Equal("abc-123", OidcTokenClaims.ReadJti(jwt));
    }

    [Fact]
    public void ReadExpiry_PresentClaim_ReturnsInstant()
    {
        var exp = DateTimeOffset.UtcNow.AddHours(1);
        var jwt = TestJwt.Create(new Dictionary<string, object?>(StringComparer.Ordinal) { ["exp"] = exp.ToUnixTimeSeconds() });

        var result = OidcTokenClaims.ReadExpiry(jwt);

        Assert.NotNull(result);
        Assert.Equal(exp.ToUnixTimeSeconds(), result!.Value.ToUnixTimeSeconds());
    }

    [Fact]
    public void ReadExpiry_AbsentClaim_ReturnsNull()
    {
        var jwt = TestJwt.Create(new Dictionary<string, object?>(StringComparer.Ordinal) { ["sub"] = "user-1" });

        Assert.Null(OidcTokenClaims.ReadExpiry(jwt));
    }

    [Fact]
    public void ReadJwtHeaderKid_PresentHeader_ReturnsValue()
    {
        var jwt = TestJwt.Create(new Dictionary<string, object?>(StringComparer.Ordinal) { ["sub"] = "user-1" }, kid: "key-1");

        Assert.Equal("key-1", OidcTokenClaims.ReadJwtHeaderKid(jwt));
    }

    [Fact]
    public void ReadJwtHeaderKid_AbsentHeader_ReturnsNull()
    {
        var jwt = TestJwt.Create(new Dictionary<string, object?>(StringComparer.Ordinal) { ["sub"] = "user-1" });

        Assert.Null(OidcTokenClaims.ReadJwtHeaderKid(jwt));
    }

    [Fact]
    public void ReadClaim_MalformedToken_ReturnsNull()
    {
        Assert.Null(OidcTokenClaims.ReadClaim("not-a-jwt", "sub"));
    }
}
