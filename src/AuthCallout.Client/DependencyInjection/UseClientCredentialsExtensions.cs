// -----------------------------------------------------------------------
// <copyright file="UseClientCredentialsExtensions.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Connection;
using AuthCallout.Client.Tokens;

namespace AuthCallout.Client.DependencyInjection;

/// <summary>
/// The <c>UseClientCredentials</c> helper on <see cref="CalloutClientOptions"/> (packet P19
/// task 5): <c>services.AddCalloutNatsClient(o =&gt; { ...; o.UseClientCredentials(cc =&gt; ...); })</c>.
/// </summary>
public static class UseClientCredentialsExtensions
{
    /// <summary>
    /// Configures this client to authenticate via <see cref="ClientCredentialsTokenProvider"/>
    /// (ADR 0002: a service connects as just another OIDC principal).
    /// </summary>
    /// <param name="options">The client options being configured.</param>
    /// <param name="configure">Configures the client-credentials provider's options.</param>
    /// <returns><paramref name="options"/>, for chaining.</returns>
    public static CalloutClientOptions UseClientCredentials(
        this CalloutClientOptions options, Action<ClientCredentialsTokenProviderOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configure);

        var providerOptions = new ClientCredentialsTokenProviderOptions();
        configure(providerOptions);
        options.TokenProvider = new ClientCredentialsTokenProvider(providerOptions);
        return options;
    }
}
