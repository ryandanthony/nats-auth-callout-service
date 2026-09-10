// -----------------------------------------------------------------------
// <copyright file="EventTestBuilders.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Primitives;

namespace AuthCallout.Nats.Tests.Events;

/// <summary>
/// Minimal, valid builders for the config types P09 tests need (same pattern as P06's
/// <c>JwksTestBuilders</c>).
/// </summary>
internal static class EventTestBuilders
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

    public static Application ApplicationWithEvents(string name, EventSubjects? events, IssuerConfig? issuer = null) => new(
        Name: name,
        TargetAccount: "ACCT",
        Issuer: issuer ?? MinimalIssuer(),
        Match: null,
        AnonymousProfile: null,
        Profiles: [],
        IdentityPropagation: null,
        Events: events);

    public static RoutingConfig RoutingFor(params Application[] applications) => new(applications);

    public static BootstrapConfig BootstrapWithAnonymousEvents(EventSubjects? anonymousEvents)
    {
        var source = SourceConfig.Files(
            new FilesSourceSettings("/config/apps"), DurationString.Parse("60s"), DurationString.Parse("15m"), DurationString.Parse("5s"));

        return new BootstrapConfig(
            NatsUrl: "nats://nats:4222",
            IssuerSeed: SecretRef.Inline("issuer-seed"),
            ServiceUserSeed: null,
            XkeySeed: null,
            AssertionKeys: null,
            Source: source,
            Jwks: new JwksSettings(DurationString.Parse("5m"), DurationString.Parse("5s"), PrewarmRequired: true),
            Concurrency: new ConcurrencySettings(MaxDegreeOfParallelism: 32, PerRequestTimeout: DurationString.Parse("1500ms"), QueueCapacity: null),
            Anonymous: new AnonymousGlobalConfig(TargetAccount: "ANON", Expiry: DurationString.Parse("5m"), Events: anonymousEvents));
    }

    public static ActiveConfig ActiveConfigFor(BootstrapConfig bootstrap, RoutingConfig routing, long version = 1) =>
        new(bootstrap, routing, version);
}
