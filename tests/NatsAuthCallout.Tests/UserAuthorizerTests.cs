// -----------------------------------------------------------------------
// <copyright file="UserAuthorizerTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using Microsoft.Extensions.Time.Testing;
using NatsAuthCallout.Authorization;

namespace NatsAuthCallout.Tests;

public sealed class UserAuthorizerTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static UserAuthorizer Build(FakeTokenValidator validator)
    {
        var app = TestApplications.Household();
        var time = new FakeTimeProvider(Now);
        return new UserAuthorizer(app, validator, time);
    }

    [Fact]
    public async Task ServiceAccountNKey_GetsFullAccess_WithoutTokenValidation()
    {
        var authorizer = Build(new FakeTokenValidator());

        var decision = await authorizer.AuthorizeAsync(TestApplications.ServiceNKey, bearerToken: null);

        Assert.Equal(PrincipalKind.ServiceAccount, decision.Kind);
        Assert.Equal([">"], decision.PublishAllow);
        Assert.Equal([">"], decision.SubscribeAllow);
        Assert.Null(decision.ExpiresAt);
    }

    [Fact]
    public async Task NoToken_GetsAnonymousGrant()
    {
        var authorizer = Build(new FakeTokenValidator());

        var decision = await authorizer.AuthorizeAsync("UUSER", bearerToken: null);

        Assert.Equal(PrincipalKind.Anonymous, decision.Kind);
        Assert.Contains("app.auth.commands", decision.PublishAllow);
        Assert.DoesNotContain(decision.PublishAllow, p => p.Contains("house", StringComparison.Ordinal));
        Assert.Equal(Now.AddSeconds(600), decision.ExpiresAt);
    }

    [Fact]
    public async Task NonJwtToken_GetsAnonymousGrant()
    {
        var authorizer = Build(new FakeTokenValidator());

        // NKey-style token (no dots) must not be treated as a JWT.
        var decision = await authorizer.AuthorizeAsync("UUSER", "not-a-jwt-token");

        Assert.Equal(PrincipalKind.Anonymous, decision.Kind);
    }

    [Fact]
    public async Task InvalidToken_FallsBackToAnonymous()
    {
        var validator = new FakeTokenValidator().AddInvalid("bad.jwt.token");
        var authorizer = Build(validator);

        var decision = await authorizer.AuthorizeAsync("UUSER", "bad.jwt.token");

        Assert.Equal(PrincipalKind.Anonymous, decision.Kind);
    }

    [Fact]
    public async Task ValidToken_MissingScopeClaim_FallsBackToAnonymous()
    {
        var validator = new FakeTokenValidator().AddValid(
            "valid.jwt.token",
            FakeTokenValidator.Claims(("sub", ["user-1"])));
        var authorizer = Build(validator);

        var decision = await authorizer.AuthorizeAsync("UUSER", "valid.jwt.token");

        Assert.Equal(PrincipalKind.Anonymous, decision.Kind);
    }

    [Fact]
    public async Task ValidToken_WithScope_ExpandsPerScopePermissions()
    {
        var validator = new FakeTokenValidator().AddValid(
            "valid.jwt.token",
            FakeTokenValidator.Claims(
                ("sub", ["user-1"]),
                ("household_id", ["house-A"])),
            expiresAt: Now.AddMinutes(15));
        var authorizer = Build(validator);

        var decision = await authorizer.AuthorizeAsync("UUSER", "valid.jwt.token");

        Assert.Equal(PrincipalKind.Authenticated, decision.Kind);
        Assert.Equal("user-1", decision.Name);
        Assert.Contains("app.house.house-A.commands", decision.PublishAllow);
        Assert.Contains("app.house.house-A.queries", decision.PublishAllow);
        Assert.Contains("app.house.house-A.events.>", decision.SubscribeAllow);
        Assert.Contains("app.events.>", decision.SubscribeAllow);
        // Token expiry is preferred over the fallback.
        Assert.Equal(Now.AddMinutes(15), decision.ExpiresAt);
    }

    [Fact]
    public async Task ValidToken_MultipleScopes_ExpandsForEachValue()
    {
        var validator = new FakeTokenValidator().AddValid(
            "valid.jwt.token",
            FakeTokenValidator.Claims(
                ("sub", ["user-1"]),
                ("household_id", ["house-A", "house-B"])));
        var authorizer = Build(validator);

        var decision = await authorizer.AuthorizeAsync("UUSER", "valid.jwt.token");

        Assert.Contains("app.house.house-A.commands", decision.PublishAllow);
        Assert.Contains("app.house.house-B.commands", decision.PublishAllow);
    }

    [Fact]
    public async Task ValidToken_WithoutExpiry_UsesFallbackLifetime()
    {
        var validator = new FakeTokenValidator().AddValid(
            "valid.jwt.token",
            FakeTokenValidator.Claims(
                ("sub", ["user-1"]),
                ("household_id", ["house-A"])),
            expiresAt: null);
        var authorizer = Build(validator);

        var decision = await authorizer.AuthorizeAsync("UUSER", "valid.jwt.token");

        Assert.Equal(Now.AddSeconds(300), decision.ExpiresAt);
    }

    [Fact]
    public async Task Authenticated_DoesNotGrant_OtherHouseholdSubjects()
    {
        var validator = new FakeTokenValidator().AddValid(
            "valid.jwt.token",
            FakeTokenValidator.Claims(
                ("sub", ["user-1"]),
                ("household_id", ["house-A"])));
        var authorizer = Build(validator);

        var decision = await authorizer.AuthorizeAsync("UUSER", "valid.jwt.token");

        Assert.DoesNotContain("app.house.house-B.commands", decision.PublishAllow);
        Assert.DoesNotContain("app.house.house-B.events.>", decision.SubscribeAllow);
    }
}
