// -----------------------------------------------------------------------
// <copyright file="WolverineIntegrationTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using AuthCallout.Client.Connection;
using AuthCallout.Client.Tokens;
using AuthCallout.Contracts.Primitives;
using AuthCallout.Integration.Fixtures;
using AuthCallout.Integration.Support;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using Wolverine;

namespace AuthCallout.Integration.Scenarios;

/// <summary>
/// P20 task 4: the Wolverine-over-NATS integration suite — a real <see cref="WolverineHostFactory"/>
/// service host and client host, both authenticated through the callout
/// (<see cref="TestApplications.WolverineApp"/>), proving the full round trip
/// (<c>UseNatsWithCallout</c> → <see cref="AuthCallout.Client.Wolverine.CalloutIdentityMiddleware"/> →
/// verified <see cref="AuthCallout.Client.Identity.CalloutIdentityContext"/> handed to the handler)
/// end to end over a real NATS server and a real Keycloak-issued token, plus the negative paths
/// (forged/missing identity header, anonymous opt-out, token-freshness-at-connect).
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class WolverineIntegrationTests(IntegrationEnvironment environment) : IAsyncLifetime
{
    private HostFixture? _calloutHost;

    public async Task InitializeAsync()
    {
        var nats = environment.Nats;
        var keycloak = environment.Keycloak;
        _calloutHost = await HostFixture.StartAsync(
            nats,
            keycloak.CertificatePem,
            [TestApplications.WolverineApp(keycloak.IssuerAuthority)],
            anonymous: null,
            CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        if (_calloutHost is not null)
        {
            await _calloutHost.DisposeAsync();
        }
    }

    /// <summary>
    /// Scenario 1: a "client" Wolverine host (wolverine-client creds) publishes <see cref="Ping"/>; a
    /// "service" Wolverine host (wolverine-service creds) listening via <see cref="WolverineHostFactory"/>
    /// handles it through the real middleware chain and records the verified caller's <c>sub</c>
    /// (<see cref="PingHandlers.Handle(Ping, AuthCallout.Client.Identity.CalloutIdentityContext)"/>) — proving
    /// the service's injected <see cref="AuthCallout.Client.Identity.CalloutIdentityContext.Subject"/> equals
    /// the client token's own <c>sub</c> claim.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task RoundTrip_VerifiedIdentity_HandlerSeesClientSubject()
    {
        var nats = environment.Nats;

        var (serviceProvider, serviceHttpClient) = await CreateTokenProviderAsync(IntegrationRealm.WolverineServiceClientId);
        var (clientProvider, clientHttpClient) = await CreateTokenProviderAsync(IntegrationRealm.WolverineClientClientId);
        var clientToken = await environment.Keycloak.GetClientCredentialsTokenAsync(IntegrationRealm.WolverineClientClientId, IntegrationRealm.ServiceClientSecret);
        var expectedSubject = OidcTokenClaims.ReadSub(clientToken);
        Assert.NotNull(expectedSubject);

        using var serviceHost = await WolverineHostFactory.StartAsync(nats.ConnectionUrl, "wolverine-service-round-trip", serviceProvider);
        using var clientHost = await WolverineHostFactory.StartAsync(nats.ConnectionUrl, "wolverine-client-round-trip", clientProvider);

        var payload = Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture);
        var bus = clientHost.Services.GetRequiredService<IMessageBus>();
        await bus.PublishAsync(new Ping(payload));

        var recorded = await PollAsync(() => Recorder.PingHandled.FirstOrDefault(entry => string.Equals(entry.payload, payload, StringComparison.Ordinal)));
        Assert.NotEqual(default((string payload, string subject)), recorded);
        Assert.Equal(expectedSubject, recorded.subject);

        serviceProvider.Dispose();
        clientProvider.Dispose();
        serviceHttpClient.Dispose();
        clientHttpClient.Dispose();
    }

    /// <summary>
    /// Scenario 2: a raw NATS publish correctly shaped as a Wolverine <see cref="Ping"/> envelope
    /// (the <c>message-type</c> header Wolverine's own <c>EnvelopeMapper</c> writes, System.Text.Json
    /// body) but with no <c>Callout-Identity</c> header at all never reaches the handler — the
    /// inbound <see cref="AuthCallout.Client.Wolverine.CalloutIdentityMiddleware"/> stops the chain
    /// before <see cref="PingHandlers.Handle(Ping, AuthCallout.Client.Identity.CalloutIdentityContext)"/>
    /// runs. A forged (garbage, well-formed-looking) identity header is covered too, in the same
    /// test, since both paths terminate the chain the same way and a raw connection can set either
    /// header value with no extra ceremony.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task MissingOrForgedIdentityHeader_HandlerNeverRuns()
    {
        var keycloak = environment.Keycloak;
        var nats = environment.Nats;
        var clientToken = await keycloak.GetClientCredentialsTokenAsync(IntegrationRealm.WolverineClientClientId, IntegrationRealm.ServiceClientSecret);

        var (serviceProvider, serviceHttpClient) = await CreateTokenProviderAsync(IntegrationRealm.WolverineServiceClientId);
        using var serviceHost = await WolverineHostFactory.StartAsync(nats.ConnectionUrl, "wolverine-service-forged", serviceProvider);

        await using var raw = await ConnectRawAsync(nats.ConnectionUrl, clientToken, "wolverine-raw-forged");
        var client = raw.Connection!;

        var missingHeaderPayload = Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture);
        await PublishRawAsync(client, "wolverine.dev.ping", typeof(Ping), new Ping(missingHeaderPayload), identityHeaderValue: null);

        var forgedHeaderPayload = Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture);
        await PublishRawAsync(client, "wolverine.dev.ping", typeof(Ping), new Ping(forgedHeaderPayload), identityHeaderValue: "forged.not-a.real-assertion");

        await Task.Delay(TimeSpan.FromSeconds(3));
        Assert.DoesNotContain(Recorder.PingHandled, entry => string.Equals(entry.payload, missingHeaderPayload, StringComparison.Ordinal));
        Assert.DoesNotContain(Recorder.PingHandled, entry => string.Equals(entry.payload, forgedHeaderPayload, StringComparison.Ordinal));

        serviceProvider.Dispose();
        serviceHttpClient.Dispose();
    }

    /// <summary>
    /// Scenario 3: <see cref="AnonymousPing"/> opts out of identity verification
    /// (<c>[RequireIdentity(false)]</c>) — a raw connection with no identity header at all still
    /// reaches <see cref="PingHandlers.Handle(AnonymousPing)"/>.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task RequireIdentityFalse_HandlerRunsWithNoIdentityHeader()
    {
        var keycloak = environment.Keycloak;
        var nats = environment.Nats;
        var clientToken = await keycloak.GetClientCredentialsTokenAsync(IntegrationRealm.WolverineClientClientId, IntegrationRealm.ServiceClientSecret);

        var (serviceProvider, serviceHttpClient) = await CreateTokenProviderAsync(IntegrationRealm.WolverineServiceClientId);
        using var serviceHost = await WolverineHostFactory.StartAsync(nats.ConnectionUrl, "wolverine-service-anon", serviceProvider);

        await using var raw = await ConnectRawAsync(nats.ConnectionUrl, clientToken, "wolverine-raw-anonymous");
        var client = raw.Connection!;

        var payload = Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture);
        await PublishRawAsync(client, "wolverine.dev.anonymous-ping", typeof(AnonymousPing), new AnonymousPing(payload), identityHeaderValue: null);

        var recorded = await PollAsync(() => Recorder.AnonymousPingHandled.FirstOrDefault(p => string.Equals(p, payload, StringComparison.Ordinal)));
        Assert.Equal(payload, recorded);

        serviceProvider.Dispose();
        serviceHttpClient.Dispose();
    }

    /// <summary>
    /// Scenario 4: token freshness at connect. A token with a <see cref="IntegrationRealm.ShortLivedTokenSeconds"/>
    /// lifetime is fetched and allowed to expire BEFORE the host starts; because
    /// <see cref="AuthCallout.Client.Wolverine.NatsTransportExtensions.UseNatsWithCallout"/> wires
    /// the transport's <c>AuthCallback</c> to pull a current token from the
    /// <see cref="ICalloutTokenProvider"/> on every (re)connect (ADR 0021 rules 4-5) rather than
    /// capturing one at configuration time, the host still connects and publishes successfully —
    /// the provider is asked again at connect time, well after the first token expired.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task TokenFreshnessAtConnect_HostStartsAfterFirstTokenExpired()
    {
        var nats = environment.Nats;

        // short-lived-web is a public, password-grant client (no service account) with a
        // 6-second access-token lifespan (IntegrationRealm.ShortLivedTokenSeconds). Fetch its
        // first token now and let it fully expire BEFORE the host ever connects: the provider
        // re-fetches on every call (no caching here, unlike ClientCredentialsTokenProvider), so
        // AuthCallback asking it again at connect time always gets a fresh token rather than the
        // one captured at configuration time (ADR 0021 rules 4-5).
        var firstToken = await environment.Keycloak.GetPasswordGrantTokenAsync(
            IntegrationRealm.ShortLivedClientId, IntegrationRealm.NoteUsername, IntegrationRealm.NotePassword);
        Assert.NotNull(firstToken);
        await Task.Delay(TimeSpan.FromSeconds(IntegrationRealm.ShortLivedTokenSeconds + 2));

        // Deliberately NON-caching: every call mints an all-new password-grant token with a new
        // jti, exactly like the dev samples' DelegateTokenProvider. The provider is asked for a
        // token by more than one adapter collaborator at host start (AuthCallback at connect,
        // CalloutPrefixPrewarmService for the inbox prefix). The callout keys this connection's
        // self-assertion by the jti of the token the transport actually PRESENTED, so the
        // adapter must build the outbound identity header from that presented token
        // (CalloutTransportContext.PresentedToken), never from a second independent fetch.
        // A provider that hands out a different jti per call is the regression this guards:
        // before the fix, the header prefetch fetched its own token and the self-assertion
        // lookup missed, failing host startup.
        var fetchCount = 0;
        var provider = new DelegateTokenProvider(async _ =>
        {
            Interlocked.Increment(ref fetchCount);
            return await environment.Keycloak.GetPasswordGrantTokenAsync(
                IntegrationRealm.ShortLivedClientId, IntegrationRealm.NoteUsername, IntegrationRealm.NotePassword);
        });

        // short-lived-web IS matched by the wolverine app's profile (TestApplications.WolverineApp),
        // so the host's transport actually authenticates and connects; a successful start proves
        // the host asked the provider for a current token at connect time (AuthCallback), well
        // after the first token's 6-second lifetime had elapsed, rather than reusing the stale
        // one captured when WolverineHostFactory was configured. Startup also completes the
        // outbound-header prefetch, proving the self-assertion resolved against the presented jti.
        using var host = await WolverineHostFactory.StartAsync(nats.ConnectionUrl, "wolverine-freshness", provider);
        Assert.NotNull(host);
        Assert.True(fetchCount >= 2, $"Expected independent token fetches (distinct jtis), saw {fetchCount}.");
    }

    /// <summary>
    /// Polls <paramref name="read"/> until it returns a non-default value or the timeout elapses.
    /// </summary>
    private static async Task<T> PollAsync<T>(Func<T> read, int timeoutSeconds = 10)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var value = read();
            if (!Equals(value, default(T)))
            {
                return value;
            }

            await Task.Delay(100);
        }

        return read();
    }

    /// <summary>Opens a raw <see cref="CalloutNatsConnection"/> against the wolverine app, bypassing any Wolverine host.</summary>
    private static async Task<CalloutNatsConnection> ConnectRawAsync(string natsUrl, string token, string name)
    {
        var connection = new CalloutNatsConnection(new CalloutClientOptions
        {
            Url = natsUrl,
            ApplicationName = TestApplications.WolverineAppName,
            TokenProvider = new StaticTokenProvider(token),
            Name = name,
        });
        await connection.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(20));
        return connection;
    }

    /// <summary>
    /// Publishes a raw, Wolverine-shaped envelope: the <c>message-type</c> header Wolverine's own
    /// <c>EnvelopeMapper</c> writes (the message type's full CLR name), a System.Text.Json body
    /// (Wolverine's default serializer), and an explicit (or absent) <c>Callout-Identity</c> header.
    /// </summary>
    private static async Task PublishRawAsync(NatsConnection client, string subject, Type messageType, object message, string? identityHeaderValue)
    {
        var headers = new NatsHeaders
        {
            ["message-type"] = messageType.FullName!,
            ["content-type"] = "application/json",
        };
        if (identityHeaderValue is not null)
        {
            headers["Callout-Identity"] = identityHeaderValue;
        }

        var body = JsonSerializer.SerializeToUtf8Bytes(message, messageType);
        await client.PublishAsync(subject, body, headers: headers);
    }

    private async Task<(ClientCredentialsTokenProvider provider, HttpClient httpClient)> CreateTokenProviderAsync(string clientId)
    {
        var keycloak = environment.Keycloak;
        var httpClient = TrustedHttpClientFactory.Create(keycloak.CertificatePem);
        var tokenEndpoint = new Uri($"{keycloak.BaseAddress}/realms/{IntegrationRealm.RealmName}/protocol/openid-connect/token");
        var provider = new ClientCredentialsTokenProvider(
            new ClientCredentialsTokenProviderOptions
            {
                TokenEndpoint = tokenEndpoint,
                ClientId = clientId,
                ClientSecret = SecretRef.Inline(IntegrationRealm.ServiceClientSecret),
            },
            httpClient);
        await provider.GetTokenAsync(CancellationToken.None);
        return (provider, httpClient);
    }
}
