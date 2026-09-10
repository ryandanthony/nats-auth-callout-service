// -----------------------------------------------------------------------
// <copyright file="AuthOutcomePayloadBuilderTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Decisions;
using AuthCallout.Nats.Events;

namespace AuthCallout.Nats.Tests.Events;

/// <summary>
/// Unit tests for <see cref="AuthOutcomePayloadBuilder"/> (P09 ADR→test map): every
/// outcome/reason combination, the anonymous success case, both <c>grant_construction_failed</c>
/// sub-reasons, and that no payload ever carries token material.
/// </summary>
public sealed class AuthOutcomePayloadBuilderTests
{
    private static readonly DateTimeOffset FixedTimestamp = new(2026, 9, 6, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_AnonymousGrant_OutcomeSuccessReasonAnonymous()
    {
        var decision = new GrantedDecision(
            Application: null,
            Principal: null,
            Issuer: null,
            TargetAccount: "ANON",
            Name: "anonymous",
            Expiry: FixedTimestamp.AddMinutes(5),
            Publish: [],
            Subscribe: [],
            ProfileName: null,
            IdentityPropagationEnabled: false,
            SelfLookupKey: null);

        var payload = AuthOutcomePayloadBuilder.Build(decision, userNKey: null, FixedTimestamp);

        Assert.Equal("success", payload.Outcome);
        Assert.Equal("anonymous", payload.Reason);
        Assert.Null(payload.Application);
        Assert.Null(payload.Profile);
    }

    [Fact]
    public void Build_AppGrant_OutcomeSuccessReasonGranted_CarriesApplicationAndProfile()
    {
        var decision = new GrantedDecision(
            Application: "notes",
            Principal: "user@example.com",
            Issuer: "https://id.example.com",
            TargetAccount: "NOTES",
            Name: "user@example.com",
            Expiry: FixedTimestamp.AddHours(1),
            Publish: ["notes.>"],
            Subscribe: ["notes.>"],
            ProfileName: "editor",
            IdentityPropagationEnabled: true,
            SelfLookupKey: "jti-123");

        var payload = AuthOutcomePayloadBuilder.Build(decision, userNKey: "UED123", FixedTimestamp);

        Assert.Equal("success", payload.Outcome);
        Assert.Equal("granted", payload.Reason);
        Assert.Equal("notes", payload.Application);
        Assert.Equal("editor", payload.Profile);
        Assert.Equal("user@example.com", payload.Principal);
        Assert.Equal("https://id.example.com", payload.Issuer);
        Assert.Equal("UED123", payload.UserNKey);
    }

    [Theory]
    [InlineData(DenyReason.NoToken, "invalid_token")]
    [InlineData(DenyReason.UnknownIssuer, "no_issuer_match")]
    [InlineData(DenyReason.SignatureInvalid, "invalid_token")]
    [InlineData(DenyReason.Expired, "invalid_token")]
    [InlineData(DenyReason.AudienceMismatch, "invalid_token")]
    [InlineData(DenyReason.AlgorithmRejected, "invalid_token")]
    [InlineData(DenyReason.NoAppMatch, "no_app_match")]
    [InlineData(DenyReason.AmbiguousApp, "ambiguous_app")]
    [InlineData(DenyReason.NoProfileMatch, "no_profile_match")]
    [InlineData(DenyReason.RequiredVariableMissing, "grant_construction_failed")]
    [InlineData(DenyReason.UnsafeTemplateValue, "grant_construction_failed")]
    [InlineData(DenyReason.NameClaimError, "grant_construction_failed")]
    [InlineData(DenyReason.AnonymousNoContributions, "invalid_token")]
    public void Build_EveryDenyReason_MapsToDocumentedWireReason(DenyReason denyReason, string expectedWireReason)
    {
        var decision = new DeniedDecision(Application: null, Principal: null, Issuer: null, Reason: denyReason);

        var payload = AuthOutcomePayloadBuilder.Build(decision, userNKey: null, FixedTimestamp);

        Assert.Equal("failure", payload.Outcome);
        Assert.Equal(expectedWireReason, payload.Reason);
    }

    [Fact]
    public void Build_RequiredVariableMissing_And_UnsafeTemplateValue_BothMapToGrantConstructionFailed()
    {
        // Doc 09: "grant_construction_failed covers required-variable and unsafe-value
        // denials (ADR 0009)" — both sub-reasons collapse to the same wire code.
        var required = new DeniedDecision(null, null, null, DenyReason.RequiredVariableMissing);
        var unsafeValue = new DeniedDecision(null, null, null, DenyReason.UnsafeTemplateValue);

        var requiredPayload = AuthOutcomePayloadBuilder.Build(required, null, FixedTimestamp);
        var unsafePayload = AuthOutcomePayloadBuilder.Build(unsafeValue, null, FixedTimestamp);

        Assert.Equal("grant_construction_failed", requiredPayload.Reason);
        Assert.Equal("grant_construction_failed", unsafePayload.Reason);
    }

    [Fact]
    public void Build_DeniedWithApplicationSelected_CarriesApplication_NoProfile()
    {
        var decision = new DeniedDecision(Application: "notes", Principal: "user@example.com", Issuer: "https://id.example.com", Reason: DenyReason.NoProfileMatch);

        var payload = AuthOutcomePayloadBuilder.Build(decision, userNKey: null, FixedTimestamp);

        Assert.Equal("notes", payload.Application);
        Assert.Null(payload.Profile);
    }

    [Fact]
    public void Build_UserNKeyNull_PayloadUserNKeyNull()
    {
        var decision = new DeniedDecision(null, null, null, DenyReason.UnknownIssuer);

        var payload = AuthOutcomePayloadBuilder.Build(decision, userNKey: null, FixedTimestamp);

        Assert.Null(payload.UserNKey);
    }

    [Fact]
    public void Build_NullDecision_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => AuthOutcomePayloadBuilder.Build(null!, null, FixedTimestamp));
    }

    [Fact]
    public void Build_NeverIncludesRawTokenMaterial()
    {
        // Decision (Contracts) structurally carries no raw-token field at all, but assert the
        // built payload's only string-shaped fields are the documented ones, so a future
        // Decision field addition can't silently leak through this builder.
        var decision = new DeniedDecision(Application: "notes", Principal: "sub-123", Issuer: "https://id.example.com", Reason: DenyReason.NoProfileMatch, Detail: "token had a bad claim: eyJhbGciOiJSUzI1NiJ9...");

        var payload = AuthOutcomePayloadBuilder.Build(decision, userNKey: "UED123", FixedTimestamp);

        Assert.DoesNotContain("eyJ", payload.Principal ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJ", payload.Application ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJ", payload.Issuer ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJ", payload.UserNKey ?? string.Empty, StringComparison.Ordinal);
        // DeniedDecision.Detail is intentionally not surfaced onto the payload at all (doc 09
        // has no "detail" field; it is logs/metrics-only per DeniedDecision's own doc comment).
    }
}
