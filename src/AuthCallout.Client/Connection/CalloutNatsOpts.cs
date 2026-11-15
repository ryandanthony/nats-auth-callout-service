// -----------------------------------------------------------------------
// <copyright file="CalloutNatsOpts.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Client.Tokens;
using AuthCallout.Contracts.Config;
using NATS.Client.Core;

namespace AuthCallout.Client.Connection;

/// <summary>
/// Builds the <see cref="NatsOpts"/> a client or subscriber must use to connect through the
/// callout (ADR 0020): bearer token + the scoped inbox prefix (<c>AppSlug.ClientInboxPrefix</c>,
/// the S2 fix — the grant is <c>_INBOX.{slug}.{principal}.&gt;</c>, never the account-wide
/// <c>_INBOX.&gt;</c>), derived from the token's <c>sub</c>, falling back to <c>azp</c>.
/// Promoted from the dev samples' <c>CalloutConnection.PrepareAsync</c>/<c>CalloutInboxPrefix</c>
/// minus the dev-Keycloak coupling. Exposes the raw <see cref="NatsOpts"/> for callers who own
/// their own connection lifecycle (and for framework layers built on this library, e.g. a
/// future Wolverine integration, ADR 0022).
/// </summary>
public static class CalloutNatsOpts
{
    /// <summary>The default anonymous inbox prefix suffix (app policy, not NACS policy; packet P19 open question 3).</summary>
    private const string AnonymousInboxSuffix = "._INBOX";

    /// <summary>
    /// Builds the <see cref="NatsOpts"/> for a connection through the callout, authenticated
    /// or anonymous per <see cref="CalloutClientOptions.Anonymous"/>.
    /// </summary>
    /// <param name="options">The client options. See <see cref="CalloutClientOptions"/>.</param>
    /// <param name="cancellationToken">Cancellation for the token fetch.</param>
    /// <returns>The populated <see cref="NatsOpts"/> (URL + token + scoped inbox prefix).</returns>
    /// <exception cref="ArgumentException"><see cref="CalloutClientOptions.ApplicationName"/> is empty.</exception>
    /// <exception cref="CalloutAuthenticationRequiredException">The authenticated path's token provider returned null (ADR 0021 rule 3).</exception>
    public static async ValueTask<NatsOpts> CreateAsync(CalloutClientOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ApplicationName);

        var slug = AppSlug.From(options.ApplicationName);

        if (options.Anonymous)
        {
            return new NatsOpts
            {
                Url = options.Url,
                Name = options.Name ?? "NATS .NET Client",
                InboxPrefix = options.AnonymousInboxPrefix ?? $"{slug.Value}{AnonymousInboxSuffix}",

                // ADR 0021 rule 4: reconnects are owned by the managed connection, never
                // NATS.Net's built-in reconnect, so every reconnect re-enters the connect
                // pipeline (fresh token, recomputed inbox prefix).
                MaxReconnectRetry = 0,
            };
        }

        if (options.TokenProvider is null)
        {
            throw new ArgumentException("TokenProvider must be set unless Anonymous is true.", nameof(options));
        }

        var token = await options.TokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false);
        if (token is null)
        {
            // ADR 0021 rule 3: never downgrade to anonymous. Fail immediately and loudly.
            throw new CalloutAuthenticationRequiredException();
        }

        return new NatsOpts
        {
            Url = options.Url,
            Name = options.Name ?? "NATS .NET Client",
            AuthOpts = new NatsAuthOpts { Token = token },
            InboxPrefix = slug.ClientInboxPrefix(ResolvePrincipal(token)),
            MaxReconnectRetry = 0,
        };
    }

    /// <summary>
    /// Derives the inbox-prefix principal from a presented token: <c>sub</c>, falling back to
    /// <c>azp</c> (mirrors the callout's own selection, doc 06).
    /// </summary>
    /// <param name="oidcToken">The raw OIDC token presented as the bearer credential.</param>
    /// <returns>The principal to scope the inbox prefix to.</returns>
    /// <exception cref="InvalidOperationException">The token carries neither <c>sub</c> nor <c>azp</c>.</exception>
    internal static string ResolvePrincipal(string oidcToken) =>
        OidcTokenClaims.ReadSub(oidcToken) ?? OidcTokenClaims.ReadAzp(oidcToken)
            ?? throw new InvalidOperationException("Presented token has neither 'sub' nor 'azp'; cannot derive the scoped inbox prefix.");
}
