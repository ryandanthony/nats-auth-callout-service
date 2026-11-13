// -----------------------------------------------------------------------
// <copyright file="GrantBuilderTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Decisions;
using AuthCallout.Core.Claims;
using AuthCallout.Core.Pipeline;

namespace AuthCallout.Core.Tests.Pipeline;

/// <summary>
/// Table + golden tests for <see cref="GrantBuilder"/> (doc 03 §Grant construction): static +
/// template-expanded subjects, the fixed auto-added KV-read subjects (doc 03/07, ADR
/// 0010/0011/0012/0013) with the identity-propagation toggle, self-lookup key resolution
/// (jti → sub fallback, doc 07), and the two grant-construction denial paths (ADR 0009,
/// D19).
/// </summary>
public sealed class GrantBuilderTests
{
    private static readonly DateTimeOffset FixedExpiry = DateTimeOffset.FromUnixTimeSeconds(2_000_000_000);

    [Fact]
    public void Build_StaticGrantOnly_ReturnsStaticSubjectsPlusUnconditionalKvReads()
    {
        var application = PipelineTestBuilders.Application(name: "notes");
        var profile = PipelineTestBuilders.Profile(grant: new Grant(Publish: ["notes.create"], Subscribe: ["notes.updates"]));
        var claims = ClaimsAccessor.Parse("""{ "sub": "user-1" }""");

        var result = GrantBuilder.Build(application, profile, claims, principal: "user-1", issuer: "https://issuer.example.com", FixedExpiry);

        Assert.Null(result.DenyReason);
        var decision = result.Decision!;

        // Static publish subject, plus the JetStream API subjects a real KV client needs to
        // read the unconditional keys + discovery buckets (option A: callout auto-adds them).
        Assert.Contains("notes.create", decision.Publish);
        Assert.Contains("$JS.API.STREAM.INFO.KV_callout_assertion_keys", decision.Publish);
        Assert.Contains("$JS.API.DIRECT.GET.KV_callout_assertion_keys", decision.Publish);
        Assert.Contains("$JS.API.STREAM.INFO.KV_callout_apps_notes_discovery", decision.Publish);
        Assert.Contains("$JS.API.DIRECT.GET.KV_callout_apps_notes_discovery", decision.Publish);

        // Every authenticated grant gets keys + discovery reads, unconditionally (doc 03/07),
        // plus the client inbox for the KV read reply path.
        Assert.Contains("$KV.callout_assertion_keys.>", decision.Subscribe);
        Assert.Contains("$KV.callout_apps_notes_discovery.descriptor", decision.Subscribe);

        // The inbox reply path is scoped to this connection's slug + derived principal
        // (never the account-wide _INBOX.>, which would allow cross-client eavesdropping).
        Assert.Contains(AppSlug.From("notes").ClientInboxSubscribeSubject("user-1"), decision.Subscribe);
        Assert.DoesNotContain("_INBOX.>", decision.Subscribe);
        Assert.Contains("notes.updates", decision.Subscribe);
    }

    [Fact]
    public void Build_AutoAddSessionAccessTrue_AddsTokenBucketReadSubject()
    {
        var application = PipelineTestBuilders.Application(name: "notes");
        var profile = PipelineTestBuilders.Profile(autoAddSessionAccess: true);
        var claims = ClaimsAccessor.Parse("""{ "sub": "user-1" }""");

        var result = GrantBuilder.Build(application, profile, claims, "user-1", "https://issuer.example.com", FixedExpiry);

        Assert.Contains("$KV.callout_apps_notes_oidc_tokens.>", result.Decision!.Subscribe);
    }

    [Fact]
    public void Build_AutoAddSessionAccessTrue_AddsTokenBucketJetStreamReadApiSubjects()
    {
        var application = PipelineTestBuilders.Application(name: "notes");
        var profile = PipelineTestBuilders.Profile(autoAddSessionAccess: true);
        var claims = ClaimsAccessor.Parse("""{ "sub": "user-1" }""");

        var result = GrantBuilder.Build(application, profile, claims, "user-1", "https://issuer.example.com", FixedExpiry);

        // A real KV client reading the token bucket needs its backing stream's JS-API subjects
        // (option A), on top of the $KV read subject, plus the inbox reply path.
        Assert.Contains("$JS.API.STREAM.INFO.KV_callout_apps_notes_oidc_tokens", result.Decision!.Publish);
        Assert.Contains("$JS.API.DIRECT.GET.KV_callout_apps_notes_oidc_tokens", result.Decision!.Publish);
        Assert.Contains(AppSlug.From("notes").ClientInboxSubscribeSubject("user-1"), result.Decision!.Subscribe);
    }

    [Fact]
    public void Build_AutoAddSessionAccessFalse_DoesNotAddTokenBucketReadSubject()
    {
        var application = PipelineTestBuilders.Application(name: "notes");
        var profile = PipelineTestBuilders.Profile(autoAddSessionAccess: false);
        var claims = ClaimsAccessor.Parse("""{ "sub": "user-1" }""");

        var result = GrantBuilder.Build(application, profile, claims, "user-1", "https://issuer.example.com", FixedExpiry);

        Assert.DoesNotContain("$KV.callout_apps_notes_oidc_tokens.>", result.Decision!.Subscribe);
    }

    [Theory]
    [InlineData("jti-value", null, "jti-value")]
    [InlineData(null, "sub-value", "sub-value")]
    [InlineData("jti-value", "sub-value", "jti-value")]
    public void Build_PropagationEnabled_SelfLookupKeyPrefersJtiFallsBackToSub(string? jti, string? sub, string expectedKey)
    {
        var application = PipelineTestBuilders.Application(name: "notes");
        var profile = PipelineTestBuilders.Profile(nameClaim: "name");
        var claimsJson = BuildClaimsJson(jti, sub);
        var claims = ClaimsAccessor.Parse(claimsJson);

        var result = GrantBuilder.Build(application, profile, claims, principal: sub, issuer: "https://issuer.example.com", FixedExpiry);

        Assert.Null(result.DenyReason);
        Assert.Equal(expectedKey, result.Decision!.SelfLookupKey);
        Assert.Contains($"$KV.callout_apps_notes_assertions.{AppSlug.DeriveKeySegment(expectedKey)}", result.Decision!.Subscribe);
    }

    [Fact]
    public void Build_NeitherJtiNorSubPresent_SelfLookupKeyIsNull_PropagationUnavailableButGrantStands()
    {
        var application = PipelineTestBuilders.Application(name: "notes");
        var profile = PipelineTestBuilders.Profile(nameClaim: "name");
        var claims = ClaimsAccessor.Parse("""{ "name": "Ann" }""");

        var result = GrantBuilder.Build(application, profile, claims, principal: null, issuer: "https://issuer.example.com", FixedExpiry);

        Assert.Null(result.DenyReason);
        Assert.Null(result.Decision!.SelfLookupKey);
    }

    [Fact]
    public void Build_IdentityPropagationDisabled_NoSelfLookupSubject_ButStillGetsKeysAndDiscoveryReads()
    {
        var application = PipelineTestBuilders.Application(name: "notes", identityPropagation: new IdentityPropagationConfig(Enabled: false));
        var profile = PipelineTestBuilders.Profile();
        var claims = ClaimsAccessor.Parse("""{ "sub": "user-1", "jti": "token-1" }""");

        var result = GrantBuilder.Build(application, profile, claims, "user-1", "https://issuer.example.com", FixedExpiry);

        Assert.False(result.Decision!.IdentityPropagationEnabled);
        Assert.Null(result.Decision!.SelfLookupKey);
        Assert.DoesNotContain(result.Decision!.Subscribe, s => s.Contains("_assertions.", StringComparison.Ordinal));

        // ADR 0012: keys + discovery reads are unconditional regardless of the toggle.
        Assert.Contains("$KV.callout_assertion_keys.>", result.Decision!.Subscribe);
        Assert.Contains("$KV.callout_apps_notes_discovery.descriptor", result.Decision!.Subscribe);
    }

    [Fact]
    public void Build_TemplatedSubjects_ExpandedAndAddedToGrant_DocExample()
    {
        var variables = new TemplateVariable[]
        {
            new("tenant", "household_id", [], Required: false),
            new("user", "email", [Transform.Lowercase, Transform.ReplaceOf(".", "_"), Transform.ReplaceOf("@", "_")], Required: false),
        };
        var profile = PipelineTestBuilders.TemplatedProfile(
            variables,
            publishTemplates: ["mise.house.{tenant}.user.{user}.commands"],
            subscribeTemplates: ["mise.house.{tenant}.user.{user}.events.>"]);
        var application = PipelineTestBuilders.Application(name: "mise", profiles: [profile]);
        var claims = ClaimsAccessor.Parse("""{ "sub": "user-1", "household_id": ["H1", "H2"], "email": "Alice@Example.com" }""");

        var result = GrantBuilder.Build(application, profile, claims, principal: null, issuer: "https://issuer.example.com", FixedExpiry);

        Assert.Null(result.DenyReason);

        // The two template-expanded publish subjects come first (cross-product over household_id),
        // followed by the auto-added JetStream read-API subjects for the KV buckets this grant reads.
        Assert.Equal(
            ["mise.house.H1.user.alice_example_com.commands", "mise.house.H2.user.alice_example_com.commands"],
            result.Decision!.Publish.Where(static s => !s.StartsWith("$JS.API.", StringComparison.Ordinal)).ToArray());
        Assert.Contains("$JS.API.STREAM.INFO.KV_callout_assertion_keys", result.Decision!.Publish);
        Assert.Contains("mise.house.H1.user.alice_example_com.events.>", result.Decision!.Subscribe);
        Assert.Contains("mise.house.H2.user.alice_example_com.events.>", result.Decision!.Subscribe);
    }

    [Fact]
    public void Build_RequiredVariableMissing_DeniesBeforeAnySubjectsBuilt()
    {
        var variables = new TemplateVariable[] { new("tenant", "household_id", [], Required: true) };
        var profile = PipelineTestBuilders.TemplatedProfile(variables, publishTemplates: ["x.{tenant}"], subscribeTemplates: []);
        var application = PipelineTestBuilders.Application(profiles: [profile]);
        var claims = ClaimsAccessor.Parse("""{ "sub": "user-1" }""");

        var result = GrantBuilder.Build(application, profile, claims, "user-1", "https://issuer.example.com", FixedExpiry);

        Assert.Null(result.Decision);
        Assert.Equal(DenyReason.RequiredVariableMissing, result.DenyReason);
    }

    [Fact]
    public void Build_UnsafeTemplateValue_Denies()
    {
        var variables = new TemplateVariable[] { new("user", "email", [Transform.RequireSafe], Required: false) };
        var profile = PipelineTestBuilders.TemplatedProfile(variables, publishTemplates: ["x.{user}"], subscribeTemplates: []);
        var application = PipelineTestBuilders.Application(profiles: [profile]);
        var claims = ClaimsAccessor.Parse("""{ "sub": "user-1", "email": "alice@example.com" }""");

        var result = GrantBuilder.Build(application, profile, claims, "user-1", "https://issuer.example.com", FixedExpiry);

        Assert.Null(result.Decision);
        Assert.Equal(DenyReason.UnsafeTemplateValue, result.DenyReason);
    }

    [Fact]
    public void Build_NameClaimAbsent_FallsBackToSub()
    {
        var application = PipelineTestBuilders.Application();
        var profile = PipelineTestBuilders.Profile(nameClaim: "preferred_username");
        var claims = ClaimsAccessor.Parse("""{ "sub": "user-1" }""");

        var result = GrantBuilder.Build(application, profile, claims, "user-1", "https://issuer.example.com", FixedExpiry);

        Assert.Equal("user-1", result.Decision!.Name);
    }

    [Fact]
    public void Build_NeitherNameClaimNorSubResolves_DeniesNameClaimError()
    {
        var application = PipelineTestBuilders.Application();
        var profile = PipelineTestBuilders.Profile(nameClaim: "preferred_username");
        var claims = ClaimsAccessor.Parse("{}");

        var result = GrantBuilder.Build(application, profile, claims, principal: null, issuer: "https://issuer.example.com", FixedExpiry);

        Assert.Null(result.Decision);
        Assert.Equal(DenyReason.NameClaimError, result.DenyReason);
    }

    [Fact]
    public void Build_PopulatesEveryDownstreamField_TargetAccountExpiryProfileNameIssuerPrincipal()
    {
        var application = PipelineTestBuilders.Application(name: "notes");
        var profile = PipelineTestBuilders.Profile(name: "editor");
        var claims = ClaimsAccessor.Parse("""{ "sub": "user-1" }""");

        var result = GrantBuilder.Build(application, profile, claims, "user-1", "https://issuer.example.com", FixedExpiry);

        var decision = result.Decision!;
        Assert.Equal("notes", decision.Application);
        Assert.Equal("user-1", decision.Principal);
        Assert.Equal("https://issuer.example.com", decision.Issuer);
        Assert.Equal("NOTES", decision.TargetAccount);
        Assert.Equal("editor", decision.ProfileName);
        Assert.Equal(FixedExpiry, decision.Expiry);
        Assert.True(decision.IdentityPropagationEnabled);
    }

    private static string BuildClaimsJson(string? jti, string? sub)
    {
        // "name" is always present so name resolution succeeds independent of jti/sub, which
        // this test varies to isolate self-lookup-key resolution (doc 07 §Key parts).
        var pairs = new List<string> { "\"name\": \"Test User\"" };
        if (jti is not null)
        {
            pairs.Add($"\"jti\": \"{jti}\"");
        }

        if (sub is not null)
        {
            pairs.Add($"\"sub\": \"{sub}\"");
        }

        return "{ " + string.Join(", ", pairs) + " }";
    }
}
