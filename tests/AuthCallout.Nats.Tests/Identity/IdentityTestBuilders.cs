// -----------------------------------------------------------------------
// <copyright file="IdentityTestBuilders.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Decisions;
using AuthCallout.Contracts.Primitives;

namespace AuthCallout.Nats.Tests.Identity;

/// <summary>
/// Minimal, valid builders for the config/decision types P08's <c>AuthCallout.Nats.Identity</c>
/// tests need (same pattern as P09's <c>EventTestBuilders</c>).
/// </summary>
internal static class IdentityTestBuilders
{
    public static IssuerConfig MinimalIssuer(string authority = "https://id.example.com/realms/main") => new(
        Authority: authority,
        JwksUri: null,
        JwksCache: DurationString.Parse("5m"),
        RefreshInterval: null,
        ValidateAudience: false,
        Audience: null,
        ClockSkew: DurationString.Parse("60s"),
        AllowedAlgorithms: ["RS256"]);

    public static Application ApplicationWithPropagation(string name, bool propagationEnabled) => new(
        Name: name,
        TargetAccount: "ACCT",
        Issuer: MinimalIssuer(),
        Match: null,
        AnonymousProfile: null,
        Profiles: [],
        IdentityPropagation: new IdentityPropagationConfig(propagationEnabled),
        Events: null);

    public static RoutingConfig RoutingFor(params Application[] applications) => new(applications);

    public static BootstrapConfig BootstrapWithAssertionKeys(IReadOnlyList<AssertionKey>? assertionKeys)
    {
        var source = SourceConfig.Files(
            new FilesSourceSettings("/config/apps"), DurationString.Parse("60s"), DurationString.Parse("15m"), DurationString.Parse("5s"));

        return new BootstrapConfig(
            NatsUrl: "nats://nats:4222",
            IssuerSeed: SecretRef.Inline("issuer-seed"),
            ServiceUserSeed: null,
            XkeySeed: null,
            AssertionKeys: assertionKeys,
            Source: source,
            Jwks: new JwksSettings(DurationString.Parse("5m"), DurationString.Parse("5s"), PrewarmRequired: true),
            Concurrency: new ConcurrencySettings(MaxDegreeOfParallelism: 32, PerRequestTimeout: DurationString.Parse("1500ms"), QueueCapacity: null),
            Anonymous: new AnonymousGlobalConfig(TargetAccount: "ANON", Expiry: DurationString.Parse("5m"), Events: null));
    }

    public static ActiveConfig ActiveConfigFor(BootstrapConfig bootstrap, RoutingConfig routing, long version = 1) =>
        new(bootstrap, routing, version);

    public static GrantedDecision GrantFor(
        string application,
        bool propagationEnabled,
        string? selfLookupKey,
        DateTimeOffset expiry,
        string principal = "user-1",
        string issuer = "https://id.example.com/realms/main") => new(
        Application: application,
        Principal: principal,
        Issuer: issuer,
        TargetAccount: "ACCT",
        Name: "user",
        Expiry: expiry,
        Publish: [],
        Subscribe: [],
        ProfileName: "editor",
        IdentityPropagationEnabled: propagationEnabled,
        SelfLookupKey: selfLookupKey);
}
