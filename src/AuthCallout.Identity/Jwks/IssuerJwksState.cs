// -----------------------------------------------------------------------
// <copyright file="IssuerJwksState.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace AuthCallout.Identity.Jwks;

/// <summary>
/// Per-issuer JWKS cache state owned by <see cref="JwksCacheManager"/>: the underlying
/// <see cref="ConfigurationManager{T}"/>, single-flight coordination for on-demand refreshes,
/// and the freshness bookkeeping surfaced via <c>JwksIssuerHealth</c> (doc 08 §JWKS
/// cache manager). One instance per distinct issuer authority, shared
/// across every application configured with that issuer (doc 04
/// §Derived, not stored: issuers are de-duplicated across applications).
/// </summary>
internal sealed class IssuerJwksState : IDisposable
{
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly TimeProvider _timeProvider;
    private volatile bool _hasEverSucceeded;
    private volatile bool _lastRefreshSucceeded;
    private DateTimeOffset? _lastRefreshed;
    private volatile IReadOnlyList<SecurityKey> _cachedSigningKeys = [];

    /// <summary>Initializes a new instance of the <see cref="IssuerJwksState"/> class for one issuer.</summary>
    /// <param name="authority">The issuer authority this state tracks.</param>
    /// <param name="configurationManager">The underlying discovery/JWKS configuration manager for this issuer.</param>
    /// <param name="timeProvider">Time source for <see cref="LastRefreshed"/> (test seam; defaults to <see cref="TimeProvider.System"/>).</param>
    internal IssuerJwksState(string authority, ConfigurationManager<OpenIdConnectConfiguration> configurationManager, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(authority);
        ArgumentNullException.ThrowIfNull(configurationManager);
        Authority = authority;
        ConfigurationManager = configurationManager;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Gets the issuer authority this state tracks.</summary>
    /// <value>The issuer authority.</value>
    internal string Authority { get; }

    /// <summary>Gets the underlying discovery/JWKS configuration manager for this issuer.</summary>
    /// <value>The configuration manager.</value>
    internal ConfigurationManager<OpenIdConnectConfiguration> ConfigurationManager { get; }

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
    /// Fetches (or, if a fetch is already in flight, awaits) this issuer's current
    /// configuration exactly once concurrently (doc 08 §Single-flight on demand): concurrent
    /// callers collapse into the one upstream fetch already running rather than each issuing
    /// their own.
    /// </summary>
    /// <param name="cancellationToken">Cancellation for the fetch.</param>
    /// <returns>A task that completes when the single-flight fetch (this caller's or a concurrent one's) finishes.</returns>
    internal async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ConfigurationManager.RequestRefresh();
            var configuration = await ConfigurationManager.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
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
            _refreshGate.Release();
        }
    }

    /// <summary>Gets every signing key currently cached for this issuer (last known good, even if stale).</summary>
    /// <returns>Every currently cached signing key; empty when nothing has ever been fetched.</returns>
    internal IReadOnlyList<SecurityKey> GetSigningKeys() => _cachedSigningKeys;

    /// <inheritdoc />
    public void Dispose() => _refreshGate.Dispose();
}
