// -----------------------------------------------------------------------
// <copyright file="NatsTransportExtensionsTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Connection;
using AuthCallout.Client.Tokens;
using AuthCallout.Client.Wolverine;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using Wolverine;

namespace AuthCallout.Client.Wolverine.Tests;

/// <summary>
/// Tests for <see cref="NatsTransportExtensions.UseNatsWithCallout"/> (ADR 0022 task 1):
/// options validation, and the transport's <c>AuthCallback</c> pulling a current token from
/// the configured <see cref="ICalloutTokenProvider"/> on every invocation (ADR 0021 rules 4-5).
/// </summary>
public sealed class NatsTransportExtensionsTests
{
    [Fact]
    public void UseNatsWithCallout_NullOptions_Throws()
    {
        global::Wolverine.WolverineOptions? options = null;

        Assert.Throws<ArgumentNullException>(() => options!.UseNatsWithCallout(_ => { }));
    }

    [Fact]
    public async Task UseNatsWithCallout_NullConfigure_Throws()
    {
        await using var options = new global::Wolverine.WolverineOptions();

        Assert.Throws<ArgumentNullException>(() => options.UseNatsWithCallout(null!));
    }

    [Fact]
    public async Task UseNatsWithCallout_MissingApplicationName_Throws()
    {
        await using var options = new global::Wolverine.WolverineOptions();

        Assert.Throws<ArgumentException>(() => options.UseNatsWithCallout(callout =>
        {
            callout.TokenProvider = new StaticTokenProvider("token");
        }));
    }

    [Fact]
    public async Task UseNatsWithCallout_MissingTokenProvider_Throws()
    {
        await using var options = new global::Wolverine.WolverineOptions();

        Assert.Throws<ArgumentException>(() => options.UseNatsWithCallout(callout =>
        {
            callout.ApplicationName = "notes";
        }));
    }

    [Fact]
    public async Task UseNatsWithCallout_AuthCallback_PullsCurrentTokenFromProvider()
    {
        await using var options = new global::Wolverine.WolverineOptions();
        var tokenProvider = new RecordingTokenProvider("current-token");

        options.UseNatsWithCallout(callout =>
        {
            callout.Url = "nats://localhost:4222";
            callout.ApplicationName = "notes";
            callout.TokenProvider = tokenProvider;
        });

        var transport = options.Transports.GetOrCreate<global::Wolverine.Nats.Internal.NatsTransport>();
        var callback = transport.Configuration.AuthCallback!;
        var cred = await callback(new Uri("nats://localhost:4222"), CancellationToken.None);

        Assert.Equal(1, tokenProvider.CallCount);
        Assert.Equal(NatsAuthCred.FromToken("current-token"), cred);
    }

    [Fact]
    public async Task UseNatsWithCallout_AuthCallback_NullToken_ThrowsAuthenticationRequired()
    {
        await using var options = new global::Wolverine.WolverineOptions();
        var tokenProvider = new RecordingTokenProvider(null);

        options.UseNatsWithCallout(callout =>
        {
            callout.Url = "nats://localhost:4222";
            callout.ApplicationName = "notes";
            callout.TokenProvider = tokenProvider;
        });

        var transport = options.Transports.GetOrCreate<global::Wolverine.Nats.Internal.NatsTransport>();
        var callback = transport.Configuration.AuthCallback!;

        await Assert.ThrowsAsync<CalloutAuthenticationRequiredException>(
            () => callback(new Uri("nats://localhost:4222"), CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task UseNatsWithCallout_RegistersCalloutServices()
    {
        await using var options = new global::Wolverine.WolverineOptions();

        options.UseNatsWithCallout(callout =>
        {
            callout.Url = "nats://localhost:4222";
            callout.ApplicationName = "notes";
            callout.TokenProvider = new StaticTokenProvider("token");
        });

        var provider = options.Services.BuildServiceProvider();
        Assert.NotNull(provider.GetService<CalloutTransportContext>());
        Assert.NotNull(provider.GetService<CalloutWolverineOptions>());
        Assert.NotNull(provider.GetService<CalloutHostedConnection>());
        Assert.NotNull(provider.GetService<OutboundIdentityHeaderService>());
        Assert.NotNull(provider.GetService<ICalloutIdentityVerifierAccessor>());
        Assert.NotNull(provider.GetService<OutboundIdentityHeaderHolder>());

        // The single-connection design (ConfigureNatsOpts seam): no second managed
        // CalloutNatsConnection is registered; identity services use the transport's own
        // connection via CalloutTransportContext.
        Assert.Null(provider.GetService<CalloutNatsConnection>());

        // The transport context captured the Wolverine transport at configuration time.
        var transport = options.Transports.GetOrCreate<global::Wolverine.Nats.Internal.NatsTransport>();
        Assert.Same(transport, provider.GetRequiredService<CalloutTransportContext>().Transport);
    }

    private sealed class RecordingTokenProvider : ICalloutTokenProvider
    {
        private readonly string? _token;

        public RecordingTokenProvider(string? token)
        {
            _token = token;
        }

        public int CallCount { get; private set; }

        public ValueTask<string?> GetTokenAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return ValueTask.FromResult(_token);
        }

        public ValueTask InvalidateAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
