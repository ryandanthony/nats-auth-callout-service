// -----------------------------------------------------------------------
// <copyright file="KeycloakFixture.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Integration.Support;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace AuthCallout.Integration.Fixtures;

/// <summary>
/// A real Keycloak container (Testcontainers.Keycloak base image, E23) importing
/// <see cref="IntegrationRealm.BuildRealmJson"/> on startup, served over real HTTPS with a
/// throwaway self-signed certificate (<see cref="SelfSignedCertificate"/>). HTTPS, not plain
/// HTTP: the callout Host's production JWKS fetch path
/// (<see cref="AuthCallout.Identity.Jwks.IssuerConfigurationManagerFactory"/>'s parameterless
/// constructor) always requires HTTPS via <c>HttpDocumentRetriever.RequireHttps</c> — this
/// suite drives that exact path (<c>HostFixture</c> runs the real <c>ComponentGraph</c>), so
/// relaxing it here would leave the production fetch path unexercised, the opposite of what
/// M2 is supposed to prove. Every real HTTPS client this suite builds against this fixture
/// (its own <see cref="GetPasswordGrantTokenAsync"/>/<see cref="GetClientCredentialsTokenAsync"/>,
/// and the callout Host's JWKS fetch) must trust <see cref="CertificatePem"/> via
/// <see cref="TrustedHttpClientFactory"/> — see that type's doc comment for why this fixture
/// does not mutate the OS/user certificate store.
/// </summary>
public sealed class KeycloakFixture : IAsyncLifetime
{
    private const int HttpsPort = 8443;

    private IContainer? _container;
    private string? _certificatePem;

    /// <summary>Gets the realm's issuer authority URL (<c>{baseAddress}/realms/integration</c>), for app config <c>issuer.authority</c>.</summary>
    /// <value>The issuer authority.</value>
    public string IssuerAuthority => $"{BaseAddress}/realms/{IntegrationRealm.RealmName}";

    /// <summary>Gets this fixture's PEM-encoded self-signed CA certificate. Pass to <see cref="TrustedHttpClientFactory.Create"/> before any real HTTPS call against <see cref="IssuerAuthority"/>.</summary>
    /// <value>The PEM-encoded certificate.</value>
    public string CertificatePem => _certificatePem ?? throw NotStarted();

    /// <summary>Gets the container's HTTPS base address (host-mapped).</summary>
    /// <value>The base address.</value>
    public string BaseAddress => _container is null
        ? throw NotStarted()
        : $"https://127.0.0.1:{_container.GetMappedPublicPort(HttpsPort)}";

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        var (certificatePem, privateKeyPem) = SelfSignedCertificate.Generate();
        _certificatePem = certificatePem;

        var realmJson = IntegrationRealm.BuildRealmJson();
        var realmFile = Path.Combine(Path.GetTempPath(), $"integration-realm-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(realmFile, realmJson);

        try
        {
            using var waitStrategyHandler = TrustedHttpClientFactory.CreateHandler(certificatePem);

            _container = new ContainerBuilder("quay.io/keycloak/keycloak:26.0")
                .WithPortBinding(HttpsPort, assignRandomHostPort: true)
                .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
                .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", "admin")
                .WithEnvironment("KC_HTTPS_CERTIFICATE_FILE", "/opt/keycloak/conf/integration-cert.pem")
                .WithEnvironment("KC_HTTPS_CERTIFICATE_KEY_FILE", "/opt/keycloak/conf/integration-key.pem")
                .WithResourceMapping(System.Text.Encoding.UTF8.GetBytes(certificatePem), "/opt/keycloak/conf/integration-cert.pem")
                .WithResourceMapping(System.Text.Encoding.UTF8.GetBytes(privateKeyPem), "/opt/keycloak/conf/integration-key.pem")
                .WithResourceMapping(realmFile, "/opt/keycloak/data/import/")
                .WithCommand("start-dev", "--import-realm")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r
                    .ForPath("/realms/master/.well-known/openid-configuration")
                    .ForPort(HttpsPort)
                    .UsingTls()
                    .UsingHttpMessageHandler(waitStrategyHandler)))
                .Build();

            await _container.StartAsync();
        }
        finally
        {
            File.Delete(realmFile);
        }
    }

    /// <summary>Fetches an access token via the resource-owner password grant.</summary>
    /// <param name="clientId">The public client id.</param>
    /// <param name="username">The resource-owner username.</param>
    /// <param name="password">The resource-owner password.</param>
    /// <returns>The raw access token.</returns>
    public Task<string> GetPasswordGrantTokenAsync(string clientId, string username, string password) =>
        TokenFetcher.FetchAsync(BaseAddress, IntegrationRealm.RealmName, CertificatePem, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "password",
            ["client_id"] = clientId,
            ["username"] = username,
            ["password"] = password,
        });

    /// <summary>Fetches an access token via the client-credentials grant (ADR 0002 service principal).</summary>
    /// <param name="clientId">The confidential client id.</param>
    /// <param name="clientSecret">The confidential client secret.</param>
    /// <returns>The raw access token.</returns>
    public Task<string> GetClientCredentialsTokenAsync(string clientId, string clientSecret) =>
        TokenFetcher.FetchAsync(BaseAddress, IntegrationRealm.RealmName, CertificatePem, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
        });

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    private static InvalidOperationException NotStarted() =>
        new("KeycloakFixture has not been initialized yet.");
}
