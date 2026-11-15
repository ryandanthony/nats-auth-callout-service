// -----------------------------------------------------------------------
// <copyright file="ICalloutTokenProvider.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace AuthCallout.Client.Tokens;

/// <summary>
/// The seam <see cref="Connection.CalloutNatsConnection"/> depends on for a bearer OIDC token:
/// a not-about-to-expire access token for the authenticated connect path, plus a way to force
/// a re-mint (ADR 0021 rule 2: "invalidate the cached token, force a refresh through the token
/// provider"). Implementations never downgrade a missing/expired token to anonymous — that
/// decision belongs to the caller (ADR 0021 rule 3).
/// </summary>
public interface ICalloutTokenProvider
{
    /// <summary>
    /// Gets a current, not-about-to-expire access token, or null if none is available
    /// (ADR 0021 rule 3: the managed connection must fail immediately on null rather than
    /// falling through to an anonymous connect).
    /// </summary>
    /// <param name="cancellationToken">Cancellation for the token fetch/refresh.</param>
    /// <returns>The current access token, or null.</returns>
    ValueTask<string?> GetTokenAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Forces the provider to drop any cached token so the next <see cref="GetTokenAsync"/>
    /// re-mints (ADR 0021 rule 2: the one forced-refresh retry after an Authorization
    /// Violation).
    /// </summary>
    /// <param name="cancellationToken">Cancellation for the invalidation (and any eager re-mint it triggers).</param>
    /// <returns>A task that completes once the cached token has been dropped.</returns>
    ValueTask InvalidateAsync(CancellationToken cancellationToken);
}
