// -----------------------------------------------------------------------
// <copyright file="CalloutNatsOptsTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Connection;
using AuthCallout.Client.Tokens;
using AuthCallout.Contracts.Config;

namespace AuthCallout.Client.Tests.Connection;

/// <summary>Tests for <see cref="CalloutNatsOpts.CreateAsync"/>'s inbox-prefix derivation (ADR 0013, ADR 0021).</summary>
public sealed class CalloutNatsOptsTests
{
    [Fact]
    public async Task CreateAsync_AuthenticatedWithSub_DerivesInboxPrefixFromSub()
    {
        var jwt = TestJwt.Create(new Dictionary<string, object?>(StringComparer.Ordinal) { ["sub"] = "user-1" });
        var options = new CalloutClientOptions
        {
            ApplicationName = "notes",
            TokenProvider = new StaticTokenProvider(jwt),
        };

        var opts = await CalloutNatsOpts.CreateAsync(options, CancellationToken.None);

        var expected = AppSlug.From("notes").ClientInboxPrefix("user-1");
        Assert.Equal(expected, opts.InboxPrefix);
        Assert.Equal(jwt, opts.AuthOpts.Token);
        Assert.Equal(0, opts.MaxReconnectRetry);
    }

    [Fact]
    public async Task CreateAsync_AuthenticatedWithAzpFallback_DerivesInboxPrefixFromAzp()
    {
        var jwt = TestJwt.Create(new Dictionary<string, object?>(StringComparer.Ordinal) { ["azp"] = "notes-worker" });
        var options = new CalloutClientOptions
        {
            ApplicationName = "notes",
            TokenProvider = new StaticTokenProvider(jwt),
        };

        var opts = await CalloutNatsOpts.CreateAsync(options, CancellationToken.None);

        var expected = AppSlug.From("notes").ClientInboxPrefix("notes-worker");
        Assert.Equal(expected, opts.InboxPrefix);
    }

    [Fact]
    public async Task CreateAsync_AuthenticatedTokenWithNeitherSubNorAzp_Throws()
    {
        var jwt = TestJwt.Create(new Dictionary<string, object?>(StringComparer.Ordinal) { ["other"] = "value" });
        var options = new CalloutClientOptions
        {
            ApplicationName = "notes",
            TokenProvider = new StaticTokenProvider(jwt),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => CalloutNatsOpts.CreateAsync(options, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task CreateAsync_AuthenticatedNullToken_ThrowsCalloutAuthenticationRequired()
    {
        var options = new CalloutClientOptions
        {
            ApplicationName = "notes",
            TokenProvider = new StaticTokenProvider(null),
        };

        await Assert.ThrowsAsync<CalloutAuthenticationRequiredException>(() => CalloutNatsOpts.CreateAsync(options, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task CreateAsync_Anonymous_UsesDefaultInboxPrefix()
    {
        var options = new CalloutClientOptions
        {
            ApplicationName = "notes",
            Anonymous = true,
        };

        var opts = await CalloutNatsOpts.CreateAsync(options, CancellationToken.None);

        Assert.Equal("notes._INBOX", opts.InboxPrefix);
        Assert.True(opts.AuthOpts.IsAnonymous);
    }

    [Fact]
    public async Task CreateAsync_AnonymousWithExplicitPrefix_UsesConfiguredPrefix()
    {
        var options = new CalloutClientOptions
        {
            ApplicationName = "notes",
            Anonymous = true,
            AnonymousInboxPrefix = "custom._INBOX",
        };

        var opts = await CalloutNatsOpts.CreateAsync(options, CancellationToken.None);

        Assert.Equal("custom._INBOX", opts.InboxPrefix);
    }

    [Fact]
    public async Task CreateAsync_AuthenticatedMissingTokenProvider_Throws()
    {
        var options = new CalloutClientOptions { ApplicationName = "notes" };

        await Assert.ThrowsAsync<ArgumentException>(() => CalloutNatsOpts.CreateAsync(options, CancellationToken.None).AsTask());
    }
}
