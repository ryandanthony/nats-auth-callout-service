// -----------------------------------------------------------------------
// <copyright file="CalloutWolverineOptionsTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Tokens;
using AuthCallout.Client.Wolverine;
using AuthCallout.Contracts.Primitives;

namespace AuthCallout.Client.Wolverine.Tests;

/// <summary>
/// Tests for <see cref="CalloutWolverineOptions"/>: <see cref="CalloutWolverineOptions.UseClientCredentials"/>
/// wires a <see cref="ClientCredentialsTokenProvider"/> into <see cref="CalloutWolverineOptions.TokenProvider"/>.
/// </summary>
public sealed class CalloutWolverineOptionsTests
{
    [Fact]
    public void UseClientCredentials_SetsTokenProvider()
    {
        var options = new CalloutWolverineOptions();

        options.UseClientCredentials(cc =>
        {
            cc.Authority = new Uri("https://idp.example.com/realms/dev");
            cc.ClientId = "notes-worker";
            cc.ClientSecret = SecretRef.Inline("secret");
        });

        Assert.NotNull(options.TokenProvider);
        Assert.IsType<ClientCredentialsTokenProvider>(options.TokenProvider);
    }

    [Fact]
    public void UseClientCredentials_NullConfigure_Throws()
    {
        var options = new CalloutWolverineOptions();

        Assert.Throws<ArgumentNullException>(() => options.UseClientCredentials(null!));
    }

    [Fact]
    public void Defaults_HaveExpectedShape()
    {
        var options = new CalloutWolverineOptions();

        Assert.Equal("nats://localhost:4222", options.Url);
        Assert.Equal(string.Empty, options.ApplicationName);
        Assert.Null(options.TokenProvider);
        Assert.Null(options.Name);
    }
}
