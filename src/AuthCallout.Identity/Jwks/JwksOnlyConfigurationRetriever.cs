// -----------------------------------------------------------------------
// <copyright file="JwksOnlyConfigurationRetriever.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace AuthCallout.Identity.Jwks;

/// <summary>
/// An <see cref="IConfigurationRetriever{T}"/> that treats its address as a raw JWKS document
/// URI rather than an OIDC discovery document, for the <c>IssuerConfig.JwksUri</c>
/// override path (doc 04: "an explicit override; otherwise it is resolved from the provider's
/// OIDC discovery document"). Unlike the standard
/// <see cref="OpenIdConnectConfigurationRetriever"/>, this never fetches a discovery document,
/// so the override genuinely bypasses discovery rather than merely overriding the JWKS field
/// inside it.
/// </summary>
internal sealed class JwksOnlyConfigurationRetriever : IConfigurationRetriever<OpenIdConnectConfiguration>
{
    /// <inheritdoc />
    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
        string address, IDocumentRetriever retriever, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(retriever);

        var json = await retriever.GetDocumentAsync(address, cancel);
        var jwks = new JsonWebKeySet(json);
        var configuration = new OpenIdConnectConfiguration { JsonWebKeySet = jwks };
        foreach (var key in jwks.GetSigningKeys())
        {
            configuration.SigningKeys.Add(key);
        }

        return configuration;
    }
}
