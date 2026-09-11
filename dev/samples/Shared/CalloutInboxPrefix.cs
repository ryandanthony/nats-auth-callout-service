// -----------------------------------------------------------------------
// <copyright file="CalloutInboxPrefix.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Contracts.Config;

namespace AuthCallout.Dev.Samples.Common;

/// <summary>
/// Derives the custom inbox prefix a client must configure (<c>NatsOpts.InboxPrefix</c>) so
/// its request-reply inboxes land inside the scoped inbox subject the callout grants
/// (<c>_INBOX.{app-slug}.{derived-principal-key}.&gt;</c> — see
/// <see cref="AppSlug.ClientInboxPrefix"/>). The principal mirrors the callout's own
/// selection: the token's <c>sub</c>, falling back to <c>azp</c>.
/// </summary>
public static class CalloutInboxPrefix
{
    /// <summary>Derives the inbox prefix for a connection presenting <paramref name="oidcToken"/> to <paramref name="appSlugText"/>.</summary>
    /// <param name="appSlugText">The application's slug (matching the callout's ADR 0013 slug of the app name).</param>
    /// <param name="oidcToken">The raw OIDC token this client connects with.</param>
    /// <returns>The inbox prefix to set on <c>NatsOpts.InboxPrefix</c>.</returns>
    /// <exception cref="InvalidOperationException">The token carries neither <c>sub</c> nor <c>azp</c>.</exception>
    public static string For(string appSlugText, string oidcToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appSlugText);
        ArgumentException.ThrowIfNullOrWhiteSpace(oidcToken);

        var principal = OidcTokenClaims.ReadSub(oidcToken) ?? OidcTokenClaims.ReadClaim(oidcToken, "azp")
            ?? throw new InvalidOperationException("Presented token has neither 'sub' nor 'azp'; cannot derive the scoped inbox prefix.");
        return AppSlug.From(appSlugText).ClientInboxPrefix(principal);
    }
}
