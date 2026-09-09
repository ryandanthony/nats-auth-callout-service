// -----------------------------------------------------------------------
// <copyright file="StubIdentityProviderTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;

namespace AuthCallout.Identity.Tests.TestIdp;

/// <summary>Sanity tests for <see cref="StubIdentityProvider"/> itself: the fixture must be trustworthy before it grounds every other test in this packet.</summary>
public sealed class StubIdentityProviderTests
{
    [Fact]
    public async Task DiscoveryDocument_ServesIssuerAndJwksUri()
    {
        using var idp = new StubIdentityProvider();
        using var client = new HttpClient();

        var response = await client.GetFromJsonAsync<Dictionary<string, string>>(new Uri(idp.DiscoveryUrl));

        Assert.NotNull(response);
        Assert.Equal(idp.Authority, response["issuer"]);
        Assert.Equal(idp.JwksUrl, response["jwks_uri"]);
    }

    [Fact]
    public async Task Jwks_ServesAtLeastOneKey()
    {
        using var idp = new StubIdentityProvider();
        using var client = new HttpClient();

        var json = await client.GetStringAsync(new Uri(idp.JwksUrl));

        Assert.Contains("\"kid\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void IssueToken_ProducesTokenWithIssuerAndKid()
    {
        using var idp = new StubIdentityProvider();

        var raw = idp.IssueToken();
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(raw);

        Assert.Equal(idp.Authority, jwt.Issuer);
        Assert.Equal("kid-1", jwt.Header.Kid);
        Assert.Equal("RS256", jwt.Header.Alg);
    }

    [Fact]
    public void IssueToken_CustomClaims_AppearInPayload()
    {
        using var idp = new StubIdentityProvider();

        var raw = idp.IssueToken(claims: new Dictionary<string, object>(StringComparer.Ordinal) { ["sub"] = "user-1" });
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(raw);

        Assert.Equal("user-1", jwt.Payload["sub"]);
    }

    [Fact]
    public void AddKey_NewToken_SignsWithNewKid()
    {
        using var idp = new StubIdentityProvider();
        idp.AddKey("kid-2");

        var raw = idp.IssueToken();
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(raw);

        Assert.Equal("kid-2", jwt.Header.Kid);
    }

    [Fact]
    public void IssueToken_ExplicitSigningKid_UsesThatKey()
    {
        using var idp = new StubIdentityProvider();
        idp.AddKey("kid-2");

        var raw = idp.IssueToken(signingKid: "kid-1");
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(raw);

        Assert.Equal("kid-1", jwt.Header.Kid);
    }

    [Fact]
    public void IssueAlgorithmConfusionToken_ProducesHs256TokenWithRealKid()
    {
        using var idp = new StubIdentityProvider();

        var raw = idp.IssueAlgorithmConfusionToken();
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(raw);

        Assert.Equal("HS256", jwt.Header.Alg);
        Assert.Equal("kid-1", jwt.Header.Kid);
    }

    [Fact]
    public async Task DiscoveryFetchCount_IncrementsPerFetch()
    {
        using var idp = new StubIdentityProvider();
        using var client = new HttpClient();

        Assert.Equal(0, idp.DiscoveryFetchCount);
        await client.GetStringAsync(new Uri(idp.DiscoveryUrl));
        await client.GetStringAsync(new Uri(idp.DiscoveryUrl));

        Assert.Equal(2, idp.DiscoveryFetchCount);
    }

    [Fact]
    public async Task JwksFetchCount_IncrementsPerFetch()
    {
        using var idp = new StubIdentityProvider();
        using var client = new HttpClient();

        await client.GetStringAsync(new Uri(idp.JwksUrl));

        Assert.Equal(1, idp.JwksFetchCount);
    }
}
