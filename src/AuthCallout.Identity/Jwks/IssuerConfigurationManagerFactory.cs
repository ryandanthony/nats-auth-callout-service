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
/// Builds the per-issuer discovery/JWKS fetch plumbing, choosing discovery vs. the
/// <see cref="IssuerConfig.JwksUri"/> override (doc 04: "an explicit override; otherwise it
/// is resolved from the provider's OIDC discovery document"). A separate, overridable type
/// (rather than a private method) so tests can point a <see cref="JwksCacheManager"/> at an
/// in-proc stub IdP's <see cref="HttpClient"/>/base address without any real network access.
/// </summary>
/// <remarks>
/// Returns an <see cref="IssuerFetcher"/> that calls the underlying
/// <see cref="IConfigurationRetriever{T}"/> directly on every fetch, rather than handing out a
/// <c>Microsoft.IdentityModel</c> <see cref="ConfigurationManager{T}"/> for
/// <see cref="IssuerJwksState"/> to call <c>GetConfigurationAsync</c> on. <c>ConfigurationManager</c>'s
/// own caching (<c>AutomaticRefreshInterval</c>/<c>_syncAfter</c>) silently returns the
/// previous configuration on any fetch attempted before that interval elapses, <b>even after
/// <c>RequestRefresh()</c></b>, unless the process opts into its "blocking" mode via an
/// <c>AppContext</c> switch — and that switch is cached process-wide on the *first* touch of
/// any <c>Microsoft.IdentityModel.Tokens</c> type, so a library can't reliably set it before
/// some unrelated earlier code (a token handler, another test) already has. This packet's own
/// <see cref="IssuerJwksState"/> already implements single-flight coordination and freshness
/// bookkeeping (doc 08 §Single-flight on demand), so it needs a fetch primitive that always
/// re-fetches on request, not another layer of the same caching decision.
/// </remarks>
public class IssuerConfigurationManagerFactory
{
    private readonly HttpClient? _httpClient;
    private readonly bool _requireHttps;

    /// <summary>Initializes a new instance of the <see cref="IssuerConfigurationManagerFactory"/> class using the default <see cref="HttpDocumentRetriever"/> transport (HTTPS required).</summary>
    public IssuerConfigurationManagerFactory()
    {
        _requireHttps = true;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IssuerConfigurationManagerFactory"/> class
    /// using a specific <see cref="HttpClient"/> (test seam: point at an in-proc stub IdP over
    /// plain HTTP, so <see cref="HttpDocumentRetriever.RequireHttps"/> is relaxed only for this
    /// constructor; the production, parameterless constructor always requires HTTPS).
    /// </summary>
    /// <param name="httpClient">The HTTP client to fetch discovery/JWKS documents with.</param>
    public IssuerConfigurationManagerFactory(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _requireHttps = false;
    }

    /// <summary>
    /// Creates the fetch primitive for one issuer: discovery-based when
    /// <see cref="IssuerConfig.JwksUri"/> is null, direct-JWKS (bypassing discovery entirely)
    /// otherwise. Every call to the returned fetcher performs a genuine fetch; caching cadence
    /// is <see cref="IssuerJwksState"/>'s decision, not this type's.
    /// </summary>
    /// <param name="issuer">The issuer to build a fetcher for.</param>
    /// <returns>The constructed fetcher for this issuer.</returns>
    public virtual IssuerFetcher Create(IssuerConfig issuer)
    {
        ArgumentNullException.ThrowIfNull(issuer);

        var documentRetriever = BuildDocumentRetriever();
        return issuer.JwksUri is { } jwksUri
            ? new IssuerFetcher(jwksUri, new JwksOnlyConfigurationRetriever(), documentRetriever)
            : new IssuerFetcher(DiscoveryAddress(issuer.Authority), new OpenIdConnectConfigurationRetriever(), documentRetriever);
    }

    private static string DiscoveryAddress(string authority) =>
        authority.TrimEnd('/') + "/.well-known/openid-configuration";

    private IDocumentRetriever BuildDocumentRetriever()
    {
        var retriever = _httpClient is not null ? new HttpDocumentRetriever(_httpClient) : new HttpDocumentRetriever();
        retriever.RequireHttps = _requireHttps;
        return retriever;
    }
}
