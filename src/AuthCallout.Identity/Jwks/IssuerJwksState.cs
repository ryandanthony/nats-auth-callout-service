// -----------------------------------------------------------------------
// <copyright file="IssuerJwksState.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using Microsoft.IdentityModel.Tokens;

namespace AuthCallout.Identity.Jwks;

/// <summary>
/// Per-issuer JWKS cache state owned by <see cref="JwksCacheManager"/>: the underlying
/// <see cref="IssuerFetcher"/>, single-flight coordination for on-demand refreshes, and the
/// freshness bookkeeping surfaced via <c>JwksIssuerHealth</c> (doc 08 §JWKS cache manager).
/// One instance per distinct issuer authority, shared across every application configured
/// with that issuer (doc 04 §Derived, not stored: issuers are de-duplicated across
/// applications).
/// </summary>
internal sealed class IssuerJwksState
{
    private readonly Lock _inFlightLock = new();
    private readonly IssuerFetcher _fetcher;
    private readonly TimeProvider _timeProvider;
    private volatile bool _hasEverSucceeded;
    private volatile bool _lastRefreshSucceeded;
    private DateTimeOffset? _lastRefreshed;
    private volatile IReadOnlyList<SecurityKey> _cachedSigningKeys = [];
    private Task? _inFlightRefresh;

    /// <summary>Initializes a new instance of the <see cref="IssuerJwksState"/> class for one issuer.</summary>
    /// <param name="authority">The issuer authority this state tracks.</param>
    /// <param name="fetcher">Performs this issuer's discovery/JWKS fetch, unconditionally, on every call.</param>
    /// <param name="timeProvider">Time source for <see cref="LastRefreshed"/> (test seam; defaults to <see cref="TimeProvider.System"/>).</param>
    internal IssuerJwksState(string authority, IssuerFetcher fetcher, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(authority);
        ArgumentNullException.ThrowIfNull(fetcher);
        Authority = authority;
        _fetcher = fetcher;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Gets the issuer authority this state tracks.</summary>
    /// <value>The issuer authority.</value>
    internal string Authority { get; }

    /// <summary>Gets when this issuer's JWKS was last <b>successfully</b> fetched, or null if never.</summary>
    /// <value>The last successful fetch time, or null.</value>
    internal DateTimeOffset? LastRefreshed => _lastRefreshed;

    /// <summary>Gets a value indicating whether at least one fetch has ever succeeded for this issuer (so <see cref="GetSigningKeys"/> can return usable, even if stale, keys).</summary>
    /// <value>True once the first successful fetch has completed.</value>
    internal bool HasEverSucceeded => _hasEverSucceeded;

    /// <summary>Gets a value indicating whether the most recent refresh attempt succeeded.</summary>
    /// <value>True when the last attempted refresh succeeded.</value>
    internal bool LastRefreshSucceeded => _lastRefreshSucceeded;

    /// <summary>
    /// Fetches (or, if a fetch is already in flight, awaits the <b>same</b> fetch) this
    /// issuer's current configuration (doc 08 §Single-flight on demand): concurrent callers
    /// collapse into the one upstream fetch already running rather than each issuing their
    /// own, verified by every caller observing the identical underlying <see cref="Task"/>
    /// rather than merely being serialized behind a gate. Always performs a genuine fetch (see
    /// <see cref="IssuerFetcher"/>); this type is the sole owner of any "is it too soon to
    /// refresh again" decision, and currently makes none — every explicit refresh request
    /// (prewarm, unknown-kid retry, proactive interval) is honored.
    /// </summary>
    /// <param name="cancellationToken">Cancellation for the fetch.</param>
    /// <returns>A task that completes when the single-flight fetch (this caller's or a concurrent one's) finishes.</returns>
    internal Task RefreshAsync(CancellationToken cancellationToken)
    {
        Task inFlight;
        lock (_inFlightLock)
        {
            _inFlightRefresh ??= RunRefreshAsync(cancellationToken);
            inFlight = _inFlightRefresh;
        }

        return inFlight;
    }

    private async Task RunRefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var configuration = await _fetcher.FetchAsync(cancellationToken).ConfigureAwait(false);
            _cachedSigningKeys = [.. configuration.SigningKeys];
            _hasEverSucceeded = true;
            _lastRefreshSucceeded = true;
            _lastRefreshed = _timeProvider.GetUtcNow();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fetch failures are health/log concerns (doc 08: "Degraded stale-but-usable"),
            // never something RefreshAsync should throw for — IJwksCacheManager.RefreshAsync's
            // contract is "attempt finished", not "attempt succeeded".
            _lastRefreshSucceeded = false;
        }
        finally
        {
            lock (_inFlightLock)
            {
                _inFlightRefresh = null;
            }
        }
    }

    /// <summary>Gets every signing key currently cached for this issuer (last known good, even if stale).</summary>
    /// <returns>Every currently cached signing key; empty when nothing has ever been fetched.</returns>
    internal IReadOnlyList<SecurityKey> GetSigningKeys() => _cachedSigningKeys;
}
