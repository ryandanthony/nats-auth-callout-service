// -----------------------------------------------------------------------
// <copyright file="IssuerFetcher.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace AuthCallout.Identity.Jwks;

/// <summary>
/// A thin, always-re-fetch wrapper around one issuer's <see cref="IConfigurationRetriever{T}"/>
/// (discovery or direct-JWKS) plus its <see cref="IDocumentRetriever"/> transport. See
/// <see cref="IssuerConfigurationManagerFactory"/>'s remarks for why this exists instead of a
/// <c>Microsoft.IdentityModel</c> <see cref="ConfigurationManager{T}"/>: every call to
/// <see cref="FetchAsync"/> performs a genuine HTTP fetch, with no internal caching or
/// throttling of its own — that responsibility belongs entirely to
/// <see cref="IssuerJwksState"/>, the single owner of this issuer's freshness/single-flight
/// state.
/// </summary>
public sealed class IssuerFetcher
{
    private readonly string _address;
    private readonly IConfigurationRetriever<OpenIdConnectConfiguration> _configurationRetriever;
    private readonly IDocumentRetriever _documentRetriever;

    /// <summary>Initializes a new instance of the <see cref="IssuerFetcher"/> class.</summary>
    /// <param name="address">The discovery document or direct-JWKS URL to fetch from.</param>
    /// <param name="configurationRetriever">Interprets the fetched document (discovery vs. direct-JWKS shape).</param>
    /// <param name="documentRetriever">Performs the underlying HTTP fetch.</param>
    public IssuerFetcher(string address, IConfigurationRetriever<OpenIdConnectConfiguration> configurationRetriever, IDocumentRetriever documentRetriever)
    {
        ArgumentException.ThrowIfNullOrEmpty(address);
        ArgumentNullException.ThrowIfNull(configurationRetriever);
        ArgumentNullException.ThrowIfNull(documentRetriever);
        _address = address;
        _configurationRetriever = configurationRetriever;
        _documentRetriever = documentRetriever;
    }

    /// <summary>Fetches this issuer's current configuration (discovery + JWKS, or direct-JWKS), unconditionally hitting the network.</summary>
    /// <param name="cancellationToken">Cancellation for the fetch.</param>
    /// <returns>The fetched configuration.</returns>
    public Task<OpenIdConnectConfiguration> FetchAsync(CancellationToken cancellationToken) =>
        _configurationRetriever.GetConfigurationAsync(_address, _documentRetriever, cancellationToken);
}
