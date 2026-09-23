// -----------------------------------------------------------------------
// <copyright file="RefreshingCalloutConnection.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using AuthCallout.Dev.Samples.Common;
using NATS.Client.Core;

namespace AuthCallout.Dev.Samples.LoadService;

/// <summary>
/// One load worker's callout connection options, re-minting the OIDC token shortly before it
/// expires. The dev realm's <c>accessTokenLifespan</c> is 300s, so a token fetched once at
/// startup gets every connect denied as expired after ~5 minutes. Re-minting only near
/// <c>exp</c> (not per connect) keeps the load on the callout rather than on Keycloak.
/// </summary>
public sealed class RefreshingCalloutConnection
{
    // Re-mint this far ahead of exp so an in-flight connect never presents an expired token
    // (also absorbs minor clock skew between this process, Keycloak, and the callout).
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(30);

    // After a failed re-mint, keep the old token (it may still have a few seconds left) and
    // retry after this long rather than hammering Keycloak on every connect.
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(5);

    private readonly string _appSlug;
    private readonly TimeProvider _timeProvider;
    private CalloutConnectionInfo _current;
    private DateTimeOffset _refreshAt;

    private RefreshingCalloutConnection(string appSlug, CalloutConnectionInfo initial, TimeProvider timeProvider)
    {
        _appSlug = appSlug;
        _timeProvider = timeProvider;
        _current = initial;
        _refreshAt = RefreshDeadline(initial.Token);
    }

    /// <summary>Fetches the initial token, or returns null (logged) if that fetch fails.</summary>
    /// <param name="appSlug">The application slug to connect as.</param>
    /// <param name="timeProvider">The clock used to decide when to re-mint.</param>
    /// <param name="cancellationToken">Cancellation for the initial token fetch.</param>
    /// <returns>The ready connection source, or null if the initial fetch failed.</returns>
    public static async Task<RefreshingCalloutConnection?> CreateAsync(string appSlug, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var initial = await TryPrepareAsync(appSlug, cancellationToken);
        return initial is null ? null : new RefreshingCalloutConnection(appSlug, initial, timeProvider);
    }

    /// <summary>Returns connection options carrying a token that is not about to expire, re-minting first if needed.</summary>
    /// <param name="cancellationToken">Cancellation for a re-mint.</param>
    /// <returns>The NATS options to connect with.</returns>
    public async Task<NatsOpts> GetOptionsAsync(CancellationToken cancellationToken)
    {
        if (_timeProvider.GetUtcNow() < _refreshAt)
        {
            return _current.Options;
        }

        var refreshed = await TryPrepareAsync(_appSlug, cancellationToken);
        if (refreshed is null)
        {
            _refreshAt = _timeProvider.GetUtcNow() + RetryAfterFailure;
            return _current.Options;
        }

        _current = refreshed;
        _refreshAt = RefreshDeadline(refreshed.Token);
        return _current.Options;
    }

    // A token with no readable exp is treated as never needing a refresh (Keycloak always
    // sets one, so this is purely defensive).
    private static DateTimeOffset RefreshDeadline(string token) =>
        OidcTokenClaims.ReadExpiry(token) is { } expiry ? expiry - RefreshMargin : DateTimeOffset.MaxValue;

    private static async Task<CalloutConnectionInfo?> TryPrepareAsync(string appSlug, CancellationToken cancellationToken)
    {
        try
        {
            return await CalloutConnection.PrepareAsync(
                appSlug, clientId: "notes-web", username: "note-user", password: "note-pass", cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await Console.Error.WriteLineAsync($"LoadService: token fetch failed ({ex.GetType().Name}: {ex.Message}).");
            return null;
        }
    }
}
