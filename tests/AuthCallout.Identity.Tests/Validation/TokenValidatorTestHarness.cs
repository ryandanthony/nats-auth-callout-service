// -----------------------------------------------------------------------
// <copyright file="TokenValidatorTestHarness.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using AuthCallout.Identity.Jwks;
using AuthCallout.Identity.Tests.Jwks;
using AuthCallout.Identity.Tests.TestIdp;
using AuthCallout.Identity.Validation;

namespace AuthCallout.Identity.Tests.Validation;

/// <summary>
/// Wires a real <see cref="TokenValidator"/> against a real <see cref="JwksCacheManager"/> and
/// a <see cref="StubIdentityProvider"/> — no <c>IJwksKeyProvider</c>/<c>IJwksCacheManager</c>
/// substitutes here, since P06's own JWKS half is what actually resolves keys by <c>kid</c>
/// and this packet's own single-flight/unknown-kid path is part of what these tests verify
/// end to end.
/// </summary>
internal sealed class TokenValidatorTestHarness : IDisposable
{
    private readonly HttpClient _httpClient = new();
    private readonly FakeActiveConfigSource _configSource = new();

    public TokenValidatorTestHarness()
    {
        Idp = new StubIdentityProvider();
        JwksManager = new JwksCacheManager(_configSource, JwksTestBuilders.DefaultJwksSettings(), new IssuerConfigurationManagerFactory(_httpClient));
        Validator = new TokenValidator(JwksManager, JwksManager);
    }

    public StubIdentityProvider Idp { get; }

    public JwksCacheManager JwksManager { get; }

    public TokenValidator Validator { get; }

    /// <summary>Registers <paramref name="issuer"/> in the tracked config and prewarms its JWKS.</summary>
    public async Task RegisterIssuerAsync(IssuerConfig issuer)
    {
        RegisterIssuerWithoutPrewarm(issuer);
        await JwksManager.PrewarmAllAsync(CancellationToken.None);
    }

    /// <summary>Registers <paramref name="issuer"/> in the tracked config without prewarming (test seam for the "no keys cached yet" validator path).</summary>
    public void RegisterIssuerWithoutPrewarm(IssuerConfig issuer)
    {
        var routing = JwksTestBuilders.RoutingFor(JwksTestBuilders.ApplicationFor(issuer));
        _configSource.SetCurrent(JwksTestBuilders.MinimalBootstrap(), routing);
    }

    public void Dispose()
    {
        Idp.Dispose();
        JwksManager.Dispose();
        _httpClient.Dispose();
    }
}
