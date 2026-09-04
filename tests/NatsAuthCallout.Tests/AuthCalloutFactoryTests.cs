// -----------------------------------------------------------------------
// <copyright file="AuthCalloutFactoryTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using Microsoft.Extensions.Time.Testing;
using NatsAuthCallout.Configuration;

namespace NatsAuthCallout.Tests;

public sealed class AuthCalloutFactoryTests
{
    private static AuthCalloutOptions ValidOptions()
    {
        return new AuthCalloutOptions
        {
            IssuerSeed = "SEED",
            Applications = [TestApplications.Household()],
        };
    }

    [Fact]
    public void Validate_Passes_ForValidOptions()
    {
        AuthCalloutFactory.Validate(ValidOptions());
    }

    [Fact]
    public void Validate_Throws_WhenIssuerSeedMissing()
    {
        var options = ValidOptions();
        options.IssuerSeed = string.Empty;

        Assert.Throws<InvalidOperationException>(() => AuthCalloutFactory.Validate(options));
    }

    [Fact]
    public void Validate_Throws_WhenNoApplications()
    {
        var options = ValidOptions();
        options.Applications.Clear();

        Assert.Throws<InvalidOperationException>(() => AuthCalloutFactory.Validate(options));
    }

    [Fact]
    public void Validate_Throws_OnDuplicateApplicationNames()
    {
        var options = ValidOptions();
        options.Applications.Add(TestApplications.Household());

        Assert.Throws<InvalidOperationException>(() => AuthCalloutFactory.Validate(options));
    }

    [Fact]
    public void CreateSelector_UsesInjectedValidatorFactory()
    {
        var options = ValidOptions();
        var selector = AuthCalloutFactory.CreateSelector(
            options,
            new FakeTimeProvider(),
            _ => new FakeTokenValidator());

        var chosen = selector.Select("UUSER", connectUsername: "app", bearerToken: null);
        Assert.Equal("app", chosen!.Application.Name);
    }

    [Fact]
    public void JwtValidationOptions_ResolvesJwksUri_FromAuthority()
    {
        var options = new JwtValidationOptions { Authority = "https://issuer.example/realms/app" };

        Assert.Equal(
            new Uri("https://issuer.example/realms/app/protocol/openid-connect/certs"),
            options.ResolveJwksUri());
    }

    [Fact]
    public void JwtValidationOptions_PrefersExplicitJwksUri()
    {
        var options = new JwtValidationOptions
        {
            Authority = "https://issuer.example/realms/app",
            JwksUri = "https://custom/keys",
        };

        Assert.Equal(new Uri("https://custom/keys"), options.ResolveJwksUri());
    }

    [Fact]
    public void JwtValidationOptions_Throws_WhenNoAuthorityOrJwks()
    {
        var options = new JwtValidationOptions();

        Assert.Throws<InvalidOperationException>(() => options.ResolveJwksUri());
    }
}
