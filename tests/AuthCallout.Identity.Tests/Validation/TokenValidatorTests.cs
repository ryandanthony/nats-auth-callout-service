// -----------------------------------------------------------------------
// <copyright file="TokenValidatorTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using AuthCallout.Contracts.Decisions;
using AuthCallout.Contracts.Primitives;
using AuthCallout.Identity.Tests.Jwks;
using AuthCallout.Identity.Validation;
using Microsoft.IdentityModel.Tokens;

namespace AuthCallout.Identity.Tests.Validation;

/// <summary>
/// Table tests for <see cref="TokenValidator"/> against the P06 ADR→test
/// map (ADR 0016): algorithm confusion / rejection, expiry boundaries, audience handling,
/// embedded-key ignoring, unknown-kid single-flight refresh, and never-throws on malformed
/// input.
/// </summary>
public sealed class TokenValidatorTests : IDisposable
{
    private static readonly string[] HouseholdIds = ["H1", "H2"];

    private static readonly string[] NotesEditorRoles = ["notes-editor"];

    private readonly TokenValidatorTestHarness _harness = new();

    [Fact]
    public async Task ValidateAsync_ValidToken_ReturnsClaims()
    {
        var issuer = JwksTestBuilders.IssuerFor(_harness.Idp);
        await _harness.RegisterIssuerAsync(issuer);
        var token = _harness.Idp.IssueToken(claims: new Dictionary<string, object>(StringComparer.Ordinal) { ["sub"] = "user-1" });

        var outcome = await _harness.Validator.ValidateAsync(token, issuer, CancellationToken.None);

        Assert.True(outcome.IsValid);
        Assert.Equal("user-1", outcome.Claims!.GetSingleValue("sub"));
    }

    [Fact]
    public async Task ValidateAsync_ValidToken_PreservesNestedAndArrayClaims()
    {
        var issuer = JwksTestBuilders.IssuerFor(_harness.Idp);
        await _harness.RegisterIssuerAsync(issuer);
        var token = _harness.Idp.IssueToken(claims: new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["household_id"] = HouseholdIds,
        });

        var outcome = await _harness.Validator.ValidateAsync(token, issuer, CancellationToken.None);

        Assert.True(outcome.IsValid);
        Assert.Equal(["H1", "H2"], outcome.Claims!.GetValues("household_id"));
    }

    // Regression: string is IEnumerable<char>, so a claims-to-JSON converter that matches
    // IEnumerable before string writes every scalar string claim as a JSON array of
    // single-character strings ("notes-web" -> ["n","o",...]). That silently breaks the
    // authorization `match` on azp / nested realm_access.roles and denies every request,
    // even though the token itself validates. This asserts scalar strings (top-level and
    // nested-inside-an-object) survive validation intact.
    [Fact]
    public async Task ValidateAsync_ValidToken_PreservesScalarStringClaims()
    {
        var issuer = JwksTestBuilders.IssuerFor(_harness.Idp);
        await _harness.RegisterIssuerAsync(issuer);
        var token = _harness.Idp.IssueToken(claims: new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["azp"] = "notes-web",
            ["preferred_username"] = "alice",
            ["realm_access"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["roles"] = NotesEditorRoles,
            },
        });

        var outcome = await _harness.Validator.ValidateAsync(token, issuer, CancellationToken.None);

        Assert.True(outcome.IsValid);
        Assert.Equal("notes-web", outcome.Claims!.GetSingleValue("azp"));
        Assert.Equal("alice", outcome.Claims!.GetSingleValue("preferred_username"));
        Assert.Equal(["notes-editor"], outcome.Claims!.GetValues("realm_access.roles"));
    }

    [Fact]
    public async Task ValidateAsync_ExpiredToken_DeniesExpired()
    {
        var issuer = JwksTestBuilders.IssuerFor(_harness.Idp);
        await _harness.RegisterIssuerAsync(issuer);
        var token = _harness.Idp.IssueToken(
            notBefore: DateTimeOffset.UtcNow.AddMinutes(-10), expires: DateTimeOffset.UtcNow.AddMinutes(-5));

        var outcome = await _harness.Validator.ValidateAsync(token, issuer, CancellationToken.None);

        Assert.False(outcome.IsValid);
        Assert.Equal(DenyReason.Expired, outcome.Reason);
    }

    [Fact]
    public async Task ValidateAsync_ExpiredWithinClockSkew_IsValid()
    {
        var issuer = JwksTestBuilders.IssuerFor(_harness.Idp, clockSkew: DurationString.Parse("60s"));
        await _harness.RegisterIssuerAsync(issuer);
        // Expired 30s ago, within the 60s clock skew: boundary test per ADR 0016.
        var token = _harness.Idp.IssueToken(
            notBefore: DateTimeOffset.UtcNow.AddMinutes(-5), expires: DateTimeOffset.UtcNow.AddSeconds(-30));

        var outcome = await _harness.Validator.ValidateAsync(token, issuer, CancellationToken.None);

        Assert.True(outcome.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_ExpiredBeyondClockSkew_DeniesExpired()
    {
        var issuer = JwksTestBuilders.IssuerFor(_harness.Idp, clockSkew: DurationString.Parse("60s"));
        await _harness.RegisterIssuerAsync(issuer);
        // Expired 90s ago, beyond the 60s clock skew.
        var token = _harness.Idp.IssueToken(
            notBefore: DateTimeOffset.UtcNow.AddMinutes(-5), expires: DateTimeOffset.UtcNow.AddSeconds(-90));

        var outcome = await _harness.Validator.ValidateAsync(token, issuer, CancellationToken.None);

        Assert.False(outcome.IsValid);
        Assert.Equal(DenyReason.Expired, outcome.Reason);
    }

    [Fact]
    public async Task ValidateAsync_NotYetValidWithinClockSkew_IsValid()
    {
        var issuer = JwksTestBuilders.IssuerFor(_harness.Idp, clockSkew: DurationString.Parse("60s"));
        await _harness.RegisterIssuerAsync(issuer);
        // nbf 30s in the future, within the 60s clock skew.
        var token = _harness.Idp.IssueToken(
            notBefore: DateTimeOffset.UtcNow.AddSeconds(30), expires: DateTimeOffset.UtcNow.AddMinutes(5));

        var outcome = await _harness.Validator.ValidateAsync(token, issuer, CancellationToken.None);

        Assert.True(outcome.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_NotYetValidBeyondClockSkew_DeniesExpired()
    {
        var issuer = JwksTestBuilders.IssuerFor(_harness.Idp, clockSkew: DurationString.Parse("60s"));
        await _harness.RegisterIssuerAsync(issuer);
        // nbf 90s in the future, beyond the 60s clock skew.
        var token = _harness.Idp.IssueToken(
            notBefore: DateTimeOffset.UtcNow.AddSeconds(90), expires: DateTimeOffset.UtcNow.AddMinutes(5));

        var outcome = await _harness.Validator.ValidateAsync(token, issuer, CancellationToken.None);

        Assert.False(outcome.IsValid);
        Assert.Equal(DenyReason.Expired, outcome.Reason);
    }

    [Fact]
    public async Task ValidateAsync_WrongIssuer_DeniesSignatureInvalid()
    {
        var issuer = JwksTestBuilders.IssuerFor(_harness.Idp);
        await _harness.RegisterIssuerAsync(issuer);
        // Craft a token whose iss doesn't match the issuer we're validating against, by
        // registering a different issuer config pointing at the same authority string but
        // asking the IdP to sign as if it were a different issuer is out of scope for the stub;
        // instead validate against a mismatched IssuerConfig.Authority directly.
        var token = _harness.Idp.IssueToken();
        var mismatchedIssuer = issuer with { Authority = "https://not-the-real-issuer.example.com" };

        var outcome = await _harness.Validator.ValidateAsync(token, mismatchedIssuer, CancellationToken.None);

        Assert.False(outcome.IsValid);
    }

    [Theory]
    [InlineData(true)] // validateAudience on, matching audience
    public async Task ValidateAsync_AudienceValidationOn_MatchingAudience_IsValid(bool validateAudience)
    {
        var issuer = JwksTestBuilders.IssuerFor(_harness.Idp, validateAudience: validateAudience, audience: ["expected-aud"]);
        await _harness.RegisterIssuerAsync(issuer);
        var token = _harness.Idp.IssueToken(audience: "expected-aud");

        var outcome = await _harness.Validator.ValidateAsync(token, issuer, CancellationToken.None);

        Assert.True(outcome.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_AudienceValidationOn_MismatchedAudience_DeniesAudienceMismatch()
    {
        var issuer = JwksTestBuilders.IssuerFor(_harness.Idp, validateAudience: true, audience: ["expected-aud"]);
        await _harness.RegisterIssuerAsync(issuer);
        var token = _harness.Idp.IssueToken(audience: "wrong-aud");

        var outcome = await _harness.Validator.ValidateAsync(token, issuer, CancellationToken.None);

        Assert.False(outcome.IsValid);
        Assert.Equal(DenyReason.AudienceMismatch, outcome.Reason);
    }

    [Fact]
    public async Task ValidateAsync_AudienceValidationOn_MultipleAcceptedAudiences_AnyMatchIsValid()
    {
        var issuer = JwksTestBuilders.IssuerFor(_harness.Idp, validateAudience: true, audience: ["aud-a", "aud-b"]);
        await _harness.RegisterIssuerAsync(issuer);
        var token = _harness.Idp.IssueToken(audience: "aud-b");

        var outcome = await _harness.Validator.ValidateAsync(token, issuer, CancellationToken.None);

        Assert.True(outcome.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_AudienceValidationOff_NoAudienceClaim_IsStillValid()
    {
        var issuer = JwksTestBuilders.IssuerFor(_harness.Idp, validateAudience: false);
        await _harness.RegisterIssuerAsync(issuer);
        var token = _harness.Idp.IssueToken();

        var outcome = await _harness.Validator.ValidateAsync(token, issuer, CancellationToken.None);

        Assert.True(outcome.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_AlgorithmConfusionAttack_DeniesAlgorithmRejected()
    {
        // ADR 0016 §Why HS* is excluded: forged HS256 token whose kid matches a real,
        // published RSA key, using that key's public bytes as the (attacker-known) HMAC secret.
        var issuer = JwksTestBuilders.IssuerFor(_harness.Idp);
        await _harness.RegisterIssuerAsync(issuer);
        var token = _harness.Idp.IssueAlgorithmConfusionToken();

        var outcome = await _harness.Validator.ValidateAsync(token, issuer, CancellationToken.None);

        Assert.False(outcome.IsValid);
        Assert.Equal(DenyReason.AlgorithmRejected, outcome.Reason);
    }

    [Fact]
    public async Task ValidateAsync_AlgorithmNotInAllowList_DeniesAlgorithmRejected()
    {
        // Legitimately-signed RS256 token, but the issuer's allow-list only accepts ES256.
        var issuer = JwksTestBuilders.IssuerFor(_harness.Idp, allowedAlgorithms: ["ES256"]);
        await _harness.RegisterIssuerAsync(issuer);
        var token = _harness.Idp.IssueToken();

        var outcome = await _harness.Validator.ValidateAsync(token, issuer, CancellationToken.None);

        Assert.False(outcome.IsValid);
        Assert.Equal(DenyReason.AlgorithmRejected, outcome.Reason);
    }

    [Fact]
    public async Task ValidateAsync_UnknownKid_TriggersSingleFlightRefresh_ThenSucceeds()
    {
        // doc 08: an unknown kid triggers a single-flight JWKS refresh; here the "unknown" kid
        // becomes known between issuance and validation because the IdP rotated after the
        // validator's last (pre-registration) view — simulated by signing with a key added
        // to the IdP only after the initial prewarm captured just the original key.
        var issuer = JwksTestBuilders.IssuerFor(_harness.Idp);
        await _harness.RegisterIssuerAsync(issuer);
        _harness.Idp.AddKey("kid-rotated");
        var token = _harness.Idp.IssueToken(signingKid: "kid-rotated");

        var outcome = await _harness.Validator.ValidateAsync(token, issuer, CancellationToken.None);

        Assert.True(outcome.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_UnknownKid_StaysUnknownAfterRefresh_DeniesSignatureInvalid()
    {
        // The signing key was never published at all (e.g. a forged/foreign key): the
        // single-flight retry re-fetches genuinely current JWKS and still can't find it.
        var issuer = JwksTestBuilders.IssuerFor(_harness.Idp);
        await _harness.RegisterIssuerAsync(issuer);
        using var foreignRsa = RSA.Create(2048);
        var foreignKey = new RsaSecurityKey(foreignRsa) { KeyId = "kid-never-published" };
        var credentials = new SigningCredentials(foreignKey, SecurityAlgorithms.RsaSha256);
        var payload = new JwtPayload(
            issuer: _harness.Idp.Authority,
            audience: null,
            claims: [],
            notBefore: DateTimeOffset.UtcNow.AddMinutes(-1).UtcDateTime,
            expires: DateTimeOffset.UtcNow.AddMinutes(5).UtcDateTime);
        var jwt = new JwtSecurityToken(new JwtHeader(credentials), payload);
        var token = new JwtSecurityTokenHandler().WriteToken(jwt);

        var outcome = await _harness.Validator.ValidateAsync(token, issuer, CancellationToken.None);

        Assert.False(outcome.IsValid);
        Assert.Equal(DenyReason.SignatureInvalid, outcome.Reason);
    }

    [Fact]
    public async Task ValidateAsync_NoJwksCachedAtAll_TriggersRefreshBeforeGivingUp()
    {
        // Issuer registered but never prewarmed: the validator's own "no keys yet" path must
        // trigger a refresh rather than immediately denying.
        var issuer = JwksTestBuilders.IssuerFor(_harness.Idp);
        _harness.RegisterIssuerWithoutPrewarm(issuer);
        var token = _harness.Idp.IssueToken();

        var outcome = await _harness.Validator.ValidateAsync(token, issuer, CancellationToken.None);

        Assert.True(outcome.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_MalformedToken_DeniesSignatureInvalid_NeverThrows()
    {
        var issuer = JwksTestBuilders.IssuerFor(_harness.Idp);
        await _harness.RegisterIssuerAsync(issuer);

        var outcome = await _harness.Validator.ValidateAsync("not-a-jwt-at-all", issuer, CancellationToken.None);

        Assert.False(outcome.IsValid);
        Assert.Equal(DenyReason.SignatureInvalid, outcome.Reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("...")]
    [InlineData("a.b")]
    [InlineData("a.b.c.d.e")]
    [InlineData("{}")]
    [InlineData("null")]
    public async Task ValidateAsync_VariousMalformedInputs_NeverThrows_AlwaysDenies(string malformed)
    {
        var issuer = JwksTestBuilders.IssuerFor(_harness.Idp);
        await _harness.RegisterIssuerAsync(issuer);

        var outcome = await _harness.Validator.ValidateAsync(malformed, issuer, CancellationToken.None);

        Assert.False(outcome.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_NullRawToken_Throws()
    {
        var issuer = JwksTestBuilders.IssuerFor(_harness.Idp);

        await Assert.ThrowsAsync<ArgumentNullException>(() => _harness.Validator.ValidateAsync(null!, issuer, CancellationToken.None));
    }

    [Fact]
    public async Task ValidateAsync_NullIssuer_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _harness.Validator.ValidateAsync("token", null!, CancellationToken.None));
    }

    [Fact]
    public void Constructor_NullKeyProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new TokenValidator(null!, _harness.JwksManager));
    }

    [Fact]
    public void Constructor_NullJwksCacheManager_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new TokenValidator(_harness.JwksManager, null!));
    }

    /// <inheritdoc />
    public void Dispose() => _harness.Dispose();
}
