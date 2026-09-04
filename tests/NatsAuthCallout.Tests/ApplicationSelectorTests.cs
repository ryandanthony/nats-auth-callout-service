// -----------------------------------------------------------------------
// <copyright file="ApplicationSelectorTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using NatsAuthCallout.Authorization;
using NatsAuthCallout.Configuration;
using NatsAuthCallout.Routing;

namespace NatsAuthCallout.Tests;

public sealed class ApplicationSelectorTests
{
    private static ApplicationSelector Build(string? defaultApp = null)
    {
        var time = new FakeTimeProvider();
        var appA = TestApplications.Household("app-a", "ACCOUNT_A");
        appA.Routing.ServiceAccountNKeys = ["UNKEY_A"];
        appA.Routing.ConnectUsernames = ["app-a"];
        appA.Routing.TokenIssuers = ["https://issuer.example/realms/app-a"];

        var appB = TestApplications.Household("app-b", "ACCOUNT_B");
        appB.Routing.ServiceAccountNKeys = ["UNKEY_B"];
        appB.Routing.ConnectUsernames = ["app-b"];
        appB.Routing.TokenIssuers = ["https://issuer.example/realms/app-b"];

        var authorizers = new List<UserAuthorizer>
        {
            new(appA, new FakeTokenValidator(), time),
            new(appB, new FakeTokenValidator(), time),
        };

        return new ApplicationSelector(authorizers, defaultApp);
    }

    [Fact]
    public void ServiceAccountNKey_RoutesToOwningApplication()
    {
        var selector = Build();

        var chosen = selector.Select("UNKEY_B", connectUsername: null, bearerToken: null);

        Assert.Equal("app-b", chosen!.Application.Name);
    }

    [Fact]
    public void ConnectUsername_RoutesToMatchingApplication()
    {
        var selector = Build();

        var chosen = selector.Select("UUSER", connectUsername: "app-a", bearerToken: null);

        Assert.Equal("app-a", chosen!.Application.Name);
    }

    [Fact]
    public void TokenIssuer_RoutesToMatchingApplication()
    {
        var selector = Build();
        var token = JwtFactory.Create(issuer: "https://issuer.example/realms/app-b");

        var chosen = selector.Select("UUSER", connectUsername: null, bearerToken: token);

        Assert.Equal("app-b", chosen!.Application.Name);
    }

    [Fact]
    public void NoMatch_WithoutDefault_ReturnsNull()
    {
        var selector = Build();

        var chosen = selector.Select("UUSER", connectUsername: null, bearerToken: null);

        Assert.Null(chosen);
    }

    [Fact]
    public void NoMatch_WithDefault_ReturnsDefaultApplication()
    {
        var selector = Build(defaultApp: "app-a");

        var chosen = selector.Select("UUSER", connectUsername: null, bearerToken: null);

        Assert.Equal("app-a", chosen!.Application.Name);
    }

    [Fact]
    public void ServiceAccount_TakesPrecedenceOverUsername()
    {
        var selector = Build();

        // NKey belongs to app-b, username points at app-a: NKey wins.
        var chosen = selector.Select("UNKEY_B", connectUsername: "app-a", bearerToken: null);

        Assert.Equal("app-b", chosen!.Application.Name);
    }

    [Fact]
    public void UnknownDefault_Throws()
    {
        var time = new FakeTimeProvider();
        var authorizers = new List<UserAuthorizer>
        {
            new(TestApplications.Household("app-a"), new FakeTokenValidator(), time),
        };

        Assert.Throws<InvalidOperationException>(
            () => new ApplicationSelector(authorizers, "does-not-exist"));
    }

    [Fact]
    public void TryReadIssuer_ReturnsNull_ForGarbage()
    {
        Assert.Null(ApplicationSelector.TryReadIssuer("not-a-jwt"));
        Assert.Null(ApplicationSelector.TryReadIssuer(null));
        Assert.Null(ApplicationSelector.TryReadIssuer(string.Empty));
    }

    private static class JwtFactory
    {
        public static string Create(string issuer)
        {
            var handler = new JsonWebTokenHandler();
            var key = new SymmetricSecurityKey(new byte[32]);
            return handler.CreateToken(new SecurityTokenDescriptor
            {
                Issuer = issuer,
                SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
            });
        }
    }
}
