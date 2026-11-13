// -----------------------------------------------------------------------
// <copyright file="AllowInsecureHttpKeycloakTests.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Primitives;
using AuthCallout.Host.Lifecycle;
using AuthCallout.Identity.Jwks;
using AuthCallout.Identity.Validation;
using AuthCallout.Integration.Support;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace AuthCallout.Integration.Scenarios;

/// <summary>
/// Issue #2 acceptance: a real Keycloak container served over <b>plain HTTP</b> (no TLS at all,
/// the docker-compose/Testcontainers dev setup the flag exists for), driven through the
/// production construction path <c>new JwksCacheManager(source, bootstrap.Jwks)</c> exactly as
/// <c>ComponentGraph</c> builds it (no injected <see cref="HttpClient"/>, so the real
/// <c>HttpDocumentRetriever.RequireHttps</c> decision is what's under test).
/// <list type="bullet">
/// <item><description><c>jwks.allowInsecureHttp</c> off: the issuer never warms (fails exactly as before the flag existed).</description></item>
/// <item><description>on: prewarm succeeds and a real Keycloak-issued token validates through <see cref="TokenValidator"/>, so token validation itself is unchanged and still enforced.</description></item>
/// </list>
/// Owns its own container instead of the shared HTTPS <c>KeycloakFixture</c>, since the whole
/// point is a TLS-less IdP.
/// </summary>
public sealed class AllowInsecureHttpKeycloakTests : IAsyncLifetime
{
    private const int HttpPort = 8080;

    private IContainer? _keycloak;

    private string BaseAddress => $"http://127.0.0.1:{_keycloak!.GetMappedPublicPort(HttpPort)}";

    private string Authority => $"{BaseAddress}/realms/{IntegrationRealm.RealmName}";

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        var realmFile = Path.Combine(Path.GetTempPath(), $"integration-realm-http-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(realmFile, IntegrationRealm.BuildRealmJson());
        try
        {
            _keycloak = new ContainerBuilder("quay.io/keycloak/keycloak:26.0")
                .WithPortBinding(HttpPort, assignRandomHostPort: true)
                .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
                .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", "admin")
                .WithResourceMapping(realmFile, "/opt/keycloak/data/import/")
                .WithCommand("start-dev", "--import-realm", "--http-enabled=true")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r
                    .ForPath($"/realms/{IntegrationRealm.RealmName}/.well-known/openid-configuration")
                    .ForPort(HttpPort)))
                .Build();
            await _keycloak.StartAsync();
        }
        finally
        {
            File.Delete(realmFile);
        }
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_keycloak is not null)
        {
            await _keycloak.DisposeAsync();
        }
    }

    [Fact]
    public async Task AllowInsecureHttpOff_HttpKeycloak_NeverWarms()
    {
        using var manager = BuildManager(allowInsecureHttp: false, out _);

        await manager.PrewarmAllAsync(CancellationToken.None);

        var health = Assert.Single(manager.Health);
        Assert.False(health.HasUsableKeys);
        Assert.Empty(manager.GetSigningKeys(Authority));
    }

    [Fact]
    public async Task AllowInsecureHttpOn_HttpKeycloak_PrewarmsAndRealTokenValidates()
    {
        using var manager = BuildManager(allowInsecureHttp: true, out var issuer);

        await manager.PrewarmAllAsync(CancellationToken.None);

        Assert.True(Assert.Single(manager.Health).HasUsableKeys);

        var token = await FetchPasswordGrantTokenAsync(IntegrationRealm.NotesClientId, IntegrationRealm.NoteUsername, IntegrationRealm.NotePassword);
        var outcome = await new TokenValidator(manager, manager).ValidateAsync(token, issuer, CancellationToken.None);

        Assert.Null(outcome.Reason);
        Assert.NotNull(outcome.Claims);
    }

    private JwksCacheManager BuildManager(bool allowInsecureHttp, out IssuerConfig issuer)
    {
        issuer = TestApplications.Issuer(Authority);
        var bootstrap = new BootstrapConfig(
            NatsUrl: "nats://unused",
            IssuerSeed: SecretRef.Inline("unused"),
            ServiceUserSeed: null,
            XkeySeed: null,
            AssertionKeys: null,
            Source: SourceConfig.Files(new FilesSourceSettings("/unused"), DurationString.Parse("60s"), DurationString.Parse("15m"), DurationString.Parse("5s")),
            Jwks: new JwksSettings(DurationString.Parse("5m"), DurationString.Parse("5s"), PrewarmRequired: true, AllowInsecureHttp: allowInsecureHttp),
            Concurrency: new ConcurrencySettings(MaxDegreeOfParallelism: 4, PerRequestTimeout: DurationString.Parse("2s"), QueueCapacity: null),
            Anonymous: new AnonymousGlobalConfig("ANON", DurationString.Parse("5m"), Events: null));

        var source = new ActiveConfigSource();
        source.Apply(new ActiveConfig(bootstrap, new RoutingConfig([TestApplications.NotesApp(Authority)]), Version: 1));

        // The production constructor: JwksSettings.AllowInsecureHttp alone decides RequireHttps.
        return new JwksCacheManager(source, bootstrap.Jwks);
    }

    private async Task<string> FetchPasswordGrantTokenAsync(string clientId, string username, string password)
    {
        using var client = new HttpClient();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "password",
            ["client_id"] = clientId,
            ["username"] = username,
            ["password"] = password,
        });
        using var response = await client.PostAsync(new Uri($"{Authority}/protocol/openid-connect/token"), content);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("access_token").GetString()!;
    }
}
