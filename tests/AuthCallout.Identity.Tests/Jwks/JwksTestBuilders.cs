// -----------------------------------------------------------------------
// <copyright file="JwksTestBuilders.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Primitives;
using AuthCallout.Identity.Tests.TestIdp;

namespace AuthCallout.Identity.Tests.Jwks;

/// <summary>Minimal, valid builders for the config types P06 tests need, pointed at a <see cref="StubIdentityProvider"/>.</summary>
internal static class JwksTestBuilders
{
    public static IssuerConfig IssuerFor(
        StubIdentityProvider idp,
        bool useJwksUriOverride = false,
        bool validateAudience = false,
        IReadOnlyList<string>? audience = null,
        DurationString? clockSkew = null,
        IReadOnlyList<string>? allowedAlgorithms = null,
        DurationString? refreshInterval = null) => new(
        Authority: idp.Authority,
        JwksUri: useJwksUriOverride ? idp.JwksUrl : null,
        JwksCache: DurationString.Parse("5m"),
        RefreshInterval: refreshInterval,
        ValidateAudience: validateAudience,
        Audience: audience,
        ClockSkew: clockSkew ?? DurationString.Parse("60s"),
        AllowedAlgorithms: allowedAlgorithms ?? ["RS256"]);

    public static Application ApplicationFor(IssuerConfig issuer, string name = "app") => new(
        Name: name,
        TargetAccount: "ACCT",
        Issuer: issuer,
        Match: null,
        AnonymousProfile: null,
        Profiles: [],
        IdentityPropagation: null,
        Events: null);

    public static RoutingConfig RoutingFor(params Application[] applications) => new(applications);

    public static JwksSettings DefaultJwksSettings(DurationString? refreshInterval = null, DurationString? fetchTimeout = null) =>
        new(refreshInterval ?? DurationString.Parse("5m"), fetchTimeout ?? DurationString.Parse("5s"), PrewarmRequired: true);

    public static BootstrapConfig MinimalBootstrap(SourceConfig? source = null)
    {
        var effectiveSource = source ?? SourceConfig.Files(
            new FilesSourceSettings("/config/apps"), DurationString.Parse("60s"), DurationString.Parse("15m"), DurationString.Parse("5s"));

        return new BootstrapConfig(
            NatsUrl: "nats://nats:4222",
            IssuerSeed: SecretRef.Inline("issuer-seed"),
            ServiceUserSeed: null,
            XkeySeed: null,
            AssertionKeys: null,
            Source: effectiveSource,
            Jwks: DefaultJwksSettings(),
            Concurrency: new ConcurrencySettings(MaxDegreeOfParallelism: 32, PerRequestTimeout: DurationString.Parse("1500ms"), QueueCapacity: null),
            Anonymous: new AnonymousGlobalConfig(TargetAccount: "ANON", Expiry: DurationString.Parse("5m"), Events: null));
    }
}
