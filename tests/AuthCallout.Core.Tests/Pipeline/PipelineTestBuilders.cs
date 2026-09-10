// -----------------------------------------------------------------------
// <copyright file="PipelineTestBuilders.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Decisions;
using AuthCallout.Contracts.Primitives;

namespace AuthCallout.Core.Tests.Pipeline;

/// <summary>
/// Minimal, valid builders for the canonical config model and requests, scoped to
/// <c>AuthCallout.Core.Tests.Pipeline</c> so each pipeline test only overrides the field(s)
/// it cares about (mirrors <c>AuthCallout.Contracts.Tests.TestBuilders</c>).
/// </summary>
internal static class PipelineTestBuilders
{
    internal static IssuerConfig Issuer(string authority = "https://issuer.example.com") => new(
        Authority: authority,
        JwksUri: null,
        JwksCache: DurationString.Parse("5m"),
        RefreshInterval: null,
        ValidateAudience: false,
        Audience: null,
        ClockSkew: DurationString.Parse("60s"),
        AllowedAlgorithms: ["RS256"]);

    internal static Profile Profile(
        string name = "editor",
        ClaimMatch? match = null,
        Grant? grant = null,
        string nameClaim = "sub",
        bool autoAddSessionAccess = false) => new(
        Name: name,
        Match: match ?? ClaimMatch.Empty,
        Grant: grant ?? new Grant(Publish: ["notes.>"], Subscribe: ["notes.>"]),
        NameClaim: nameClaim,
        Variables: [],
        PublishTemplates: [],
        SubscribeTemplates: [],
        AutoAddSessionAccess: autoAddSessionAccess);

    /// <summary>Builds a profile with template variables/templates, kept separate from <see cref="Profile"/> to stay within the repo's max-parameter-count analyzer rule.</summary>
    internal static Profile TemplatedProfile(
        IReadOnlyList<TemplateVariable> variables,
        IReadOnlyList<string> publishTemplates,
        IReadOnlyList<string> subscribeTemplates,
        string name = "templated",
        ClaimMatch? match = null) => new(
        Name: name,
        Match: match ?? ClaimMatch.Empty,
        Grant: Grant.Empty,
        NameClaim: "sub",
        Variables: variables,
        PublishTemplates: publishTemplates,
        SubscribeTemplates: subscribeTemplates,
        AutoAddSessionAccess: false);

    internal static Application Application(
        string name = "notes",
        IssuerConfig? issuer = null,
        ClaimMatch? match = null,
        Grant? anonymousProfile = null,
        IReadOnlyList<Profile>? profiles = null,
        IdentityPropagationConfig? identityPropagation = null) => new(
        Name: name,
        TargetAccount: "NOTES",
        Issuer: issuer ?? Issuer(),
        Match: match,
        AnonymousProfile: anonymousProfile,
        Profiles: profiles ?? [Profile()],
        IdentityPropagation: identityPropagation,
        Events: null);

    internal static RoutingConfig Routing(params Application[] applications) => new(applications);

    internal static AnonymousGlobalConfig Anonymous(string targetAccount = "ANON", string expiry = "5m") =>
        new(targetAccount, DurationString.Parse(expiry), Events: null);

    internal static BootstrapConfig Bootstrap(AnonymousGlobalConfig? anonymous = null) => new(
        NatsUrl: "nats://nats:4222",
        IssuerSeed: SecretRef.Inline("issuer-seed"),
        ServiceUserSeed: null,
        XkeySeed: null,
        AssertionKeys: null,
        Source: SourceConfig.Files(
            new FilesSourceSettings("/config/apps"),
            DurationString.Parse("60s"),
            DurationString.Parse("15m"),
            DurationString.Parse("5s")),
        Jwks: new JwksSettings(DurationString.Parse("5m"), DurationString.Parse("5s"), PrewarmRequired: true),
        Concurrency: new ConcurrencySettings(MaxDegreeOfParallelism: 64, PerRequestTimeout: DurationString.Parse("1500ms"), QueueCapacity: 256),
        Anonymous: anonymous ?? Anonymous());

    internal static ActiveConfig ActiveConfig(RoutingConfig? routing = null, BootstrapConfig? bootstrap = null, long version = 1) =>
        new(bootstrap ?? Bootstrap(), routing ?? Routing(Application()), version);

    /// <summary>Builds a raw (unsigned, "alg":"none") JWT text carrying the given claims — sufficient for Stage 1's unvalidated <c>iss</c> peek and for a substitute <see cref="Contracts.Seams.ITokenValidator"/>'s input, since real signature verification is P06's concern, not this pipeline's.</summary>
    internal static string RawToken(string? issuer = "https://issuer.example.com")
    {
        var header = Base64UrlEncode("""{"alg":"none","typ":"JWT"}""");
        var payloadJson = issuer is null ? "{}" : $$"""{"iss":"{{issuer}}"}""";
        var payload = Base64UrlEncode(payloadJson);
        return $"{header}.{payload}.";
    }

    internal static AuthRequest Request(string? rawToken = null, string userNKey = "UABCDEF") =>
        new(userNKey, rawToken, ConnectInfo.Empty, ServerId: "server-1");

    private static string Base64UrlEncode(string text) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
