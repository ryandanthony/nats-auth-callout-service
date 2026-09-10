// -----------------------------------------------------------------------
// <copyright file="CalloutTestBuilders.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Decisions;
using AuthCallout.Contracts.Primitives;
using NATS.Jwt;
using NATS.Jwt.Models;
using NATS.NKeys;

namespace AuthCallout.Nats.Tests.Callout;

/// <summary>Minimal, valid builders for the config/decision/request types P07 tests need (same pattern as P06/P09's test builders).</summary>
internal static class CalloutTestBuilders
{
    /// <summary>A minimal, valid granted decision for an authenticated application grant. Override individual fields with a <c>with</c> expression.</summary>
    public static GrantedDecision Granted() => new(
        Application: "notes",
        Principal: "user@example.com",
        Issuer: "https://id.example.com",
        TargetAccount: "NOTES",
        Name: "user@example.com",
        Expiry: DateTimeOffset.UtcNow.AddMinutes(5),
        Publish: ["notes.>"],
        Subscribe: ["notes.>"],
        ProfileName: "editor",
        IdentityPropagationEnabled: false,
        SelfLookupKey: null);

    /// <summary>A minimal, valid anonymous granted decision.</summary>
    public static GrantedDecision AnonymousGranted() => new(
        Application: null,
        Principal: null,
        Issuer: null,
        TargetAccount: "ANON",
        Name: "anonymous",
        Expiry: DateTimeOffset.UtcNow.AddMinutes(5),
        Publish: [],
        Subscribe: [],
        ProfileName: null,
        IdentityPropagationEnabled: false,
        SelfLookupKey: null);

    public static DeniedDecision Denied(
        string? application = null,
        string? principal = null,
        string? issuer = null,
        DenyReason reason = DenyReason.NoProfileMatch,
        string? detail = null) => new(application, principal, issuer, reason, detail);

    public static IssuerConfig MinimalIssuer(string authority = "https://id.example.com/realms/main") => new(
        Authority: authority,
        JwksUri: null,
        JwksCache: DurationString.Parse("5m"),
        RefreshInterval: null,
        ValidateAudience: false,
        Audience: null,
        ClockSkew: DurationString.Parse("60s"),
        AllowedAlgorithms: ["RS256"]);

    public static Application ApplicationFor(string name = "notes", IssuerConfig? issuer = null) => new(
        Name: name,
        TargetAccount: "NOTES",
        Issuer: issuer ?? MinimalIssuer(),
        Match: null,
        AnonymousProfile: null,
        Profiles: [],
        IdentityPropagation: null,
        Events: null);

    public static RoutingConfig RoutingFor(params Application[] applications) => new(applications);

    public static BootstrapConfig MinimalBootstrap()
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
            Anonymous: new AnonymousGlobalConfig(TargetAccount: "ANON", Expiry: DurationString.Parse("5m"), Events: null));
    }

    public static ActiveConfig ActiveConfigFor(RoutingConfig? routing = null, BootstrapConfig? bootstrap = null, long version = 1) =>
        new(bootstrap ?? MinimalBootstrap(), routing ?? RoutingFor(), version);

    /// <summary>Generates a throwaway user NKey public key, disposing the backing key pair immediately (callers never need the private half).</summary>
    public static string NewUserPublicKey()
    {
        using var keyPair = KeyPair.CreatePair(PrefixByte.User);
        return keyPair.GetPublicKey();
    }

    /// <summary>Builds a raw (self-signed by a throwaway server key) NATS authorization-request JWT payload, as bytes.</summary>
    public static byte[] BuildRequestPayload(RequestPayloadOptions? options = null)
    {
        var effectiveOptions = options ?? new RequestPayloadOptions();
        using var serverKp = KeyPair.CreatePair(PrefixByte.Server);
        var effectiveUserNKey = effectiveOptions.UserNKey ?? NewUserPublicKey();

        var claims = NatsJwt.NewAuthorizationRequestClaims(serverKp.GetPublicKey());
        claims.Audience = "nats-authorization-request";
        claims.AuthorizationRequest.UserNKey = effectiveUserNKey;
        claims.AuthorizationRequest.NatsServer = new NatsServerId { Id = effectiveOptions.ServerId, Name = "n1" };
        claims.AuthorizationRequest.NatsConnectOptions = new NatsConnectOptions { Token = effectiveOptions.RawToken ?? string.Empty };
        claims.AuthorizationRequest.NatsClientInformation = new NatsClientInformation { Host = effectiveOptions.Host ?? string.Empty };

        var jwt = NatsJwt.EncodeAuthorizationRequestClaims(claims, serverKp, null);
        return System.Text.Encoding.UTF8.GetBytes(jwt);
    }
}
