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
/// <see cref="IssuerJwksState"/> per distinct issuer authority (de-duplicated across
/// applications), prewarmed before <c>Serving</c>, proactively refreshed on an interval, and
/// refreshed single-flight on demand when <see cref="AuthCallout.Identity.Validation.TokenValidator"/>
/// encounters an unknown <c>kid</c>. Tracks the issuer set from
/// <see cref="IActiveConfigSource"/> change notifications so an added/removed application's
/// issuer is prewarmed/dropped accordingly.
/// </summary>
public sealed class JwksCacheManager : IJwksCacheManager, IJwksKeyProvider, IDisposable
{
    private readonly ConcurrentDictionary<string, TrackedIssuer> _issuers = new(StringComparer.Ordinal);
    private readonly IActiveConfigSource _activeConfigSource;
    private readonly JwksSettings _settings;
    private readonly IssuerConfigurationManagerFactory _fetcherFactory;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="JwksCacheManager"/> class.</summary>
    /// <param name="activeConfigSource">The active config source to track the issuer set from.</param>
    /// <param name="settings">Bootstrap JWKS cache manager settings (default refresh interval, fetch timeout).</param>
    public JwksCacheManager(IActiveConfigSource activeConfigSource, JwksSettings settings)
        : this(activeConfigSource, settings, new IssuerConfigurationManagerFactory())
    {
    }

    /// <summary>Initializes a new instance of the <see cref="JwksCacheManager"/> class with an injectable fetcher factory (test seam: substitutes a stub IdP's document retriever).</summary>
    /// <param name="activeConfigSource">The active config source to track the issuer set from.</param>
    /// <param name="settings">Bootstrap JWKS cache manager settings (default refresh interval, fetch timeout).</param>
    /// <param name="fetcherFactory">Builds the per-issuer <see cref="IssuerFetcher"/>.</param>
    public JwksCacheManager(
        IActiveConfigSource activeConfigSource, JwksSettings settings, IssuerConfigurationManagerFactory fetcherFactory)
    {
        ArgumentNullException.ThrowIfNull(activeConfigSource);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(fetcherFactory);

        _activeConfigSource = activeConfigSource;
        _settings = settings;
        _fetcherFactory = fetcherFactory;
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

        var tracked = _issuers.Values.ToArray();
        var refreshTasks = tracked.Select(t => t.State.RefreshAsync(cancellationToken));
        await Task.WhenAll(refreshTasks);

        foreach (var t in tracked)
        {
            t.EnsureProactiveRefreshStarted();
        }
    }

    /// <inheritdoc />
    public Task RefreshAsync(string issuerAuthority, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(issuerAuthority);

        return _issuers.TryGetValue(issuerAuthority, out var tracked)
            ? tracked.State.RefreshAsync(cancellationToken)
            : Task.CompletedTask;
    }

    /// <inheritdoc />
    public IReadOnlyList<JwksIssuerHealth> Health =>
        [.. _issuers.Values.Select(static t => ToHealth(t.State))];

    /// <inheritdoc />
    public IReadOnlyList<SecurityKey> GetSigningKeys(string issuerAuthority)
    {
        ArgumentNullException.ThrowIfNull(issuerAuthority);

        return _issuers.TryGetValue(issuerAuthority, out var tracked) ? tracked.State.GetSigningKeys() : [];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _activeConfigSource.Changed -= OnActiveConfigChanged;
        foreach (var tracked in _issuers.Values)
        {
            tracked.Dispose();
        }

        _disposed = true;
    }

    private void OnActiveConfigChanged(object? sender, ActiveConfigChangedEventArgs e) => SyncIssuers(e.NewConfig.Routing);

    /// <summary>
    /// Reconciles tracked per-issuer state against the routing aggregate's current issuer set
    /// (doc 08: "add issuer -&gt; prewarm it; last app for an issuer removed -&gt; drop state").
    /// Adding an issuer here does not itself fetch (callers decide when to await that, e.g.
    /// <see cref="PrewarmAllAsync"/>); this only ensures the tracked set matches the config.
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
            var fetcher = _fetcherFactory.Create(issuer);
            var state = new IssuerJwksState(authority, fetcher);
            var refreshInterval = (issuer.RefreshInterval ?? _settings.RefreshInterval).Value;
            _issuers.TryAdd(authority, new TrackedIssuer(state, refreshInterval));
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

    /// <summary>
    /// Pairs an <see cref="IssuerJwksState"/> with its own proactive-refresh loop (doc 08:
    /// "re-fetches each issuer's JWKS on a configurable interval so keys are renewed ahead of
    /// expiry"). The loop is started once prewarm has completed for this issuer (no point
    /// scheduling a proactive refresh before the first fetch has even happened) and stops when
    /// this instance is disposed (issuer removed from config, or the whole manager shutting
    /// down).
    /// </summary>
    private sealed class TrackedIssuer : IDisposable
    {
        private readonly PeriodicTimer _timer;
        private readonly CancellationTokenSource _cts = new();
        private Task? _loopTask;
        private readonly Lock _startLock = new();

        public TrackedIssuer(IssuerJwksState state, TimeSpan refreshInterval)
        {
            State = state;
            _timer = new PeriodicTimer(refreshInterval);
        }

        public IssuerJwksState State { get; }

        public void EnsureProactiveRefreshStarted()
        {
            if (_loopTask is not null)
            {
                return;
            }

            lock (_startLock)
            {
                _loopTask ??= Task.Run(RunLoopAsync);
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _timer.Dispose();
            _cts.Dispose();
        }

        private async Task RunLoopAsync()
        {
            try
            {
                while (await _timer.WaitForNextTickAsync(_cts.Token))
                {
                    await State.RefreshAsync(_cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on Dispose/shutdown.
            }
        }
    }
}
