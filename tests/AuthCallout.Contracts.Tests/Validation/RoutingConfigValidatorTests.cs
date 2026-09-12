// -----------------------------------------------------------------------
// <copyright file="RoutingConfigValidatorTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Primitives;
using AuthCallout.Contracts.Validation;

namespace AuthCallout.Contracts.Tests.Validation;

/// <summary>
/// Table tests for every doc 04 §Validation / ADR 0016 rule enforced by
/// <see cref="ConfigValidator.ValidateRouting"/>. Each test asserts both the accept and
/// reject side of one rule, and asserts on the deterministic error code (not just
/// "IsValid is false") so a regression that changes the wrong rule's text still fails.
/// </summary>
public sealed class RoutingConfigValidatorTests
{
    [Fact]
    public void ValidRoutingAggregate_Passes()
    {
        var routing = TestBuilders.ValidRouting();

        var result = ConfigValidator.ValidateRouting(routing);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void DuplicateApplicationNames_Rejected()
    {
        var app1 = TestBuilders.ValidApplication(name: "notes", issuer: TestBuilders.ValidIssuer("https://a.example.com"));
        var app2 = TestBuilders.ValidApplication(name: "notes", issuer: TestBuilders.ValidIssuer("https://b.example.com"));
        var routing = new RoutingConfig([app1, app2]);

        var result = ConfigValidator.ValidateRouting(routing);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => string.Equals(e.Code, "duplicate-application-name", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateApplicationSlugs_Rejected()
    {
        // "Notes App" and "notes-app" both slugify to "notes-app" (ADR 0013).
        var app1 = TestBuilders.ValidApplication(name: "Notes App", issuer: TestBuilders.ValidIssuer("https://a.example.com"));
        var app2 = TestBuilders.ValidApplication(name: "notes-app", issuer: TestBuilders.ValidIssuer("https://b.example.com"));
        var routing = new RoutingConfig([app1, app2]);

        var result = ConfigValidator.ValidateRouting(routing);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => string.Equals(e.Code, "duplicate-application-slug", StringComparison.Ordinal));
    }

    [Fact]
    public void InvalidApplicationSlug_Rejected()
    {
        var app = TestBuilders.ValidApplication(name: "!!!");
        var routing = new RoutingConfig([app]);

        var result = ConfigValidator.ValidateRouting(routing);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => string.Equals(e.Code, "invalid-application-slug", StringComparison.Ordinal));
    }

    [Fact]
    public void SingleApplicationOnIssuer_WithNoMatch_IsValid()
    {
        var app = TestBuilders.ValidApplication(match: null);
        var routing = new RoutingConfig([app]);

        var result = ConfigValidator.ValidateRouting(routing);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void TwoApplicationsSharingIssuer_OneWithNullMatch_Rejected()
    {
        var issuer = TestBuilders.ValidIssuer("https://shared.example.com");
        var withMatch = TestBuilders.ValidApplication(
            name: "app-a",
            issuer: issuer,
            match: new ClaimMatch(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["azp"] = ["client-a"] }));
        var withoutMatch = TestBuilders.ValidApplication(name: "app-b", issuer: issuer, match: null);
        var routing = new RoutingConfig([withMatch, withoutMatch]);

        var result = ConfigValidator.ValidateRouting(routing);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => string.Equals(e.Code, "ambiguous-null-match", StringComparison.Ordinal));
    }

    [Fact]
    public void TwoApplicationsSharingIssuer_WithIdenticalMatch_Rejected()
    {
        var issuer = TestBuilders.ValidIssuer("https://shared.example.com");
        var match = new ClaimMatch(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["azp"] = ["client-a"] });
        var app1 = TestBuilders.ValidApplication(name: "app-a", issuer: issuer, match: match);
        var app2 = TestBuilders.ValidApplication(name: "app-b", issuer: issuer, match: match);
        var routing = new RoutingConfig([app1, app2]);

        var result = ConfigValidator.ValidateRouting(routing);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => string.Equals(e.Code, "indistinguishable-applications", StringComparison.Ordinal));
    }

    [Fact]
    public void TwoApplicationsSharingIssuer_WithDivergentValidationParameters_Rejected()
    {
        // Stage 2 validates once per request using the shared authority's parameters
        // (ResolutionPipeline takes candidates[0].Issuer), so two apps on one authority
        // with different validation-relevant settings would make effective strictness
        // depend on candidate ordering: a token failing app-a's audience check could
        // validate under app-b's laxer parameters and then route to app-a at stage 3.
        // The aggregate must therefore reject any divergence (fail closed at config load).
        var strict = TestBuilders.ValidIssuer("https://shared.example.com");
        var lax = strict with { ValidateAudience = false, Audience = null };
        var app1 = TestBuilders.ValidApplication(
            name: "app-a",
            issuer: strict,
            match: new ClaimMatch(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["azp"] = ["client-a"] }));
        var app2 = TestBuilders.ValidApplication(
            name: "app-b",
            issuer: lax,
            match: new ClaimMatch(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["azp"] = ["client-b"] }));
        var routing = new RoutingConfig([app1, app2]);

        var result = ConfigValidator.ValidateRouting(routing);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => string.Equals(e.Code, "divergent-issuer-config", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("clockSkew")]
    [InlineData("allowedAlgorithms")]
    [InlineData("jwksUri")]
    [InlineData("audience")]
    public void TwoApplicationsSharingIssuer_AnySingleDivergentIssuerField_Rejected(string divergentField)
    {
        var baseline = TestBuilders.ValidIssuer("https://shared.example.com");
        var divergent = divergentField switch
        {
            "clockSkew" => baseline with { ClockSkew = DurationString.Parse("120s") },
            "allowedAlgorithms" => baseline with { AllowedAlgorithms = ["RS256", "ES256"] },
            "jwksUri" => baseline with { JwksUri = "https://shared.example.com/other/jwks" },
            "audience" => baseline with { Audience = ["nats", "other"] },
            _ => throw new ArgumentOutOfRangeException(nameof(divergentField)),
        };
        var app1 = TestBuilders.ValidApplication(
            name: "app-a",
            issuer: baseline,
            match: new ClaimMatch(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["azp"] = ["client-a"] }));
        var app2 = TestBuilders.ValidApplication(
            name: "app-b",
            issuer: divergent,
            match: new ClaimMatch(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["azp"] = ["client-b"] }));
        var routing = new RoutingConfig([app1, app2]);

        var result = ConfigValidator.ValidateRouting(routing);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => string.Equals(e.Code, "divergent-issuer-config", StringComparison.Ordinal));
    }

    [Fact]
    public void TwoApplicationsSharingIssuer_WithIdenticalIssuerConfig_NotRejectedForDivergence()
    {
        // Identical issuer blocks (the supported multi-app-per-authority shape) must not
        // trip the divergence rule; distinct matches keep the pair distinguishable.
        var issuer = TestBuilders.ValidIssuer("https://shared.example.com");
        var app1 = TestBuilders.ValidApplication(
            name: "app-a",
            issuer: issuer,
            match: new ClaimMatch(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["azp"] = ["client-a"] }));
        var app2 = TestBuilders.ValidApplication(
            name: "app-b",
            issuer: issuer,
            match: new ClaimMatch(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["azp"] = ["client-b"] }));
        var routing = new RoutingConfig([app1, app2]);

        var result = ConfigValidator.ValidateRouting(routing);

        Assert.True(result.IsValid);
        Assert.DoesNotContain(result.Errors, e => string.Equals(e.Code, "divergent-issuer-config", StringComparison.Ordinal));
    }

    [Fact]
    public void TwoApplicationsSharingIssuer_WithDistinctMatches_IsValid()
    {
        var issuer = TestBuilders.ValidIssuer("https://shared.example.com");
        var app1 = TestBuilders.ValidApplication(
            name: "app-a",
            issuer: issuer,
            match: new ClaimMatch(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["azp"] = ["client-a"] }));
        var app2 = TestBuilders.ValidApplication(
            name: "app-b",
            issuer: issuer,
            match: new ClaimMatch(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["azp"] = ["client-b"] }));
        var routing = new RoutingConfig([app1, app2]);

        var result = ConfigValidator.ValidateRouting(routing);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void EmptyApplicationName_Rejected()
    {
        var app = TestBuilders.ValidApplication(name: string.Empty) with { Name = string.Empty };
        var routing = new RoutingConfig([app]);

        var result = ConfigValidator.ValidateRouting(routing);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => string.Equals(e.Code, "application-name-required", StringComparison.Ordinal));
    }

    [Fact]
    public void EmptyTargetAccount_Rejected()
    {
        var app = TestBuilders.ValidApplication() with { TargetAccount = string.Empty };
        var routing = new RoutingConfig([app]);

        var result = ConfigValidator.ValidateRouting(routing);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => string.Equals(e.Code, "target-account-required", StringComparison.Ordinal));
    }

    [Fact]
    public void EmptyIssuerAuthority_Rejected()
    {
        var issuer = TestBuilders.ValidIssuer() with { Authority = string.Empty };
        var app = TestBuilders.ValidApplication(issuer: issuer);
        var routing = new RoutingConfig([app]);

        var result = ConfigValidator.ValidateRouting(routing);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => string.Equals(e.Code, "issuer-authority-required", StringComparison.Ordinal));
    }

    [Fact]
    public void ClockSkewAtCap_IsValid()
    {
        var issuer = TestBuilders.ValidIssuer() with { ClockSkew = IssuerConfig.ClockSkewCap };
        var app = TestBuilders.ValidApplication(issuer: issuer);
        var routing = new RoutingConfig([app]);

        var result = ConfigValidator.ValidateRouting(routing);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ClockSkewAboveCap_Rejected()
    {
        var issuer = TestBuilders.ValidIssuer() with { ClockSkew = Primitives.DurationString.Parse("301s") };
        var app = TestBuilders.ValidApplication(issuer: issuer);
        var routing = new RoutingConfig([app]);

        var result = ConfigValidator.ValidateRouting(routing);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => string.Equals(e.Code, "clock-skew-exceeds-cap", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("HS256")]
    [InlineData("HS384")]
    [InlineData("none")]
    public void DisallowedAlgorithm_Rejected(string algorithm)
    {
        var issuer = TestBuilders.ValidIssuer() with { AllowedAlgorithms = [algorithm] };
        var app = TestBuilders.ValidApplication(issuer: issuer);
        var routing = new RoutingConfig([app]);

        var result = ConfigValidator.ValidateRouting(routing);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => string.Equals(e.Code, "algorithm-not-allowed", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("RS256")]
    [InlineData("ES384")]
    [InlineData("PS512")]
    public void AllowedAlgorithm_Accepted(string algorithm)
    {
        var issuer = TestBuilders.ValidIssuer() with { AllowedAlgorithms = [algorithm] };
        var app = TestBuilders.ValidApplication(issuer: issuer);
        var routing = new RoutingConfig([app]);

        var result = ConfigValidator.ValidateRouting(routing);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void NoAllowedAlgorithms_Rejected()
    {
        var issuer = TestBuilders.ValidIssuer() with { AllowedAlgorithms = [] };
        var app = TestBuilders.ValidApplication(issuer: issuer);
        var routing = new RoutingConfig([app]);

        var result = ConfigValidator.ValidateRouting(routing);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => string.Equals(e.Code, "algorithms-required", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateAudienceTrue_WithNoAudience_Rejected()
    {
        var issuer = TestBuilders.ValidIssuer() with { ValidateAudience = true, Audience = null };
        var app = TestBuilders.ValidApplication(issuer: issuer);
        var routing = new RoutingConfig([app]);

        var result = ConfigValidator.ValidateRouting(routing);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => string.Equals(e.Code, "audience-required-when-validating", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateAudienceFalse_WithNoAudience_IsValid()
    {
        var issuer = TestBuilders.ValidIssuer() with { ValidateAudience = false, Audience = null };
        var app = TestBuilders.ValidApplication(issuer: issuer);
        var routing = new RoutingConfig([app]);

        var result = ConfigValidator.ValidateRouting(routing);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void EmptyProfileName_Rejected()
    {
        var profile = TestBuilders.ValidProfile(name: string.Empty) with { Name = string.Empty };
        var app = TestBuilders.ValidApplication(profiles: [profile]);
        var routing = new RoutingConfig([app]);

        var result = ConfigValidator.ValidateRouting(routing);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => string.Equals(e.Code, "profile-name-required", StringComparison.Ordinal));
    }

    [Fact]
    public void AutoAddSessionAccess_WithPropagationDisabled_Rejected()
    {
        var profile = TestBuilders.ValidProfile(autoAddSessionAccess: true);
        var app = TestBuilders.ValidApplication(
            profiles: [profile],
            identityPropagation: new IdentityPropagationConfig(Enabled: false));
        var routing = new RoutingConfig([app]);

        var result = ConfigValidator.ValidateRouting(routing);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => string.Equals(e.Code, "auto-session-access-requires-propagation", StringComparison.Ordinal));
    }

    [Fact]
    public void AutoAddSessionAccess_WithPropagationEnabled_IsValid()
    {
        var profile = TestBuilders.ValidProfile(autoAddSessionAccess: true);
        var app = TestBuilders.ValidApplication(
            profiles: [profile],
            identityPropagation: new IdentityPropagationConfig(Enabled: true));
        var routing = new RoutingConfig([app]);

        var result = ConfigValidator.ValidateRouting(routing);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void DuplicateVariableName_Rejected()
    {
        var variable = new TemplateVariable("tenant", "household_id", [], Required: true);
        var profile = TestBuilders.ValidProfile() with { Variables = [variable, variable] };
        var app = TestBuilders.ValidApplication(profiles: [profile]);
        var routing = new RoutingConfig([app]);

        var result = ConfigValidator.ValidateRouting(routing);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => string.Equals(e.Code, "duplicate-variable-name", StringComparison.Ordinal));
    }

    [Fact]
    public void EmptyVariableClaimPath_Rejected()
    {
        var variable = new TemplateVariable("tenant", string.Empty, [], Required: true);
        var profile = TestBuilders.ValidProfile() with { Variables = [variable] };
        var app = TestBuilders.ValidApplication(profiles: [profile]);
        var routing = new RoutingConfig([app]);

        var result = ConfigValidator.ValidateRouting(routing);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => string.Equals(e.Code, "variable-claim-required", StringComparison.Ordinal));
    }

    [Fact]
    public void MalformedReplaceTransform_MissingFrom_Rejected()
    {
        var variable = new TemplateVariable(
            "user", "email", [new Transform(TransformKind.Replace, From: null, To: "_")], Required: false);
        var profile = TestBuilders.ValidProfile() with { Variables = [variable] };
        var app = TestBuilders.ValidApplication(profiles: [profile]);
        var routing = new RoutingConfig([app]);

        var result = ConfigValidator.ValidateRouting(routing);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => string.Equals(e.Code, "malformed-replace-transform", StringComparison.Ordinal));
    }

    [Fact]
    public void MalformedAffixTransform_MissingText_Rejected()
    {
        var variable = new TemplateVariable(
            "user", "email", [new Transform(TransformKind.Prefix, Text: null)], Required: false);
        var profile = TestBuilders.ValidProfile() with { Variables = [variable] };
        var app = TestBuilders.ValidApplication(profiles: [profile]);
        var routing = new RoutingConfig([app]);

        var result = ConfigValidator.ValidateRouting(routing);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => string.Equals(e.Code, "malformed-affix-transform", StringComparison.Ordinal));
    }

    [Fact]
    public void WellFormedTransformPipeline_IsValid()
    {
        var variable = new TemplateVariable(
            "user",
            "email",
            [Transform.Lowercase, Transform.ReplaceOf(".", "_"), Transform.RequireSafe],
            Required: true);
        var profile = TestBuilders.ValidProfile() with
        {
            Variables = [variable],
            PublishTemplates = ["notes.{user}.>"],
        };
        var app = TestBuilders.ValidApplication(profiles: [profile]);
        var routing = new RoutingConfig([app]);

        var result = ConfigValidator.ValidateRouting(routing);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void TemplateReferencingUndeclaredVariable_Rejected()
    {
        var profile = TestBuilders.ValidProfile() with { PublishTemplates = ["notes.{missing}.>"] };
        var app = TestBuilders.ValidApplication(profiles: [profile]);
        var routing = new RoutingConfig([app]);

        var result = ConfigValidator.ValidateRouting(routing);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => string.Equals(e.Code, "unknown-template-variable", StringComparison.Ordinal));
    }

    [Fact]
    public void TemplateReferencingDeclaredVariable_IsValid()
    {
        var variable = new TemplateVariable("tenant", "household_id", [], Required: true);
        var profile = TestBuilders.ValidProfile() with
        {
            Variables = [variable],
            SubscribeTemplates = ["notes.{tenant}.>"],
        };
        var app = TestBuilders.ValidApplication(profiles: [profile]);
        var routing = new RoutingConfig([app]);

        var result = ConfigValidator.ValidateRouting(routing);

        Assert.True(result.IsValid);
    }
}
