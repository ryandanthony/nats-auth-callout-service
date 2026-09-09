// -----------------------------------------------------------------------
// <copyright file="IssuerConfigurationManagerFactory.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace AuthCallout.Identity.Jwks;

/// <summary>
/// Builds a per-issuer <see cref="ConfigurationManager{T}"/>, choosing discovery vs. the
/// <see cref="IssuerConfig.JwksUri"/> override (doc 04: "an explicit override; otherwise it
/// is resolved from the provider's OIDC discovery document"). A separate, overridable type
/// (rather than a private method) so tests can point a <see cref="JwksCacheManager"/> at an
/// in-proc stub IdP's <see cref="HttpClient"/>/base address without any real network access.
/// </summary>
public class IssuerConfigurationManagerFactory
{
    private readonly HttpClient? _httpClient;

    /// <summary>Initializes a new instance of the <see cref="IssuerConfigurationManagerFactory"/> class using the default <see cref="HttpDocumentRetriever"/> transport.</summary>
    public IssuerConfigurationManagerFactory()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="IssuerConfigurationManagerFactory"/> class using a specific <see cref="HttpClient"/> (test seam: point at an in-proc stub IdP).</summary>
    /// <param name="httpClient">The HTTP client to fetch discovery/JWKS documents with.</param>
    public IssuerConfigurationManagerFactory(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
    }

    /// <summary>
    /// Creates the <see cref="ConfigurationManager{T}"/> for one issuer: discovery-based when
    /// <see cref="IssuerConfig.JwksUri"/> is null, direct-JWKS (bypassing discovery entirely)
    /// otherwise.
    /// </summary>
    /// <param name="issuer">The issuer to build a configuration manager for.</param>
    /// <param name="settings">Bootstrap JWKS settings (proactive refresh cadence default, per-issuer overridable).</param>
    /// <returns>The constructed configuration manager for this issuer.</returns>
    public virtual ConfigurationManager<OpenIdConnectConfiguration> Create(IssuerConfig issuer, JwksSettings settings)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(settings);

        var documentRetriever = BuildDocumentRetriever();
        var manager = issuer.JwksUri is { } jwksUri
            ? new ConfigurationManager<OpenIdConnectConfiguration>(jwksUri, new JwksOnlyConfigurationRetriever(), documentRetriever)
            : new ConfigurationManager<OpenIdConnectConfiguration>(
                DiscoveryAddress(issuer.Authority), new OpenIdConnectConfigurationRetriever(), documentRetriever);

        manager.AutomaticRefreshInterval = (issuer.RefreshInterval ?? settings.RefreshInterval).Value;
        return manager;
    }

    private static string DiscoveryAddress(string authority) =>
        authority.TrimEnd('/') + "/.well-known/openid-configuration";

    private IDocumentRetriever BuildDocumentRetriever() =>
        _httpClient is not null ? new HttpDocumentRetriever(_httpClient) : new HttpDocumentRetriever();
}
