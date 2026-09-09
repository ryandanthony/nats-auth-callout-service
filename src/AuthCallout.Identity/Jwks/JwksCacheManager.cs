// -----------------------------------------------------------------------
// <copyright file="JwksCacheManager.cs" company="Ryan Anthony">
// Copyright (c) Ryan Anthony. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using System.Collections.Concurrent;
using AuthCallout.Contracts.Config;
using AuthCallout.Contracts.Seams;
using Microsoft.IdentityModel.Tokens;

namespace AuthCallout.Identity.Jwks;

/// <summary>
/// Keeps every configured issuer's JWKS warm (doc 08 §JWKS cache manager): one
/// <see cref="ConfigurationManager{T}"/> per distinct issuer authority (de-duplicated across
/// applications), prewarmed before <c>Serving</c>, proactively refreshed on an interval, and
/// refreshed single-flight on demand when <see cref="AuthCallout.Identity.Validation.TokenValidator"/>
/// encounters an unknown <c>kid</c>. Tracks the issuer set from
/// <see cref="IActiveConfigSource"/> change notifications so an added/removed application's
/// issuer is prewarmed/dropped accordingly.
/// </summary>
public sealed class JwksCacheManager : IJwksCacheManager, IJwksKeyProvider, IDisposable
{
    private readonly ConcurrentDictionary<string, IssuerJwksState> _issuers = new(StringComparer.Ordinal);
    private readonly IActiveConfigSource _activeConfigSource;
    private readonly JwksSettings _settings;
    private readonly IssuerConfigurationManagerFactory _configurationManagerFactory;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="JwksCacheManager"/> class.</summary>
    /// <param name="activeConfigSource">The active config source to track the issuer set from.</param>
    /// <param name="settings">Bootstrap JWKS cache manager settings (default refresh interval, fetch timeout).</param>
    public JwksCacheManager(IActiveConfigSource activeConfigSource, JwksSettings settings)
        : this(activeConfigSource, settings, new IssuerConfigurationManagerFactory())
    {
    }

    /// <summary>Initializes a new instance of the <see cref="JwksCacheManager"/> class with an injectable configuration-manager factory (test seam: substitutes a stub IdP's document retriever).</summary>
    /// <param name="activeConfigSource">The active config source to track the issuer set from.</param>
    /// <param name="settings">Bootstrap JWKS cache manager settings (default refresh interval, fetch timeout).</param>
    /// <param name="configurationManagerFactory">Builds the per-issuer <see cref="ConfigurationManager{T}"/>.</param>
    public JwksCacheManager(
        IActiveConfigSource activeConfigSource, JwksSettings settings, IssuerConfigurationManagerFactory configurationManagerFactory)
    {
        ArgumentNullException.ThrowIfNull(activeConfigSource);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(configurationManagerFactory);

        _activeConfigSource = activeConfigSource;
        _settings = settings;
        _configurationManagerFactory = configurationManagerFactory;
        _activeConfigSource.Changed += OnActiveConfigChanged;

        if (_activeConfigSource.Current is { } current)
        {
            SyncIssuers(current.Routing);
        }
    }

    /// <inheritdoc />
    public async Task PrewarmAllAsync(CancellationToken cancellationToken)
    {
        SyncIssuers(_activeConfigSource.Current?.Routing ?? RoutingConfig.Empty);

        var states = _issuers.Values.ToArray();
        var refreshTasks = states.Select(state => state.RefreshAsync(cancellationToken));
        await Task.WhenAll(refreshTasks).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task RefreshAsync(string issuerAuthority, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(issuerAuthority);

        return _issuers.TryGetValue(issuerAuthority, out var state)
            ? state.RefreshAsync(cancellationToken)
            : Task.CompletedTask;
    }

    /// <inheritdoc />
    public IReadOnlyList<JwksIssuerHealth> Health =>
        [.. _issuers.Values.Select(ToHealth)];

    /// <inheritdoc />
    public IReadOnlyList<SecurityKey> GetSigningKeys(string issuerAuthority)
    {
        ArgumentNullException.ThrowIfNull(issuerAuthority);

        return _issuers.TryGetValue(issuerAuthority, out var state) ? state.GetSigningKeys() : [];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _activeConfigSource.Changed -= OnActiveConfigChanged;
        foreach (var state in _issuers.Values)
        {
            state.Dispose();
        }

        _disposed = true;
    }

    private void OnActiveConfigChanged(object? sender, ActiveConfigChangedEventArgs e) => SyncIssuers(e.NewConfig.Routing);

    /// <summary>
    /// Reconciles tracked per-issuer state against the routing aggregate's current issuer set
    /// (doc 08: "add issuer -&gt; prewarm it; last app for an issuer removed -&gt; drop state").
    /// Adding an issuer here does not itself fetch (callers decide when to await that, e.g.
    /// <see cref="PrewarmAllAsync"/> or a background proactive-refresh loop owned by the host);
    /// this only ensures the tracked set matches the config.
    /// </summary>
    private void SyncIssuers(RoutingConfig routing)
    {
        var desiredAuthorities = new HashSet<string>(routing.IssuerIndex.Keys, StringComparer.Ordinal);

        foreach (var authority in desiredAuthorities)
        {
            if (_issuers.ContainsKey(authority))
            {
                continue;
            }

            var issuer = routing.IssuerIndex[authority][0].Issuer;
            var configurationManager = _configurationManagerFactory.Create(issuer, _settings);
            _issuers.TryAdd(authority, new IssuerJwksState(authority, configurationManager));
        }

        foreach (var authority in _issuers.Keys)
        {
            if (desiredAuthorities.Contains(authority))
            {
                continue;
            }

            if (_issuers.TryRemove(authority, out var removed))
            {
                removed.Dispose();
            }
        }
    }

    private static JwksIssuerHealth ToHealth(IssuerJwksState state) =>
        new(state.Authority, IsHealthy: state.LastRefreshSucceeded, HasUsableKeys: state.HasEverSucceeded, state.LastRefreshed);
}
