// -----------------------------------------------------------------------
// <copyright file="AllowInsecureHttpTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Primitives;
using AuthCallout.Identity.Jwks;
using AuthCallout.Identity.Tests.TestIdp;

namespace AuthCallout.Identity.Tests.Jwks;

/// <summary>
/// <c>jwks.allowInsecureHttp</c> (issue #2) against a real plain-HTTP issuer (the in-proc
/// <see cref="StubIdentityProvider"/> listens on <c>http://127.0.0.1</c>), through the
/// production construction path (no injected <see cref="HttpClient"/>): off, the fetch is
/// refused exactly as before; on, discovery and JWKS fetch and prewarm succeed.
/// </summary>
public sealed class AllowInsecureHttpTests : IDisposable
{
    private readonly StubIdentityProvider _idp = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Factory_DefaultTransport_HttpIssuer_OnlyFetchesWhenAllowed(bool useJwksUriOverride)
    {
        var issuer = JwksTestBuilders.IssuerFor(_idp, useJwksUriOverride);

        var strict = new IssuerConfigurationManagerFactory().Create(issuer);
        await Assert.ThrowsAnyAsync<Exception>(() => strict.FetchAsync(CancellationToken.None));

        var relaxed = new IssuerConfigurationManagerFactory(allowInsecureHttp: true).Create(issuer);
        var configuration = await relaxed.FetchAsync(CancellationToken.None);
        Assert.NotEmpty(configuration.SigningKeys);
    }

    [Fact]
    public async Task JwksCacheManager_AllowInsecureHttpFalse_HttpIssuerNeverWarms()
    {
        using var manager = BuildManager(allowInsecureHttp: false);

        // A failed fetch is recorded as an unhealthy issuer, not thrown (IssuerJwksState).
        await manager.PrewarmAllAsync(CancellationToken.None);

        Assert.Empty(manager.GetSigningKeys(_idp.Authority));
        Assert.False(Assert.Single(manager.Health).HasUsableKeys);
        Assert.Equal(0, _idp.DiscoveryFetchCount);
    }

    [Fact]
    public async Task JwksCacheManager_AllowInsecureHttpTrue_HttpIssuerPrewarms()
    {
        using var manager = BuildManager(allowInsecureHttp: true);

        await manager.PrewarmAllAsync(CancellationToken.None);

        Assert.NotEmpty(manager.GetSigningKeys(_idp.Authority));
        Assert.True(Assert.Single(manager.Health).IsHealthy);
    }

    public void Dispose() => _idp.Dispose();

    private JwksCacheManager BuildManager(bool allowInsecureHttp)
    {
        var configSource = new FakeActiveConfigSource();
        var issuer = JwksTestBuilders.IssuerFor(_idp);
        configSource.SetCurrent(JwksTestBuilders.MinimalBootstrap(), JwksTestBuilders.RoutingFor(JwksTestBuilders.ApplicationFor(issuer)));
        var settings = JwksTestBuilders.DefaultJwksSettings(fetchTimeout: DurationString.Parse("2s")) with { AllowInsecureHttp = allowInsecureHttp };
        return new JwksCacheManager(configSource, settings);
    }
}
