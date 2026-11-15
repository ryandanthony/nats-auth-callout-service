// -----------------------------------------------------------------------
// <copyright file="CalloutWolverineOptions.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Connection;
using AuthCallout.Client.Tokens;

namespace AuthCallout.Client.Wolverine;

/// <summary>
/// Options for <see cref="NatsTransportExtensions.UseNatsWithCallout"/> (ADR 0022 task 1): the
/// subset of <see cref="CalloutClientOptions"/> needed to authenticate a Wolverine NATS
/// transport through the callout, plus the application name used for outbound identity header
/// attach and inbound verification (task 2/3).
/// </summary>
public sealed class CalloutWolverineOptions
{
    /// <summary>Gets or sets the NATS server URL(s) to connect to.</summary>
    /// <value>The server URL, e.g. <c>nats://localhost:4222</c>.</value>
    public string Url { get; set; } = "nats://localhost:4222";

    /// <summary>
    /// Gets or sets the application's authored name (slugified via <c>AppSlug</c>, ADR 0013):
    /// used for discovery, outbound identity header attach, and inbound verification.
    /// </summary>
    /// <value>The application name.</value>
    public string ApplicationName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the token provider used both to authenticate the Wolverine transport's
    /// connection (via an <c>AuthCallback</c>, since Wolverine owns the connection object) and,
    /// derived from the current token, to compute the scoped inbox prefix.
    /// </summary>
    /// <value>The token provider. Required unless <see cref="UseClientCredentials"/> is called.</value>
    public ICalloutTokenProvider? TokenProvider { get; set; }

    /// <summary>Gets or sets this connection's display name (<c>NatsOpts.Name</c> / Wolverine's <c>ClientName</c>).</summary>
    /// <value>The connection name, or null to use Wolverine's default.</value>
    public string? Name { get; set; }

    /// <summary>
    /// Configures <see cref="TokenProvider"/> via <see cref="ClientCredentialsTokenProvider"/>
    /// (ADR 0002: a service connects as just another OIDC principal).
    /// </summary>
    /// <param name="configure">Configures the client-credentials provider's options.</param>
    /// <returns>This instance, for chaining.</returns>
    public CalloutWolverineOptions UseClientCredentials(Action<ClientCredentialsTokenProviderOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var providerOptions = new ClientCredentialsTokenProviderOptions();
        configure(providerOptions);
        TokenProvider = new ClientCredentialsTokenProvider(providerOptions);
        return this;
    }
}
