// -----------------------------------------------------------------------
// <copyright file="CalloutConnection.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using NATS.Client.Core;

namespace AuthCallout.Dev.Samples.Common;

/// <summary>
/// The callout-specific NATS connection setup shared by every Wolverine sample (both the
/// clients and the services): fetch an OIDC token from the dev Keycloak realm, connect to
/// NATS presenting that token as the bearer credential (which drives the connection through
/// the callout into the app's account + grant), and set the scoped custom inbox prefix the
/// callout's grant requires (the S2 fix: the grant is <c>_INBOX.{slug}.{principal}.&gt;</c>,
/// never the account-wide <c>_INBOX.&gt;</c>). Wolverine's NATS transport is layered on top
/// of the resulting <see cref="NatsOpts"/>/token; this type owns only the token + inbox
/// concerns unique to running behind the callout.
/// </summary>
public static class CalloutConnection
{
    /// <summary>
    /// Fetches an access token for one app's dev user and builds the <see cref="NatsOpts"/>
    /// (URL + bearer token + scoped inbox prefix) a Wolverine host or a raw client should use
    /// to connect through the callout.
    /// </summary>
    /// <param name="appSlug">The application slug (<c>notes</c>/<c>tasks</c>), used for the inbox prefix and audience routing.</param>
    /// <param name="clientId">The Keycloak public client id (<c>notes-web</c>/<c>tasks-web</c>).</param>
    /// <param name="username">The dev realm username.</param>
    /// <param name="password">The dev realm password.</param>
    /// <param name="cancellationToken">Cancellation for the token fetch.</param>
    /// <returns>The token and the fully-populated <see cref="NatsOpts"/> to connect with.</returns>
    public static async Task<CalloutConnectionInfo> PrepareAsync(
        string appSlug, string clientId, string username, string password, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appSlug);

        var natsUrl = DevEnvironment.ReadNatsUrl();
        var keycloakBaseUrl = DevEnvironment.ReadKeycloakBaseUrl();
        var token = await DevOidcTokenFetcher.FetchAccessTokenAsync(
            keycloakBaseUrl, realm: "dev", clientId, username, password, cancellationToken);

        var opts = new NatsOpts
        {
            Url = natsUrl,
            Name = $"{appSlug}-wolverine",
            AuthOpts = new NatsAuthOpts { Token = token },
            InboxPrefix = CalloutInboxPrefix.For(appSlug, token),
        };

        return new CalloutConnectionInfo(token, opts);
    }
}
