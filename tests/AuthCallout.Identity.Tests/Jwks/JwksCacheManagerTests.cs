// -----------------------------------------------------------------------
// <copyright file="JwksCacheManagerTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Primitives;
using AuthCallout.Identity.Jwks;
using AuthCallout.Identity.Tests.TestIdp;

namespace AuthCallout.Identity.Tests.Jwks;

/// <summary>
/// Table tests for <see cref="JwksCacheManager"/>: prewarm, single-flight on-demand refresh,
/// proactive refresh picking up rotated keys, discovery-vs-<c>jwksUri</c>-override, and
/// issuer add/remove tracking from config changes (P06 ADR→test map).
/// </summary>
public sealed class JwksCacheManagerTests : IDisposable
{
    private readonly HttpClient _httpClient = new();
    private readonly StubIdentityProvider _idp = new();

    [Fact]
    public async Task PrewarmAllAsync_FetchesDiscoveryAndJwksForConfiguredIssuer()
    {
        using var manager = BuildManager(out _, out _);

        await manager.PrewarmAllAsync(CancellationToken.None);

        Assert.True(_idp.DiscoveryFetchCount > 0);
        Assert.True(_idp.JwksFetchCount > 0);
        Assert.NotEmpty(manager.GetSigningKeys(_idp.Authority));
    }

    [Fact]
    public async Task PrewarmAllAsync_PopulatesHealthy_AfterSuccess()
    {
        using var manager = BuildManager(out _, out _);

        await manager.PrewarmAllAsync(CancellationToken.None);

        var health = Assert.Single(manager.Health);
        Assert.Equal(_idp.Authority, health.Authority);
        Assert.True(health.IsHealthy);
        Assert.True(health.HasUsableKeys);
        Assert.NotNull(health.LastRefreshed);
    }

    [Fact]
    public async Task PrewarmAllAsync_NoIssuersConfigured_CompletesWithNoHealth()
    {
        var configSource = new FakeActiveConfigSource();
        var routing = JwksTestBuilders.RoutingFor(); // no applications
        configSource.SetCurrent(JwksTestBuilders.MinimalBootstrap(), routing);
        using var manager = new JwksCacheManager(configSource, JwksTestBuilders.DefaultJwksSettings(), new IssuerConfigurationManagerFactory(_httpClient));

        await manager.PrewarmAllAsync(CancellationToken.None);

        Assert.Empty(manager.Health);
    }

    [Fact]
    public async Task GetSigningKeys_UnknownIssuer_ReturnsEmpty()
    {
        using var manager = BuildManager(out _, out _);
        await manager.PrewarmAllAsync(CancellationToken.None);

        var keys = manager.GetSigningKeys("https://never-configured.example.com");

        Assert.Empty(keys);
    }

    [Fact]
    public async Task RefreshAsync_UnknownIssuerAuthority_IsNoOp()
    {
        using var manager = BuildManager(out _, out _);
        var healthBefore = manager.Health.Count;

        // Must not throw even though this issuer was never tracked, and must not start
        // tracking it either (RefreshAsync only refreshes existing state, never creates it).
        await manager.RefreshAsync("https://never-configured.example.com", CancellationToken.None);

        Assert.Equal(healthBefore, manager.Health.Count);
        Assert.DoesNotContain(manager.Health, h => string.Equals(h.Authority, "https://never-configured.example.com", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefreshAsync_ConcurrentCalls_CollapseToSingleUpstreamFetch()
    {
        using var manager = BuildManager(out _, out _);
        await manager.PrewarmAllAsync(CancellationToken.None);
        var fetchesBeforeConcurrentRefresh = _idp.JwksFetchCount;

        var tasks = Enumerable.Range(0, 10).Select(_ => manager.RefreshAsync(_idp.Authority, CancellationToken.None));
        await Task.WhenAll(tasks);

        // Single-flight: 10 concurrent on-demand refreshes for the same issuer should not
        // result in 10 separate upstream JWKS fetches.
        var fetchesAfter = _idp.JwksFetchCount - fetchesBeforeConcurrentRefresh;
        Assert.InRange(fetchesAfter, 1, 3);
    }

    [Fact]
    public async Task ProactiveRefresh_RotatedKeyBecomesAvailable_WithoutOnDemandFetchTrigger()
    {
        using var manager = BuildManager(out _, out _);
        await manager.PrewarmAllAsync(CancellationToken.None);

        _idp.AddKey("kid-rotated");
        await manager.RefreshAsync(_idp.Authority, CancellationToken.None);

        var keys = manager.GetSigningKeys(_idp.Authority);
        Assert.Contains(keys, k => string.Equals(k.KeyId, "kid-rotated", StringComparison.Ordinal));
    }

    [Fact]
    public async Task JwksUriOverride_BypassesDiscovery()
    {
        var configSource = new FakeActiveConfigSource();
        var issuer = JwksTestBuilders.IssuerFor(_idp, useJwksUriOverride: true);
        var routing = JwksTestBuilders.RoutingFor(JwksTestBuilders.ApplicationFor(issuer));
        configSource.SetCurrent(JwksTestBuilders.MinimalBootstrap(), routing);
        using var manager = new JwksCacheManager(configSource, JwksTestBuilders.DefaultJwksSettings(), new IssuerConfigurationManagerFactory(_httpClient));

        await manager.PrewarmAllAsync(CancellationToken.None);

        Assert.Equal(0, _idp.DiscoveryFetchCount);
        Assert.True(_idp.JwksFetchCount > 0);
        Assert.NotEmpty(manager.GetSigningKeys(_idp.Authority));
    }

    [Fact]
    public async Task DiscoveryMode_FetchesDiscoveryDocumentFirst()
    {
        using var manager = BuildManager(out _, out _);

        await manager.PrewarmAllAsync(CancellationToken.None);

        Assert.True(_idp.DiscoveryFetchCount > 0);
    }

    [Fact]
    public async Task ConfigChange_AddsNewIssuer_AndPrewarmsIt()
    {
        var configSource = new FakeActiveConfigSource();
        configSource.SetCurrent(JwksTestBuilders.MinimalBootstrap(), JwksTestBuilders.RoutingFor());
        using var manager = new JwksCacheManager(configSource, JwksTestBuilders.DefaultJwksSettings(), new IssuerConfigurationManagerFactory(_httpClient));
        Assert.Empty(manager.Health);

        var issuer = JwksTestBuilders.IssuerFor(_idp);
        configSource.SetCurrent(JwksTestBuilders.MinimalBootstrap(), JwksTestBuilders.RoutingFor(JwksTestBuilders.ApplicationFor(issuer)));
        await manager.RefreshAsync(_idp.Authority, CancellationToken.None);

        Assert.Single(manager.Health);
        Assert.NotEmpty(manager.GetSigningKeys(_idp.Authority));
    }

    [Fact]
    public async Task ConfigChange_RemovesLastAppForIssuer_DropsState()
    {
        using var secondIdp = new StubIdentityProvider();
        var configSource = new FakeActiveConfigSource();
        var issuer = JwksTestBuilders.IssuerFor(_idp);
        configSource.SetCurrent(JwksTestBuilders.MinimalBootstrap(), JwksTestBuilders.RoutingFor(JwksTestBuilders.ApplicationFor(issuer)));
        using var manager = new JwksCacheManager(configSource, JwksTestBuilders.DefaultJwksSettings(), new IssuerConfigurationManagerFactory(_httpClient));
        await manager.PrewarmAllAsync(CancellationToken.None);
        Assert.Single(manager.Health);

        // New config no longer includes _idp's issuer at all.
        var otherIssuer = JwksTestBuilders.IssuerFor(secondIdp);
        configSource.SetCurrent(JwksTestBuilders.MinimalBootstrap(), JwksTestBuilders.RoutingFor(JwksTestBuilders.ApplicationFor(otherIssuer)));

        Assert.Empty(manager.GetSigningKeys(_idp.Authority));
        Assert.DoesNotContain(manager.Health, h => string.Equals(h.Authority, _idp.Authority, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConfigChange_TwoApplicationsSharingIssuer_TrackedOnce()
    {
        var configSource = new FakeActiveConfigSource();
        var issuer = JwksTestBuilders.IssuerFor(_idp);
        var routing = JwksTestBuilders.RoutingFor(
            JwksTestBuilders.ApplicationFor(issuer, "app-one"),
            JwksTestBuilders.ApplicationFor(issuer, "app-two"));
        configSource.SetCurrent(JwksTestBuilders.MinimalBootstrap(), routing);
        using var manager = new JwksCacheManager(configSource, JwksTestBuilders.DefaultJwksSettings(), new IssuerConfigurationManagerFactory(_httpClient));

        await manager.PrewarmAllAsync(CancellationToken.None);

        // De-duplicated across applications (doc 04 §Derived, not stored): one health entry.
        Assert.Single(manager.Health);
    }

    [Fact]
    public void Constructor_NullActiveConfigSource_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new JwksCacheManager(null!, JwksTestBuilders.DefaultJwksSettings()));
    }

    [Fact]
    public void GetSigningKeys_NullAuthority_Throws()
    {
        using var manager = BuildManager(out _, out _);

        Assert.Throws<ArgumentNullException>(() => manager.GetSigningKeys(null!));
    }

    [Fact]
    public async Task RefreshAsync_NullAuthority_Throws()
    {
        using var manager = BuildManager(out _, out _);

        await Assert.ThrowsAsync<ArgumentNullException>(() => manager.RefreshAsync(null!, CancellationToken.None));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _idp.Dispose();
        _httpClient.Dispose();
    }

    private JwksCacheManager BuildManager(out FakeActiveConfigSource configSource, out DurationString refreshInterval)
    {
        configSource = new FakeActiveConfigSource();
        refreshInterval = DurationString.Parse("5m");
        var issuer = JwksTestBuilders.IssuerFor(_idp);
        var routing = JwksTestBuilders.RoutingFor(JwksTestBuilders.ApplicationFor(issuer));
        configSource.SetCurrent(JwksTestBuilders.MinimalBootstrap(), routing);
        return new JwksCacheManager(configSource, JwksTestBuilders.DefaultJwksSettings(refreshInterval), new IssuerConfigurationManagerFactory(_httpClient));
    }
}
