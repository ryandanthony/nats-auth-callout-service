// -----------------------------------------------------------------------
// <copyright file="ResolutionPipelineTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Decisions;
using AuthCallout.Contracts.Seams;
using AuthCallout.Core.Claims;
using AuthCallout.Core.Pipeline;
using NSubstitute;

namespace AuthCallout.Core.Tests.Pipeline;

/// <summary>
/// End-to-end table tests for <see cref="ResolutionPipeline"/> against doc 03's flowchart /
/// ADR 0003's decision tree and doc 02's decision-tree examples, substituting
/// <see cref="ITokenValidator"/> (NSubstitute) so Core stays exercised without any real JWT
/// signature machinery (P06's concern). Also covers ADR 0003's "resolution order fixed":
/// application `Match` is evaluated only post-validation, asserted via call-order on the
/// substitute.
/// </summary>
public sealed class ResolutionPipelineTests
{
    private const string Issuer = "https://issuer.example.com";
    private static readonly DateTimeOffset FixedNow = DateTimeOffset.FromUnixTimeSeconds(1_900_000_000);
    private static readonly TimeProvider FrozenTimeProvider = new FixedTimeProvider(FixedNow);

    [Fact]
    public async Task ResolveAsync_NoToken_ComposesAnonymousUnion()
    {
        // doc 03 Stage 0 / ADR 0003 decision-tree case 1: no token -> anonymous.
        var app = PipelineTestBuilders.Application(anonymousProfile: new Grant(["notes.public"], []));
        var config = PipelineTestBuilders.ActiveConfig(routing: PipelineTestBuilders.Routing(app));
        var pipeline = new ResolutionPipeline(Substitute.For<ITokenValidator>(), FrozenTimeProvider);

        var decision = await pipeline.ResolveAsync(PipelineTestBuilders.Request(rawToken: null), config, CancellationToken.None);

        var granted = Assert.IsType<GrantedDecision>(decision);
        Assert.Equal(["notes.public"], granted.Publish);
        Assert.Null(granted.Application);
    }

    [Fact]
    public async Task ResolveAsync_NoToken_AnonymousUnionEmpty_DeniesAnonymousNoContributions()
    {
        var app = PipelineTestBuilders.Application(anonymousProfile: null);
        var config = PipelineTestBuilders.ActiveConfig(routing: PipelineTestBuilders.Routing(app));
        var pipeline = new ResolutionPipeline(Substitute.For<ITokenValidator>(), FrozenTimeProvider);

        var decision = await pipeline.ResolveAsync(PipelineTestBuilders.Request(rawToken: null), config, CancellationToken.None);

        var denied = Assert.IsType<DeniedDecision>(decision);
        Assert.Equal(DenyReason.AnonymousNoContributions, denied.Reason);
    }

    [Fact]
    public async Task ResolveAsync_TokenIssuerMatchesNoApplication_DeniesUnknownIssuer()
    {
        // ADR 0003 decision-tree case 2.
        var app = PipelineTestBuilders.Application(issuer: PipelineTestBuilders.Issuer("https://other-issuer.example.com"));
        var config = PipelineTestBuilders.ActiveConfig(routing: PipelineTestBuilders.Routing(app));
        var validator = Substitute.For<ITokenValidator>();
        var pipeline = new ResolutionPipeline(validator, FrozenTimeProvider);
        var token = PipelineTestBuilders.RawToken(issuer: Issuer);

        var decision = await pipeline.ResolveAsync(PipelineTestBuilders.Request(token), config, CancellationToken.None);

        var denied = Assert.IsType<DeniedDecision>(decision);
        Assert.Equal(DenyReason.UnknownIssuer, denied.Reason);
        Assert.Equal(Issuer, denied.Issuer);
        await validator.DidNotReceiveWithAnyArgs().ValidateAsync(default!, default!, default);
    }

    [Fact]
    public async Task ResolveAsync_MalformedToken_UnreadableIssuer_DeniesUnknownIssuer()
    {
        var app = PipelineTestBuilders.Application();
        var config = PipelineTestBuilders.ActiveConfig(routing: PipelineTestBuilders.Routing(app));
        var pipeline = new ResolutionPipeline(Substitute.For<ITokenValidator>(), FrozenTimeProvider);

        var decision = await pipeline.ResolveAsync(PipelineTestBuilders.Request("not-a-jwt"), config, CancellationToken.None);

        var denied = Assert.IsType<DeniedDecision>(decision);
        Assert.Equal(DenyReason.UnknownIssuer, denied.Reason);
    }

    [Fact]
    public async Task ResolveAsync_TokenInvalid_DeniesWithValidatorsReason_NeverDowngradedToAnonymous()
    {
        // ADR 0003 decision-tree case 3: "A present-but-bad token is never downgraded to anonymous."
        var app = PipelineTestBuilders.Application(anonymousProfile: new Grant(["notes.public"], []));
        var config = PipelineTestBuilders.ActiveConfig(routing: PipelineTestBuilders.Routing(app));
        var validator = Substitute.For<ITokenValidator>();
        validator.ValidateAsync(Arg.Any<string>(), Arg.Any<IssuerConfig>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TokenValidationOutcome.Invalid(DenyReason.Expired)));
        var pipeline = new ResolutionPipeline(validator, FrozenTimeProvider);
        var token = PipelineTestBuilders.RawToken(issuer: Issuer);

        var decision = await pipeline.ResolveAsync(PipelineTestBuilders.Request(token), config, CancellationToken.None);

        var denied = Assert.IsType<DeniedDecision>(decision);
        Assert.Equal(DenyReason.Expired, denied.Reason);
    }

    [Fact]
    public async Task ResolveAsync_ValidToken_ZeroAppMatches_DeniesNoAppMatch()
    {
        // ADR 0003 decision-tree case 4.
        var app = PipelineTestBuilders.Application(match: MatchOf("aud", "notes-api"));
        var config = PipelineTestBuilders.ActiveConfig(routing: PipelineTestBuilders.Routing(app));
        var validator = ValidatorReturning("""{ "aud": "other-api" }""");
        var pipeline = new ResolutionPipeline(validator, FrozenTimeProvider);
        var token = PipelineTestBuilders.RawToken(issuer: Issuer);

        var decision = await pipeline.ResolveAsync(PipelineTestBuilders.Request(token), config, CancellationToken.None);

        var denied = Assert.IsType<DeniedDecision>(decision);
        Assert.Equal(DenyReason.NoAppMatch, denied.Reason);
    }

    [Fact]
    public async Task ResolveAsync_ValidToken_MultipleAppMatches_DeniesAmbiguousApp()
    {
        // ADR 0003 decision-tree case 5.
        var app1 = PipelineTestBuilders.Application(name: "notes", match: ClaimMatch.Empty);
        var app2 = PipelineTestBuilders.Application(name: "tasks", match: ClaimMatch.Empty);
        var config = PipelineTestBuilders.ActiveConfig(routing: PipelineTestBuilders.Routing(app1, app2));
        var validator = ValidatorReturning("""{ "sub": "user-1" }""");
        var pipeline = new ResolutionPipeline(validator, FrozenTimeProvider);
        var token = PipelineTestBuilders.RawToken(issuer: Issuer);

        var decision = await pipeline.ResolveAsync(PipelineTestBuilders.Request(token), config, CancellationToken.None);

        var denied = Assert.IsType<DeniedDecision>(decision);
        Assert.Equal(DenyReason.AmbiguousApp, denied.Reason);
    }

    [Fact]
    public async Task ResolveAsync_ValidToken_OneAppNoProfileMatches_DeniesNoProfileMatch()
    {
        // ADR 0003 decision-tree case 6.
        var profile = PipelineTestBuilders.Profile(match: MatchOf("role", "admin"));
        var app = PipelineTestBuilders.Application(profiles: [profile]);
        var config = PipelineTestBuilders.ActiveConfig(routing: PipelineTestBuilders.Routing(app));
        var validator = ValidatorReturning("""{ "sub": "user-1", "role": "guest" }""");
        var pipeline = new ResolutionPipeline(validator, FrozenTimeProvider);
        var token = PipelineTestBuilders.RawToken(issuer: Issuer);

        var decision = await pipeline.ResolveAsync(PipelineTestBuilders.Request(token), config, CancellationToken.None);

        var denied = Assert.IsType<DeniedDecision>(decision);
        Assert.Equal(DenyReason.NoProfileMatch, denied.Reason);
        Assert.Equal("notes", denied.Application);
    }

    [Fact]
    public async Task ResolveAsync_ValidToken_OneAppOneProfile_GrantsFirstMatchingProfile()
    {
        // ADR 0003 decision-tree case 7.
        var profile = PipelineTestBuilders.Profile(name: "editor", match: MatchOf("role", "editor"));
        var app = PipelineTestBuilders.Application(name: "notes", profiles: [profile]);
        var config = PipelineTestBuilders.ActiveConfig(routing: PipelineTestBuilders.Routing(app));
        var claimsJson = $$"""{ "sub": "user-1", "role": "editor", "exp": {{FixedNow.ToUnixTimeSeconds() + 300}} }""";
        var validator = ValidatorReturning(claimsJson);
        var pipeline = new ResolutionPipeline(validator, FrozenTimeProvider);
        var token = PipelineTestBuilders.RawToken(issuer: Issuer);

        var decision = await pipeline.ResolveAsync(PipelineTestBuilders.Request(token), config, CancellationToken.None);

        var granted = Assert.IsType<GrantedDecision>(decision);
        Assert.Equal("notes", granted.Application);
        Assert.Equal("editor", granted.ProfileName);
        Assert.Equal("user-1", granted.Principal);
        Assert.Equal(Issuer, granted.Issuer);
    }

    [Fact]
    public async Task ResolveAsync_ExpiryComesFromTokenExpClaim_NotFromAnonymousExpiry()
    {
        var expUnixSeconds = FixedNow.ToUnixTimeSeconds() + 3600;
        var app = PipelineTestBuilders.Application();
        var config = PipelineTestBuilders.ActiveConfig(routing: PipelineTestBuilders.Routing(app));
        var validator = ValidatorReturning($$"""{ "sub": "user-1", "exp": {{expUnixSeconds}} }""");
        var pipeline = new ResolutionPipeline(validator, FrozenTimeProvider);
        var token = PipelineTestBuilders.RawToken(issuer: Issuer);

        var decision = await pipeline.ResolveAsync(PipelineTestBuilders.Request(token), config, CancellationToken.None);

        var granted = Assert.IsType<GrantedDecision>(decision);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(expUnixSeconds), granted.Expiry);
    }

    [Fact]
    public async Task ResolveAsync_ValidationOrder_ApplicationMatchEvaluatedOnlyAfterValidation()
    {
        // ADR 0003 §Resolution order: "Application Match evaluated only post-validation." We
        // assert this by making a substitute validator that itself checks it is invoked
        // exactly once, before any claim-based selection could occur, then returns success.
        var calls = new List<string>();
        var app = PipelineTestBuilders.Application(match: MatchOf("aud", "notes-api"));
        var config = PipelineTestBuilders.ActiveConfig(routing: PipelineTestBuilders.Routing(app));
        var validator = Substitute.For<ITokenValidator>();
        validator.ValidateAsync(Arg.Any<string>(), Arg.Any<IssuerConfig>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls.Add("validate");
                return Task.FromResult(TokenValidationOutcome.Valid(ClaimsAccessor.Parse(WithDefaultExp("""{ "sub": "user-1", "aud": "notes-api" }"""))));
            });
        var pipeline = new ResolutionPipeline(validator, FrozenTimeProvider);
        var token = PipelineTestBuilders.RawToken(issuer: Issuer);

        var decision = await pipeline.ResolveAsync(PipelineTestBuilders.Request(token), config, CancellationToken.None);

        Assert.IsType<GrantedDecision>(decision);
        Assert.Equal(["validate"], calls);
        await validator.Received(1).ValidateAsync(token, Arg.Any<IssuerConfig>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_PrincipalFallsBackToAzpWhenSubAbsent()
    {
        var app = PipelineTestBuilders.Application();
        var config = PipelineTestBuilders.ActiveConfig(routing: PipelineTestBuilders.Routing(app));
        var validator = ValidatorReturning("""{ "azp": "billing-service" }""");
        var pipeline = new ResolutionPipeline(validator, FrozenTimeProvider);
        var token = PipelineTestBuilders.RawToken(issuer: Issuer);

        // sub is absent and profile's nameClaim defaults to "sub", so grant construction
        // denies with NameClaimError, but the denial still carries the azp-derived principal
        // (doc 03: principal is set as soon as claims are available, even on a later denial).
        var decision = await pipeline.ResolveAsync(PipelineTestBuilders.Request(token), config, CancellationToken.None);

        var denied = Assert.IsType<DeniedDecision>(decision);
        Assert.Equal("billing-service", denied.Principal);
        Assert.Equal(DenyReason.NameClaimError, denied.Reason);
    }

    [Fact]
    public async Task ResolveAsync_GrantConstructionDenial_StillCarriesApplicationAndPrincipal()
    {
        var profile = PipelineTestBuilders.TemplatedProfile(
            [new TemplateVariable("tenant", "household_id", [], Required: true)],
            publishTemplates: ["x.{tenant}"],
            subscribeTemplates: []);
        var app = PipelineTestBuilders.Application(name: "notes", profiles: [profile]);
        var config = PipelineTestBuilders.ActiveConfig(routing: PipelineTestBuilders.Routing(app));
        var validator = ValidatorReturning("""{ "sub": "user-1" }""");
        var pipeline = new ResolutionPipeline(validator, FrozenTimeProvider);
        var token = PipelineTestBuilders.RawToken(issuer: Issuer);

        var decision = await pipeline.ResolveAsync(PipelineTestBuilders.Request(token), config, CancellationToken.None);

        var denied = Assert.IsType<DeniedDecision>(decision);
        Assert.Equal(DenyReason.RequiredVariableMissing, denied.Reason);
        Assert.Equal("notes", denied.Application);
        Assert.Equal("user-1", denied.Principal);
    }

    /// <summary>
    /// Gets the full doc 02 / ADR 0003 decision tree, driven end to end through the pure
    /// pipeline against one fixed application/profile fixture: every deny case shares one
    /// fixture so only the request/validator input that defines the case varies.
    /// </summary>
    /// <value>The decision-tree deny table rows.</value>
    public static TheoryData<Doc03DecisionTreeCase> Doc02DecisionTreeDenyCases => new()
    {
        // Case 2: iss matches no configured application -> UnknownIssuer.
        new Doc03DecisionTreeCase(ValidatorOutcome: null, ExpectedDeny: DenyReason.UnknownIssuer, TokenIssuer: "https://unconfigured-issuer.example.com"),

        // Case 3: iss known, but the token itself fails validation -> the validator's own reason (never anonymous).
        new Doc03DecisionTreeCase(ValidatorOutcome: TokenValidationOutcome.Invalid(DenyReason.SignatureInvalid), ExpectedDeny: DenyReason.SignatureInvalid),

        // Case 6: valid token, issuer matches, app selected, but no profile matches -> NoProfileMatch.
        new Doc03DecisionTreeCase(ValidatorOutcome: TokenValidationOutcome.Valid(ClaimsAccessor.Parse("""{ "sub": "u1", "role": "nonmember" }""")), ExpectedDeny: DenyReason.NoProfileMatch),
    };

    [Fact]
    public async Task ResolveAsync_Doc02DecisionTreeCase1_NoToken_ComposesAnonymous()
    {
        var (config, pipeline) = BuildDoc02Fixture(Substitute.For<ITokenValidator>());

        var decision = await pipeline.ResolveAsync(PipelineTestBuilders.Request(rawToken: null), config, CancellationToken.None);

        var granted = Assert.IsType<GrantedDecision>(decision);
        Assert.Null(granted.Application);
        Assert.Equal(["notes.public"], granted.Publish);
    }

    [Fact]
    public async Task ResolveAsync_Doc02DecisionTreeCase7_ValidTokenOneAppOneProfile_Grants()
    {
        var (config, pipeline) = BuildDoc02Fixture(ValidatorReturning("""{ "sub": "u1", "role": "member" }"""));

        var decision = await pipeline.ResolveAsync(PipelineTestBuilders.Request(PipelineTestBuilders.RawToken(Issuer)), config, CancellationToken.None);

        var granted = Assert.IsType<GrantedDecision>(decision);
        Assert.Equal("notes", granted.Application);
        Assert.Equal("member", granted.ProfileName);
    }

    [Theory]
    [MemberData(nameof(Doc02DecisionTreeDenyCases))]
    public async Task ResolveAsync_Doc02DecisionTreeDenyCases_MatchDocumentedReason(Doc03DecisionTreeCase testCase)
    {
        ArgumentNullException.ThrowIfNull(testCase);

        var validator = Substitute.For<ITokenValidator>();
        if (testCase.ValidatorOutcome is { } outcome)
        {
            validator.ValidateAsync(Arg.Any<string>(), Arg.Any<IssuerConfig>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(outcome));
        }

        var (config, pipeline) = BuildDoc02Fixture(validator);
        var token = PipelineTestBuilders.RawToken(issuer: testCase.TokenIssuer ?? Issuer);

        var decision = await pipeline.ResolveAsync(PipelineTestBuilders.Request(token), config, CancellationToken.None);

        var denied = Assert.IsType<DeniedDecision>(decision);
        Assert.Equal(testCase.ExpectedDeny, denied.Reason);
    }

    private static (ActiveConfig config, ResolutionPipeline pipeline) BuildDoc02Fixture(ITokenValidator validator)
    {
        var profile = PipelineTestBuilders.Profile(name: "member", match: MatchOf("role", "member"));
        var app = PipelineTestBuilders.Application(name: "notes", anonymousProfile: new Grant(["notes.public"], []), profiles: [profile]);
        var config = PipelineTestBuilders.ActiveConfig(routing: PipelineTestBuilders.Routing(app));
        return (config, new ResolutionPipeline(validator, FrozenTimeProvider));
    }

    [Fact]
    public async Task ResolveAsync_NullRequest_Throws()
    {
        var pipeline = new ResolutionPipeline(Substitute.For<ITokenValidator>());
        var config = PipelineTestBuilders.ActiveConfig();

        await Assert.ThrowsAsync<ArgumentNullException>(() => pipeline.ResolveAsync(null!, config, CancellationToken.None));
    }

    [Fact]
    public async Task ResolveAsync_NullConfig_Throws()
    {
        var pipeline = new ResolutionPipeline(Substitute.For<ITokenValidator>());
        var request = PipelineTestBuilders.Request(rawToken: null);

        await Assert.ThrowsAsync<ArgumentNullException>(() => pipeline.ResolveAsync(request, null!, CancellationToken.None));
    }

    [Fact]
    public void Constructor_NullTokenValidator_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ResolutionPipeline(null!));
    }

    /// <summary>
    /// Builds a substitute <see cref="ITokenValidator"/> returning a successful outcome for
    /// <paramref name="claimsJson"/>, injecting a default future <c>exp</c> when the caller's
    /// claims omit one — real validated claims always carry <c>exp</c> (ADR 0016
    /// <c>RequireExpirationTime</c>), so every test simulating a valid token needs one unless
    /// it is deliberately asserting expiry itself.
    /// </summary>
    private static ITokenValidator ValidatorReturning(string claimsJson)
    {
        var validator = Substitute.For<ITokenValidator>();
        validator.ValidateAsync(Arg.Any<string>(), Arg.Any<IssuerConfig>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TokenValidationOutcome.Valid(ClaimsAccessor.Parse(WithDefaultExp(claimsJson)))));
        return validator;
    }

    private static string WithDefaultExp(string claimsJson)
    {
        using var document = System.Text.Json.JsonDocument.Parse(claimsJson);
        if (document.RootElement.TryGetProperty("exp", out _))
        {
            return claimsJson;
        }

        var defaultExp = FixedNow.ToUnixTimeSeconds() + 300;
        var merged = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            merged[property.Name] = property.Value.Clone();
        }

        merged["exp"] = defaultExp;
        return System.Text.Json.JsonSerializer.Serialize(merged);
    }

    private static ClaimMatch MatchOf(string claimPath, string value) =>
        new(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { [claimPath] = [value] });

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
