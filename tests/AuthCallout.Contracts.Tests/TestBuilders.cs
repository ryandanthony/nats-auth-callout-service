// -----------------------------------------------------------------------
// <copyright file="TestBuilders.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Primitives;

namespace AuthCallout.Contracts.Tests;

/// <summary>
/// Minimal, valid builders for the canonical config model, used across validator and
/// round-trip tests so each test only overrides the field(s) it cares about.
/// </summary>
internal static class TestBuilders
{
    public static IssuerConfig ValidIssuer(string authority = "https://id.example.com/realms/main") => new(
        Authority: authority,
        JwksUri: null,
        JwksCache: DurationString.Parse("5m"),
        RefreshInterval: null,
        ValidateAudience: true,
        Audience: ["nats"],
        ClockSkew: DurationString.Parse("60s"),
        AllowedAlgorithms: ["RS256"]);

    public static Profile ValidProfile(string name = "editor", bool autoAddSessionAccess = false) => new(
        Name: name,
        Match: new ClaimMatch(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["role"] = ["editor"] }),
        Grant: new Grant(Publish: ["notes.>"], Subscribe: ["notes.>"]),
        NameClaim: "sub",
        Variables: [],
        PublishTemplates: [],
        SubscribeTemplates: [],
        AutoAddSessionAccess: autoAddSessionAccess);

    public static Application ValidApplication(
        string name = "notes",
        IssuerConfig? issuer = null,
        ClaimMatch? match = null,
        IReadOnlyList<Profile>? profiles = null,
        IdentityPropagationConfig? identityPropagation = null) => new(
        Name: name,
        TargetAccount: "NOTES",
        Issuer: issuer ?? ValidIssuer(),
        Match: match,
        AnonymousProfile: null,
        Profiles: profiles ?? [ValidProfile()],
        IdentityPropagation: identityPropagation,
        Events: null);

    public static RoutingConfig ValidRouting(params Application[] applications) =>
        new(applications.Length == 0 ? [ValidApplication()] : applications);

    public static AssertionKey ValidAssertionKey(string kid = "2026-09-06a", bool active = true) => new(
        Kid: kid,
        Key: SecretRef.Inline("test-seed-value"),
        Active: active);

    public static BootstrapConfig ValidBootstrap(
        SourceConfig? source = null,
        IReadOnlyList<AssertionKey>? assertionKeys = null) => new(
        NatsUrl: "nats://nats:4222",
        IssuerSeed: SecretRef.Inline("issuer-seed"),
        ServiceUserSeed: null,
        XkeySeed: null,
        AssertionKeys: assertionKeys,
        Source: source ?? ValidFilesSource(),
        Jwks: new JwksSettings(DurationString.Parse("5m"), DurationString.Parse("5s"), PrewarmRequired: true),
        Concurrency: new ConcurrencySettings(MaxDegreeOfParallelism: 64, PerRequestTimeout: DurationString.Parse("1500ms"), QueueCapacity: 256),
        Anonymous: new AnonymousGlobalConfig(TargetAccount: "ANON", Expiry: DurationString.Parse("5m"), Events: null));

    public static SourceConfig ValidFilesSource() => SourceConfig.Files(
        new FilesSourceSettings("/config/apps"),
        DurationString.Parse("60s"),
        DurationString.Parse("15m"),
        DurationString.Parse("5s"));
}
