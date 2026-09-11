// -----------------------------------------------------------------------
// <copyright file="UserJwtMinterTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Decisions;
using AuthCallout.Nats.Callout;
using NATS.Jwt;
using NATS.Jwt.Models;
using NATS.NKeys;

namespace AuthCallout.Nats.Tests.Callout;

/// <summary>
/// Unit tests for <see cref="UserJwtMinter"/> (P07 ADR→test map): the minted JWT contains
/// exactly the D18 fields and the correct signer, the response subject/audience binding (doc
/// 03), and that a deny response carries no internal detail.
/// </summary>
public sealed class UserJwtMinterTests : IDisposable
{
    private readonly KeyPair _issuerKeyPair;
    private readonly string _issuerSeed;
    private readonly UserJwtMinter _minter;

    public UserJwtMinterTests()
    {
        _issuerKeyPair = KeyPair.CreatePair(PrefixByte.Account);
        _issuerSeed = _issuerKeyPair.GetSeed();
        _minter = new UserJwtMinter(_issuerSeed);
    }

    public void Dispose()
    {
        _minter.Dispose();
        _issuerKeyPair.Dispose();
    }

    [Fact]
    public void MintGrant_ResponseSubjectIsUserNKey_AudienceIsServerId()
    {
        var grant = CalloutTestBuilders.Granted();
        var userNKey = CalloutTestBuilders.NewUserPublicKey();

        var response = _minter.MintGrant(grant, userNKey, "server-42");

        var decoded = NatsJwt.DecodeClaims<NatsAuthorizationResponseClaims>(response.EncodedResponseJwt);
        Assert.Equal(userNKey, decoded.Subject);
        Assert.Equal("server-42", decoded.Audience);
    }

    [Fact]
    public void MintGrant_ResponseSignedByIssuerSeed()
    {
        var grant = CalloutTestBuilders.Granted();
        var userNKey = CalloutTestBuilders.NewUserPublicKey();

        var response = _minter.MintGrant(grant, userNKey, "server-1");

        var decoded = NatsJwt.DecodeClaims<NatsAuthorizationResponseClaims>(response.EncodedResponseJwt);
        Assert.Equal(_issuerKeyPair.GetPublicKey(), decoded.Issuer);
    }

    [Fact]
    public void MintGrant_NestedUserJwt_CarriesExactlyD18Fields()
    {
        var grant = CalloutTestBuilders.Granted() with
        {
            TargetAccount = "NOTES",
            Name = "user@example.com",
            Expiry = new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero),
            Publish = ["notes.>", "notes.audit.>"],
            Subscribe = ["notes.inbox.>"],
        };
        var userNKey = CalloutTestBuilders.NewUserPublicKey();

        var response = _minter.MintGrant(grant, userNKey, "server-1");
        var outer = NatsJwt.DecodeClaims<NatsAuthorizationResponseClaims>(response.EncodedResponseJwt);
        var userClaims = NatsJwt.DecodeClaims<NatsUserClaims>(outer.AuthorizationResponse.Jwt);

        Assert.Equal("NOTES", userClaims.Audience);
        Assert.Equal("user@example.com", userClaims.Name);
        Assert.Equal(grant.Expiry, userClaims.Expires);
        Assert.Equal(["notes.>", "notes.audit.>"], userClaims.User.Pub.Allow);
        Assert.Equal(["notes.inbox.>"], userClaims.User.Sub.Allow);
        // Config-mode auth_callout rejects a user JWT that names an issuer_account; the target
        // account travels via Audience (asserted above), so IssuerAccount must stay unset (D18).
        Assert.True(string.IsNullOrEmpty(userClaims.User.IssuerAccount));
    }

    [Fact]
    public void MintGrant_NestedUserJwt_CarriesNoExtraPermissionFields()
    {
        // D18: "no bearer flag, connection-type limits, or response permissions" — assert
        // the D18-forbidden fields stay at their unset defaults.
        var grant = CalloutTestBuilders.Granted();
        var userNKey = CalloutTestBuilders.NewUserPublicKey();

        var response = _minter.MintGrant(grant, userNKey, "server-1");
        var outer = NatsJwt.DecodeClaims<NatsAuthorizationResponseClaims>(response.EncodedResponseJwt);
        var userClaims = NatsJwt.DecodeClaims<NatsUserClaims>(outer.AuthorizationResponse.Jwt);

        Assert.False(userClaims.User.BearerToken);
        Assert.Null(userClaims.User.Resp);
        Assert.True(userClaims.User.AllowedConnectionTypes is null || userClaims.User.AllowedConnectionTypes.Count == 0);
    }

    [Fact]
    public void MintGrant_NestedUserJwt_SignedByIssuerSeed()
    {
        var grant = CalloutTestBuilders.Granted();
        var userNKey = CalloutTestBuilders.NewUserPublicKey();

        var response = _minter.MintGrant(grant, userNKey, "server-1");
        var outer = NatsJwt.DecodeClaims<NatsAuthorizationResponseClaims>(response.EncodedResponseJwt);
        var userClaims = NatsJwt.DecodeClaims<NatsUserClaims>(outer.AuthorizationResponse.Jwt);

        Assert.Equal(_issuerKeyPair.GetPublicKey(), userClaims.Issuer);
    }

    [Fact]
    public void MintDenial_ResponseSubjectIsUserNKey_AudienceIsServerId()
    {
        var denial = CalloutTestBuilders.Denied();
        var userNKey = CalloutTestBuilders.NewUserPublicKey();

        var response = _minter.MintDenial(denial, userNKey, "server-99");

        var decoded = NatsJwt.DecodeClaims<NatsAuthorizationResponseClaims>(response.EncodedResponseJwt);
        Assert.Equal(userNKey, decoded.Subject);
        Assert.Equal("server-99", decoded.Audience);
    }

    [Fact]
    public void MintDenial_CarriesOnlyGenericError_NoJwt()
    {
        var denial = CalloutTestBuilders.Denied();
        var userNKey = CalloutTestBuilders.NewUserPublicKey();

        var response = _minter.MintDenial(denial, userNKey, "server-1");

        var decoded = NatsJwt.DecodeClaims<NatsAuthorizationResponseClaims>(response.EncodedResponseJwt);
        Assert.Equal(UserJwtMinter.GenericDenialError, decoded.AuthorizationResponse.Error);
        Assert.True(string.IsNullOrEmpty(decoded.AuthorizationResponse.Jwt));
    }

    [Theory]
    [InlineData(DenyReason.NoAppMatch, "internal: no app matched the token's issuer candidates")]
    [InlineData(DenyReason.NoProfileMatch, "internal: profile claim mismatch on claim 'roles'")]
    [InlineData(DenyReason.RequiredVariableMissing, "internal: required variable 'team' had no value")]
    public void MintDenial_NeverLeaksReasonOrDetail_IntoTheResponse(DenyReason reason, string detail)
    {
        var denial = CalloutTestBuilders.Denied(reason: reason, detail: detail);
        var userNKey = CalloutTestBuilders.NewUserPublicKey();

        var response = _minter.MintDenial(denial, userNKey, "server-1");

        Assert.DoesNotContain(reason.ToString(), response.EncodedResponseJwt, StringComparison.Ordinal);
        Assert.DoesNotContain("internal:", response.EncodedResponseJwt, StringComparison.Ordinal);
        Assert.DoesNotContain(detail, response.EncodedResponseJwt, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_NullOrWhitespaceSeed_Throws()
    {
        Assert.Throws<ArgumentException>(() => new UserJwtMinter(string.Empty));
    }

    [Fact]
    public void MintGrant_NullGrant_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _minter.MintGrant(null!, "U123", "server-1"));
    }

    [Fact]
    public void MintGrant_NullOrEmptyUserNKey_Throws()
    {
        var grant = CalloutTestBuilders.Granted();
        Assert.Throws<ArgumentException>(() => _minter.MintGrant(grant, string.Empty, "server-1"));
    }
}
