// -----------------------------------------------------------------------
// <copyright file="ClientCredentialsTokenProviderTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AuthCallout.Client.Tokens;
using AuthCallout.Contracts.Primitives;
using Microsoft.Extensions.Time.Testing;

namespace AuthCallout.Client.Tests.Tokens;

/// <summary>
/// Tests for <see cref="ClientCredentialsTokenProvider"/>: refresh-deadline math and
/// failed-re-mint backoff, driven by a <see cref="FakeTimeProvider"/> and a stub
/// <see cref="HttpMessageHandler"/> rather than a real token endpoint.
/// </summary>
public sealed class ClientCredentialsTokenProviderTests
{
    [Fact]
    public async Task GetTokenAsync_FirstCall_MintsAndReturnsToken()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var handler = new StubHandler(_ => TokenResponse("token-1", expiresInSeconds: 300));
        using var provider = CreateProvider(handler, time);

        var token = await provider.GetTokenAsync(CancellationToken.None);

        Assert.Equal("token-1", token);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetTokenAsync_WithinRefreshMargin_ReusesCachedToken()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var handler = new StubHandler(_ => TokenResponse("token-1", expiresInSeconds: 300));
        using var provider = CreateProvider(handler, time);

        await provider.GetTokenAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(30)); // still > 30s margin before 300s exp (barely)
        var second = await provider.GetTokenAsync(CancellationToken.None);

        Assert.Equal("token-1", second);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task GetTokenAsync_PastRefreshMargin_ReMints()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var call = 0;
        using var handler = new StubHandler(_ => TokenResponse($"token-{++call}", expiresInSeconds: 300));
        using var provider = CreateProvider(handler, time);

        await provider.GetTokenAsync(CancellationToken.None);

        // 300s exp, 30s margin => must re-mint once >= 270s have elapsed.
        time.Advance(TimeSpan.FromSeconds(271));
        var second = await provider.GetTokenAsync(CancellationToken.None);

        Assert.Equal("token-2", second);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task GetTokenAsync_FailedRemint_ReturnsStaleTokenAndBacksOff()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var call = 0;
        using var handler = new StubHandler(_ =>
        {
            call++;
            return call == 1
                ? TokenResponse("token-1", expiresInSeconds: 300)
                : throw new HttpRequestException("token endpoint down");
        });
        using var provider = CreateProvider(handler, time, failedRefreshBackoff: TimeSpan.FromSeconds(5));

        await provider.GetTokenAsync(CancellationToken.None);

        time.Advance(TimeSpan.FromSeconds(271)); // past the refresh margin: triggers a re-mint attempt
        var stale = await provider.GetTokenAsync(CancellationToken.None);

        Assert.Equal("token-1", stale); // ADR 0021 rule 5: stale token retained on failed re-mint.
        Assert.Equal(2, call);

        // Still within the failed-refresh backoff window: no further re-mint attempt.
        time.Advance(TimeSpan.FromSeconds(1));
        var stillStale = await provider.GetTokenAsync(CancellationToken.None);
        Assert.Equal("token-1", stillStale);
        Assert.Equal(2, call);
    }

    [Fact]
    public async Task InvalidateAsync_DropsCache_NextGetReMints()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var call = 0;
        using var handler = new StubHandler(_ => TokenResponse($"token-{++call}", expiresInSeconds: 300));
        using var provider = CreateProvider(handler, time);

        await provider.GetTokenAsync(CancellationToken.None);
        await provider.InvalidateAsync(CancellationToken.None);
        var second = await provider.GetTokenAsync(CancellationToken.None);

        Assert.Equal("token-2", second);
        Assert.Equal(2, handler.CallCount);
    }

    private static ClientCredentialsTokenProvider CreateProvider(StubHandler handler, FakeTimeProvider time, TimeSpan? failedRefreshBackoff = null)
    {
        var options = new ClientCredentialsTokenProviderOptions
        {
            TokenEndpoint = new Uri("https://idp.example.com/token"),
            ClientId = "test-client",
            ClientSecret = SecretRef.Inline("secret"),
        };
        if (failedRefreshBackoff is { } backoff)
        {
            options.FailedRefreshBackoff = backoff;
        }

#pragma warning disable CA2000 // Lifetime matches the test method (never greater): the provider doesn't own this client (disposeHandler: false keeps the test's `using handler` in control), and the client itself is a thin wrapper with no unmanaged resources beyond the handler.
        var httpClient = new HttpClient(handler, disposeHandler: false);
#pragma warning restore CA2000
        return new ClientCredentialsTokenProvider(options, httpClient, time);
    }

    private static HttpResponseMessage TokenResponse(string accessToken, int expiresInSeconds) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new TokenPayload(accessToken, expiresInSeconds)),
        };

    private sealed record TokenPayload(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_respond(request));
        }
    }
}
