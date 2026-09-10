// -----------------------------------------------------------------------
// <copyright file="HostTestBuilders.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Primitives;

namespace AuthCallout.Host.Tests;

/// <summary>Minimal, valid builders for the config types P13 tests need (same pattern as P06/P07/P09's test builders).</summary>
internal static class HostTestBuilders
{
    public static BootstrapConfig MinimalBootstrap(SourceConfig? source = null, bool prewarmRequired = true)
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
            Jwks: new JwksSettings(DurationString.Parse("5m"), DurationString.Parse("5s"), prewarmRequired),
            Concurrency: new ConcurrencySettings(MaxDegreeOfParallelism: 32, PerRequestTimeout: DurationString.Parse("1500ms"), QueueCapacity: null),
            Anonymous: new AnonymousGlobalConfig(TargetAccount: "ANON", Expiry: DurationString.Parse("5m"), Events: null));
    }

    public static RoutingConfig EmptyRouting() => new([]);

    public static ActiveConfig ActiveConfigFor(BootstrapConfig? bootstrap = null, RoutingConfig? routing = null, long version = 1) =>
        new(bootstrap ?? MinimalBootstrap(), routing ?? EmptyRouting(), version);
}
